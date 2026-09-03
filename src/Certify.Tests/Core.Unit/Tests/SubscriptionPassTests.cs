using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Providers;
using Certify.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for the scheduled pass which checks each configured certificate subscription. It decides which items are
    /// looked at at all, so an item wrongly excluded here simply stops updating, and one wrongly included is a request
    /// against a source which had nothing for us - or, if it collides with a renewal driven request, two requests
    /// against the same item at once
    /// </summary>
    [TestClass]
    public class SubscriptionPassTests
    {
        /// <summary>
        /// An item store which records what the pass asked it for, so a test can tell the difference between the pass
        /// standing down and the pass running and finding nothing
        /// </summary>
        private class RecordingItemStore : IManagedItemStore
        {
            private readonly List<ManagedCertificate> _items;

            public RecordingItemStore(params ManagedCertificate[] items) => _items = items.ToList();

            public int FindCallCount { get; private set; }
            public List<string> GetByIdCalls { get; } = new();

            /// <summary>
            /// Run when the pass lists the items to process, so a test can act while a pass is in progress
            /// </summary>
            public Action OnFind { get; set; }

            public Task<List<ManagedCertificate>> Find(ManagedCertificateFilter filter)
            {
                FindCallCount++;
                OnFind?.Invoke();

                return Task.FromResult(_items.ToList());
            }

            public Task<ManagedCertificate> GetById(string siteId)
            {
                GetByIdCalls.Add(siteId);

                return Task.FromResult(_items.FirstOrDefault(i => i.Id == siteId));
            }

            public bool Init(string connectionString, ILog log, string instanceId = null) => true;
            public Task<bool> IsInitialised() => Task.FromResult(true);
            public Task DeleteAll() => Task.CompletedTask;
            public Task StoreAll(IEnumerable<ManagedCertificate> list) => Task.CompletedTask;
            public Task Delete(ManagedCertificate site) => Task.CompletedTask;
            public Task DeleteByName(string nameStartsWith) => Task.CompletedTask;
            public Task<long> CountAll(ManagedCertificateFilter filter) => Task.FromResult((long)_items.Count);
            public Task<ManagedCertificate> Update(ManagedCertificate managedCertificate) => Task.FromResult(managedCertificate);
            public Task PerformMaintenance() => Task.CompletedTask;
        }

        private static CertifyManager CreateManager(IManagedItemStore itemStore)
        {
            var manager = new CertifyManager();

            var field = typeof(CertifyManager).GetField("_itemManager", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "_itemManager should be available for testing");
            field.SetValue(manager, itemStore);

            return manager;
        }

        private static async Task RunSubscriptionTasks(CertifyManager manager, CancellationToken cancellationToken = default)
        {
            var method = typeof(CertifyManager).GetMethod("PerformSubscriptionTasks", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "PerformSubscriptionTasks should be available for testing");

            await (Task)method.Invoke(manager, new object[] { cancellationToken });
        }

        private static async Task<List<ManagedCertificate>> GetSubscriptionTargets(CertifyManager manager)
        {
            var method = typeof(CertifyManager).GetMethod("GetSubscriptionTargets", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "GetSubscriptionTargets should be available for testing");

            return await (Task<List<ManagedCertificate>>)method.Invoke(manager, null);
        }

        private static ConcurrentDictionary<string, DateTimeOffset> GetMaintenanceWindowWaits(CertifyManager manager)
        {
            var field = typeof(CertifyManager).GetField("_subscriptionsAwaitingMaintenanceWindow", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "_subscriptionsAwaitingMaintenanceWindow should be available for testing");

            return (ConcurrentDictionary<string, DateTimeOffset>)field.GetValue(manager);
        }

        private static void SetPrivateField(CertifyManager manager, string fieldName, object value)
        {
            var field = typeof(CertifyManager).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{fieldName} should be available for testing");
            field.SetValue(manager, value);
        }

        private static ManagedCertificate CreateSubscription(string id, string pendingVersion = null, string externalReference = "instance-1/managed-cert-1")
        {
            var now = DateTimeOffset.UtcNow;

            return new ManagedCertificate
            {
                Id = id,
                Name = $"Subscription {id}",
                IncludeInAutoRenew = true,
                ItemType = ManagedCertificateType.SSL_ExternalSubscription,
                DateStart = now.AddDays(-1),
                DateRenewed = now.AddDays(-1),
                DateExpiry = now.AddDays(89),
                DateLastRenewalAttempt = now.AddDays(-1),
                LastRenewalStatus = RequestState.Success,
                CertificateThumbprintHash = "ABC123",
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                    ExternalReference = externalReference,
                    PollIntervalMinutes = 30,
                    DateLastPoll = now,
                    LastSourceVersion = "v1",
                    PendingSourceVersion = pendingVersion
                }
            };
        }

        [TestMethod, Description("The subscription pass stands down while the data store is unavailable")]
        public async Task PassStandsDownInDegradedMode()
        {
            var store = new RecordingItemStore(CreateSubscription("item-1", pendingVersion: "v2"));
            var manager = CreateManager(store);

            manager.HandleDataStoreFailure("Data store write test failed: disk full", "default", "sqlite");
            Assert.IsTrue(manager.IsInDegradedMode);

            await RunSubscriptionTasks(manager);

            // the outcome of a fetch and deployment could not be recorded, so the source is not contacted at all -
            // the same certificate would otherwise be fetched and redeployed on every pass
            Assert.AreEqual(0, store.FindCallCount, "The pass must not even list its items while their outcome cannot be recorded");
        }

        [TestMethod, Description("Only subscriptions configured well enough to fetch from are processed")]
        public async Task OnlyActionableSubscriptionsAreTargets()
        {
            var configured = CreateSubscription("configured");
            var unconfigured = CreateSubscription("unconfigured", externalReference: null);

            var acmeItem = new ManagedCertificate
            {
                Id = "acme-item",
                Name = "Standard Item",
                ItemType = ManagedCertificateType.SSL_ACME
            };

            var externallyManaged = new ManagedCertificate
            {
                Id = "externally-managed",
                Name = "Discovered Item",
                ItemType = ManagedCertificateType.SSL_ExternallyManaged
            };

            var manager = CreateManager(new RecordingItemStore(configured, unconfigured, acmeItem, externallyManaged));

            var targets = await GetSubscriptionTargets(manager);

            // an unconfigured subscription has nothing to fetch and nothing to fetch it from, and neither of the other
            // two items has a source at all - none of them may fall through to a request
            Assert.HasCount(1, targets);
            Assert.AreEqual("configured", targets[0].Id);
        }

        [TestMethod, Description("A maintenance window wait is dropped once the item is no longer a subscription we process")]
        public async Task StaleMaintenanceWindowWaitsAreDropped()
        {
            var store = new RecordingItemStore();
            var manager = CreateManager(store);

            // an item which was waiting for its window and has since been deleted or reconfigured
            GetMaintenanceWindowWaits(manager)["deleted-item"] = DateTimeOffset.UtcNow.AddHours(-2);

            await RunSubscriptionTasks(manager);

            Assert.IsFalse(manager.IsSubscriptionAwaitingMaintenanceWindow("deleted-item"),
                "Tracking for items which are no longer processed would otherwise accumulate for the life of the service");
        }

        [TestMethod, Description("A maintenance window wait is kept for an item which is still a subscription we process")]
        public async Task MaintenanceWindowWaitIsKeptForACurrentTarget()
        {
            var item = CreateSubscription("waiting-item");
            var manager = CreateManager(new RecordingItemStore(item));

            GetMaintenanceWindowWaits(manager)[item.Id] = DateTimeOffset.UtcNow.AddHours(-2);

            await RunSubscriptionTasks(manager);

            Assert.IsTrue(manager.IsSubscriptionAwaitingMaintenanceWindow(item.Id),
                "The item is still waiting for its window, so the wait is not logged again on every pass");
        }

        [TestMethod, Description("A subscription with no update and no poll due is left untouched")]
        public async Task SubscriptionWhichIsNotDueIsNotRequested()
        {
            // polled within its interval, renewal not due and nothing announced by the source
            var store = new RecordingItemStore(CreateSubscription("not-due"));
            var manager = CreateManager(store);

            await RunSubscriptionTasks(manager);

            Assert.AreEqual(1, store.FindCallCount, "The pass runs and lists its items");
            Assert.IsEmpty(store.GetByIdCalls,
                "A request which would only record a no-op status against the item, and report it to connected UI clients, is not made");
        }

        [TestMethod, Description("A subscription with an update waiting is picked up by the pass")]
        public async Task SubscriptionWithAPendingUpdateIsRequested()
        {
            var store = new RecordingItemStore(CreateSubscription("has-update", pendingVersion: "v2"));
            var manager = CreateManager(store);

            await RunSubscriptionTasks(manager);

            Assert.Contains("has-update", store.GetByIdCalls, "An announced update is applied on the next pass rather than at the next poll interval");
        }

        [TestMethod, Description("A subscription with a request already in progress is skipped by the pass")]
        public async Task SubscriptionWithARequestInProgressIsSkipped()
        {
            var item = CreateSubscription("in-progress", pendingVersion: "v2");
            var store = new RecordingItemStore(item);
            var manager = CreateManager(store);

            var requestsInProgress = (ConcurrentDictionary<string, DateTimeOffset?>)typeof(CertifyManager)
                .GetField("_renewalsInProgress", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(manager);

            requestsInProgress[item.Id] = DateTimeOffset.Now;

            await RunSubscriptionTasks(manager);

            // a renewal driven request holds the item's place for its whole request, including its deployment tasks;
            // running this pass against it as well would put two requests on the same item
            Assert.IsEmpty(store.GetByIdCalls, "The item is already being worked on, so the pass leaves it alone");
            Assert.IsTrue(requestsInProgress.ContainsKey(item.Id), "The pass must not release a place it did not take");
        }

        [TestMethod, Description("Only one subscription pass runs at a time")]
        public async Task PassDoesNotRunWhileAnotherIsInProgress()
        {
            var store = new RecordingItemStore(CreateSubscription("item-1", pendingVersion: "v2"));
            var manager = CreateManager(store);

            // a pass is already running
            SetPrivateField(manager, "_isSubscriptionTaskRunning", 1);

            await RunSubscriptionTasks(manager);

            Assert.AreEqual(0, store.FindCallCount, "A second pass would process the same items alongside the one already running");
        }

        [TestMethod, Description("An update arriving during a pass is serviced by a follow up pass")]
        public async Task UpdateArrivingDuringAPassIsServicedByAnotherPass()
        {
            var store = new RecordingItemStore(CreateSubscription("item-1"));
            var manager = CreateManager(store);

            store.OnFind = () =>
            {
                // the source announces an update while this pass is selecting its items, so the update arrived too
                // late for this pass to see it
                if (store.FindCallCount == 1)
                {
                    SetPrivateField(manager, "_isSubscriptionPassRequested", 1);
                }
            };

            await RunSubscriptionTasks(manager);

            Assert.AreEqual(2, store.FindCallCount, "The request made during the pass is serviced rather than left until the next scheduled pass");
        }

        [TestMethod, Description("Requests made during a pass are coalesced into a single follow up pass")]
        public async Task RepeatedRequestsDuringAPassAreCoalesced()
        {
            var store = new RecordingItemStore(CreateSubscription("item-1"));
            var manager = CreateManager(store);

            store.OnFind = () =>
            {
                if (store.FindCallCount == 1)
                {
                    // a batch of updates arriving together, each asking for a pass
                    SetPrivateField(manager, "_isSubscriptionPassRequested", 1);
                    SetPrivateField(manager, "_isSubscriptionPassRequested", 1);
                    SetPrivateField(manager, "_isSubscriptionPassRequested", 1);
                }
            };

            await RunSubscriptionTasks(manager);

            Assert.AreEqual(2, store.FindCallCount, "A batch of updates arriving together does not queue a pass each");
        }

        [TestMethod, Description("A cancelled pass does not start another one")]
        public async Task CancelledPassDoesNotRepeat()
        {
            var store = new RecordingItemStore(CreateSubscription("item-1"));
            var manager = CreateManager(store);

            using var cancellation = new CancellationTokenSource();

            store.OnFind = () =>
            {
                SetPrivateField(manager, "_isSubscriptionPassRequested", 1);
                cancellation.Cancel();
            };

            await RunSubscriptionTasks(manager, cancellation.Token);

            Assert.AreEqual(1, store.FindCallCount, "A shutdown is not held up by servicing another pass");
        }

        [TestMethod, Description("The pass releases its gate so the next scheduled pass can run")]
        public async Task PassReleasesItsGate()
        {
            var store = new RecordingItemStore(CreateSubscription("item-1"));
            var manager = CreateManager(store);

            await RunSubscriptionTasks(manager);
            await RunSubscriptionTasks(manager);

            Assert.AreEqual(2, store.FindCallCount, "A pass which did not release its gate would stop subscriptions updating for good");
        }
    }
}
