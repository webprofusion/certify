using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for the gate which keeps two requests off the same managed certificate at once. Every path which can
    /// start a request takes the item's place through it - the scheduled renewal pass, the subscription pass, a user
    /// or hub initiated request - so a failure here means two concurrent certificate orders for one item, against the
    /// CA's duplicate certificate limits. A place which is never given up would be just as bad the other way, leaving
    /// the item unable to renew for good, so a request which never finished is eventually treated as stuck
    /// </summary>
    [TestClass]
    public class RequestConcurrencyGateTests
    {
        private static ManagedCertificate CreateItem(string id = "item-1")
        {
            return new ManagedCertificate { Id = id, Name = $"Test {id}" };
        }

        private static bool InvokeTryBeginRequest(CertifyManager manager, ManagedCertificate item)
        {
            var method = typeof(CertifyManager).GetMethod("TryBeginRequest", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "TryBeginRequest should be available for testing");

            return (bool)method.Invoke(manager, new object[] { item });
        }

        private static ConcurrentDictionary<string, DateTimeOffset?> GetRequestsInProgress(CertifyManager manager)
        {
            var field = typeof(CertifyManager).GetField("_renewalsInProgress", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "_renewalsInProgress should be available for testing");

            return (ConcurrentDictionary<string, DateTimeOffset?>)field.GetValue(manager);
        }

        /// <summary>
        /// How long a request may be in progress before a new request treats it as stuck. Read from the source rather
        /// than restated, so this test does not quietly stop covering the real limit if it is changed
        /// </summary>
        private static TimeSpan GetMaxRequestInProgressAge()
        {
            var field = typeof(CertifyManager).GetField("_maxRequestInProgressAge", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "_maxRequestInProgressAge should be available for testing");

            return (TimeSpan)field.GetValue(null);
        }

        [TestMethod, Description("The first request for an item takes its place")]
        public void FirstRequestTakesTheItemsPlace()
        {
            var manager = new CertifyManager();
            var item = CreateItem();

            Assert.IsTrue(InvokeTryBeginRequest(manager, item));
            Assert.IsTrue(GetRequestsInProgress(manager).ContainsKey(item.Id), "The item's place is held for the duration of the request");
        }

        [TestMethod, Description("A second request for the same item is refused while the first still holds its place")]
        public void SecondRequestForTheSameItemIsRefused()
        {
            var manager = new CertifyManager();
            var item = CreateItem();

            Assert.IsTrue(InvokeTryBeginRequest(manager, item));

            // this is what keeps a renewal driven request and the subscription pass off the same item: without it both
            // would order a certificate for it at once
            Assert.IsFalse(InvokeTryBeginRequest(manager, item), "A request is already in progress for the item, so a second must not start");
        }

        [TestMethod, Description("A request for a different item is not blocked by one already in progress")]
        public void RequestForADifferentItemIsNotBlocked()
        {
            var manager = new CertifyManager();

            Assert.IsTrue(InvokeTryBeginRequest(manager, CreateItem("item-1")));
            Assert.IsTrue(InvokeTryBeginRequest(manager, CreateItem("item-2")), "The gate is per item, so a batch can still run several items at once");
        }

        [TestMethod, Description("A new request can start once the previous one has given up the item's place")]
        public void RequestCanStartAgainOnceThePlaceIsReleased()
        {
            var manager = new CertifyManager();
            var item = CreateItem();

            Assert.IsTrue(InvokeTryBeginRequest(manager, item));

            // the caller removes the place once its request completes
            GetRequestsInProgress(manager).TryRemove(item.Id, out _);

            Assert.IsTrue(InvokeTryBeginRequest(manager, item), "The previous request has finished, so the next one for the item may start");
        }

        [TestMethod, Description("A place held beyond the stuck request limit is given up so the item can renew again")]
        public void StuckRequestIsGivenUpSoTheItemIsNotBlockedForGood()
        {
            var manager = new CertifyManager();
            var item = CreateItem();

            var stuckSince = DateTimeOffset.Now - GetMaxRequestInProgressAge() - TimeSpan.FromMinutes(1);
            GetRequestsInProgress(manager)[item.Id] = stuckSince;

            Assert.IsTrue(InvokeTryBeginRequest(manager, item), "A request which never finished must not block the item for good");

            var heldSince = GetRequestsInProgress(manager)[item.Id];
            Assert.IsTrue(heldSince > stuckSince, "The place is retaken by the new request rather than left holding the abandoned request's timestamp");
        }

        [TestMethod, Description("A long running request which is still within the limit keeps its place")]
        public void LongRunningRequestWithinTheLimitKeepsItsPlace()
        {
            var manager = new CertifyManager();
            var item = CreateItem();

            // several dns-01 identifiers each waiting for propagation, followed by slow deployment tasks, take far
            // longer than a few minutes - a request that long is still working, not stuck
            var startedAt = DateTimeOffset.Now - GetMaxRequestInProgressAge() + TimeSpan.FromMinutes(5);
            GetRequestsInProgress(manager)[item.Id] = startedAt;

            Assert.IsFalse(InvokeTryBeginRequest(manager, item), "The request is still within the allowed duration, so a second request must not start");
            Assert.AreEqual(startedAt, GetRequestsInProgress(manager)[item.Id], "The in progress request keeps the place it took");
        }

        [TestMethod, Description("The stuck request limit sits above the renewal batch timeout")]
        public void StuckRequestLimitSitsAboveTheBatchTimeout()
        {
            var batchTimeoutField = typeof(CertifyManager).GetField("_renewalBatchTimeout", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(batchTimeoutField, "_renewalBatchTimeout should be available for testing");

            var batchTimeout = (TimeSpan)batchTimeoutField.GetValue(null);

            // a request started early in a batch which is abandoned is still running when the next batch starts. Were
            // the two limits the same, the next batch would treat it as stuck and order a certificate again while the
            // first request was still working
            Assert.IsTrue(GetMaxRequestInProgressAge() > batchTimeout,
                "A request outliving an abandoned batch must not be treated as stuck by the batch which follows it");
        }

        [TestMethod, Description("Only one of several callers racing for the same item wins its place")]
        public async Task ConcurrentCallersRaceForOnePlace()
        {
            var manager = new CertifyManager();
            var item = CreateItem();

            // two callers can both find the item free before either records its request, so the place has to be taken
            // atomically rather than checked and then added
            var attempts = await Task.WhenAll(Enumerable.Range(0, 32)
                .Select(_ => Task.Run(() => InvokeTryBeginRequest(manager, item))));

            Assert.AreEqual(1, attempts.Count(granted => granted), "Exactly one caller may hold the item's place at a time");
        }
    }
}
