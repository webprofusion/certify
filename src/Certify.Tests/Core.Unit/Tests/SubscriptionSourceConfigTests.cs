using System;
using System.Reflection;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for how a certificate subscription's own settings decide what it may do: which retrieval modes allow it
    /// to poll its source or be told of an update, how often it polls, and what it records when it is told. These
    /// decide whether an update is ever picked up at all, so a mode which is misread means a subscription which
    /// silently stops updating
    /// </summary>
    [TestClass]
    public class SubscriptionSourceConfigTests
    {
        private static bool InvokeIsPullModeEnabled(ExternalCertificateSubscription sourceConfig)
        {
            var method = typeof(CertifyManager).GetMethod("IsPullModeEnabled", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "IsPullModeEnabled should be available for testing");

            return (bool)method.Invoke(null, new object[] { sourceConfig });
        }

        private static bool InvokeIsPushModeEnabled(ExternalCertificateSubscription sourceConfig)
        {
            var method = typeof(CertifyManager).GetMethod("IsPushModeEnabled", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "IsPushModeEnabled should be available for testing");

            return (bool)method.Invoke(null, new object[] { sourceConfig });
        }

        private static ExternalCertificateSubscription CreateSource(string retrievalMode, int pollIntervalMinutes = 30, DateTimeOffset? dateLastPoll = null)
        {
            return new ExternalCertificateSubscription
            {
                SourceType = ExternalCertificateSourceTypes.ManagementHub,
                RetrievalMode = retrievalMode,
                ExternalReference = "instance-1/managed-cert-1",
                PollIntervalMinutes = pollIntervalMinutes,
                DateLastPoll = dateLastPoll
            };
        }

        private static ManagedCertificate CreateSubscription(ExternalCertificateSubscription source)
        {
            var now = DateTimeOffset.UtcNow;

            return new ManagedCertificate
            {
                Id = "subscriber-item",
                Name = "Subscriber Item",
                ItemType = ManagedCertificateType.SSL_ExternalSubscription,
                IncludeInAutoRenew = true,
                DateStart = now.AddDays(-1),
                DateRenewed = now.AddDays(-1),
                DateExpiry = now.AddDays(89),
                DateLastRenewalAttempt = now.AddDays(-1),
                LastRenewalStatus = RequestState.Success,
                CertificateThumbprintHash = "ABC123",
                ExternalSource = source
            };
        }

        [TestMethod, Description("Pull mode polls its source and is not sent updates")]
        public void PullModeOnlyPolls()
        {
            var source = CreateSource(ExternalCertificateRetrievalModes.Pull);

            Assert.IsTrue(InvokeIsPullModeEnabled(source));
            Assert.IsFalse(InvokeIsPushModeEnabled(source), "A pull only subscription is never told about an update, it goes and looks");
        }

        [TestMethod, Description("Push mode is sent updates and does not poll its source")]
        public void PushModeOnlyReceives()
        {
            var source = CreateSource(ExternalCertificateRetrievalModes.Push);

            Assert.IsFalse(InvokeIsPullModeEnabled(source));
            Assert.IsTrue(InvokeIsPushModeEnabled(source));
        }

        [TestMethod, Description("Auto mode both polls its source and accepts updates it is sent")]
        public void AutoModeDoesBoth()
        {
            var source = CreateSource(ExternalCertificateRetrievalModes.Auto);

            Assert.IsTrue(InvokeIsPullModeEnabled(source));
            Assert.IsTrue(InvokeIsPushModeEnabled(source), "Auto is the default, so it has to cover a source which can only push");
        }

        [TestMethod, Description("A subscription with no retrieval mode set falls back to polling")]
        public void MissingRetrievalModeFallsBackToPull()
        {
            var source = CreateSource(retrievalMode: null);

            // an item stored before the mode existed has none, and polling is the mode which needs nothing of the source
            Assert.IsTrue(InvokeIsPullModeEnabled(source));
            Assert.IsFalse(InvokeIsPushModeEnabled(source));
        }

        [TestMethod, Description("Retrieval modes are matched regardless of case")]
        [DataRow("pull", true, false)]
        [DataRow("PUSH", false, true)]
        [DataRow("aUtO", true, true)]
        public void RetrievalModeMatchingIgnoresCase(string retrievalMode, bool expectPull, bool expectPush)
        {
            var source = CreateSource(retrievalMode);

            Assert.AreEqual(expectPull, InvokeIsPullModeEnabled(source));
            Assert.AreEqual(expectPush, InvokeIsPushModeEnabled(source));
        }

        [TestMethod, Description("An unrecognised retrieval mode enables nothing rather than defaulting to a mode")]
        public void UnrecognisedRetrievalModeEnablesNothing()
        {
            var source = CreateSource("SomethingElse");

            // guessing a mode here would have a misconfigured subscription quietly contacting a source it was never
            // meant to, so neither mode is assumed
            Assert.IsFalse(InvokeIsPullModeEnabled(source));
            Assert.IsFalse(InvokeIsPushModeEnabled(source));
        }

        [TestMethod, Description("A subscription which has never polled its source is due to poll immediately")]
        public void NeverPolledSourceIsDue()
        {
            var source = CreateSource(ExternalCertificateRetrievalModes.Pull, dateLastPoll: null);

            Assert.IsTrue(CertifyManager.ShouldPollSource(CreateSubscription(source), source));
        }

        [TestMethod, Description("A source polled within its interval is not polled again")]
        public void SourcePolledWithinItsIntervalIsNotDue()
        {
            var now = DateTimeOffset.UtcNow;
            var source = CreateSource(ExternalCertificateRetrievalModes.Pull, pollIntervalMinutes: 30, dateLastPoll: now.AddMinutes(-29));

            Assert.IsFalse(CertifyManager.ShouldPollSource(CreateSubscription(source), source, now));
        }

        [TestMethod, Description("A source is polled again once its interval has elapsed")]
        public void SourceIsPolledOnceItsIntervalElapses()
        {
            var now = DateTimeOffset.UtcNow;
            var source = CreateSource(ExternalCertificateRetrievalModes.Pull, pollIntervalMinutes: 30, dateLastPoll: now.AddMinutes(-30));

            Assert.IsTrue(CertifyManager.ShouldPollSource(CreateSubscription(source), source, now));
        }

        [TestMethod, Description("A poll interval of zero or less uses the default rather than polling on every pass")]
        [DataRow(0)]
        [DataRow(-15)]
        public void NonPositivePollIntervalUsesTheDefault(int pollIntervalMinutes)
        {
            var now = DateTimeOffset.UtcNow;

            // an interval which is missing or nonsensical would otherwise put the source on every pass, which is what
            // the interval exists to prevent
            var withinDefault = CreateSource(ExternalCertificateRetrievalModes.Pull, pollIntervalMinutes, dateLastPoll: now.AddMinutes(-20));
            Assert.IsFalse(CertifyManager.ShouldPollSource(CreateSubscription(withinDefault), withinDefault, now),
                "The default interval applies, so a source polled 20 minutes ago is not polled again");

            var beyondDefault = CreateSource(ExternalCertificateRetrievalModes.Pull, pollIntervalMinutes, dateLastPoll: now.AddMinutes(-31));
            Assert.IsTrue(CertifyManager.ShouldPollSource(CreateSubscription(beyondDefault), beyondDefault, now),
                "Once the default interval has elapsed the source is polled");
        }

        [TestMethod, Description("A push only subscription never polls its source, however long since it last did")]
        public void PushOnlySubscriptionNeverPolls()
        {
            var now = DateTimeOffset.UtcNow;
            var source = CreateSource(ExternalCertificateRetrievalModes.Push, pollIntervalMinutes: 5, dateLastPoll: now.AddDays(-30));

            Assert.IsFalse(CertifyManager.ShouldPollSource(CreateSubscription(source), source, now));
        }

        [TestMethod, Description("An item with no source configuration is neither polled nor processed")]
        public void ItemWithNoSourceConfigurationIsNotProcessed()
        {
            var item = CreateSubscription(null);

            Assert.IsFalse(CertifyManager.ShouldPollSource(item, null));
            Assert.IsFalse(CertifyManager.ShouldProcessSubscription(item, null),
                "Callers pass the item's external source without checking it first, so a missing one has to be handled here");
        }

        [TestMethod, Description("An update announced without a version is still recorded, so it is fetched")]
        public void UpdateAnnouncedWithoutAVersionIsStillRecorded()
        {
            var source = CreateSource(ExternalCertificateRetrievalModes.Auto);
            source.LastSourceVersion = "v1";
            var item = CreateSubscription(source);

            // a source which does not say which version it has still has one we do not hold, and dropping the
            // announcement would leave the update sitting there until the next poll fell due
            Assert.IsTrue(CertifyManager.TryRecordPendingSubscriptionUpdate(item, source, sourceVersion: null));
            Assert.IsFalse(string.IsNullOrWhiteSpace(source.PendingSourceVersion), "A version marker is generated so the update is tracked as pending");
            Assert.AreNotEqual("v1", source.PendingSourceVersion);
            Assert.IsTrue(CertifyManager.HasPendingSubscriptionUpdate(source));
        }

        [TestMethod, Description("An announced version which differs only by case is treated as one we already hold")]
        public void AnnouncedVersionMatchingTheHeldVersionIsIgnoredRegardlessOfCase()
        {
            var source = CreateSource(ExternalCertificateRetrievalModes.Auto);
            source.LastSourceVersion = "ABC123";
            var item = CreateSubscription(source);

            // hub ETags are hex digests whose case is not significant, so treating a difference in case as a new
            // version would refetch and redeploy the same certificate on every announcement
            Assert.IsFalse(CertifyManager.TryRecordPendingSubscriptionUpdate(item, source, "abc123"));
            Assert.IsNull(source.PendingSourceVersion);
        }

        [TestMethod, Description("A version already awaiting deployment is not recorded a second time")]
        public void AnnouncedVersionAlreadyPendingIsIgnoredRegardlessOfCase()
        {
            var source = CreateSource(ExternalCertificateRetrievalModes.Auto);
            source.PendingSourceVersion = "ABC123";
            var item = CreateSubscription(source);

            Assert.IsFalse(CertifyManager.TryRecordPendingSubscriptionUpdate(item, source, "abc123"),
                "Repeated announcements of the same update must not each queue another pass");
        }

        [TestMethod, Description("Recording a new update clears the error from the attempt which failed and queues the next pass")]
        public void RecordingANewUpdateClearsTheSourceErrorAndQueuesTheNextPass()
        {
            var source = CreateSource(ExternalCertificateRetrievalModes.Auto);
            source.LastSourceVersion = "v1";
            source.LastError = "ManagementHub source returned 503";

            var item = CreateSubscription(source);
            item.DateNextScheduledRenewalAttempt = null;

            Assert.IsTrue(CertifyManager.TryRecordPendingSubscriptionUpdate(item, source, "v2"));

            Assert.IsNull(source.LastError, "The source has just spoken to us, so the last failure to reach it no longer describes it");
            Assert.IsNotNull(item.DateNextScheduledRenewalAttempt, "The update is applied when we hear about it rather than at the next scheduled interval");
        }

        [TestMethod, Description("Clearing the renewal trigger can leave a pending update in place for the pass which follows")]
        public void ClearingTheRenewalTriggerCanKeepThePendingUpdate()
        {
            var source = CreateSource(ExternalCertificateRetrievalModes.Auto);
            source.PendingSourceVersion = "v2";

            var item = CreateSubscription(source);
            item.DateNextScheduledRenewalAttempt = DateTimeOffset.UtcNow;

            CertifyManager.ClearSubscriptionRenewalTrigger(item, source, clearPendingSourceVersion: false);

            Assert.IsNull(item.DateNextScheduledRenewalAttempt);
            Assert.AreEqual("v2", source.PendingSourceVersion, "The update has not been applied yet, so it must still be waiting");
        }

        [TestMethod, Description("The sync status shown for a subscription reflects what last happened to it")]
        public void SyncStatusReportsWhatLastHappened()
        {
            var neverChecked = CreateSource(ExternalCertificateRetrievalModes.Auto);
            Assert.AreEqual("Awaiting First Sync", neverChecked.CurrentSyncStatus);

            var checkedWithNoVersion = CreateSource(ExternalCertificateRetrievalModes.Auto, dateLastPoll: DateTimeOffset.UtcNow);
            Assert.AreEqual("Checked", checkedWithNoVersion.CurrentSyncStatus, "The source answered but has not identified a version we hold");

            var inSync = CreateSource(ExternalCertificateRetrievalModes.Auto, dateLastPoll: DateTimeOffset.UtcNow);
            inSync.LastSourceVersion = "v1";
            Assert.AreEqual("In Sync", inSync.CurrentSyncStatus);

            var updateAvailable = CreateSource(ExternalCertificateRetrievalModes.Auto, dateLastPoll: DateTimeOffset.UtcNow);
            updateAvailable.LastSourceVersion = "v1";
            updateAvailable.PendingSourceVersion = "v2";
            Assert.AreEqual("Update Available", updateAvailable.CurrentSyncStatus);

            // a failure to reach the source takes precedence over everything else: it is the thing the operator has to act on
            var failing = CreateSource(ExternalCertificateRetrievalModes.Auto, dateLastPoll: DateTimeOffset.UtcNow);
            failing.LastSourceVersion = "v1";
            failing.PendingSourceVersion = "v2";
            failing.LastError = "ManagementHub source returned 403";
            Assert.AreEqual("Source Error", failing.CurrentSyncStatus);
        }

        [TestMethod, Description("The source item shown for a subscription prefers its name over its raw reference")]
        public void SourceItemDisplayPrefersTheRemoteName()
        {
            var withName = CreateSource(ExternalCertificateRetrievalModes.Auto);
            withName.SourceItemName = "Production Wildcard";
            Assert.AreEqual("Production Wildcard", withName.RemoteNameOrReferenceDisplay);

            var withoutName = CreateSource(ExternalCertificateRetrievalModes.Auto);
            Assert.AreEqual("instance-1/managed-cert-1", withoutName.RemoteNameOrReferenceDisplay, "Without a name the reference is the only thing identifying the remote item");

            var unconfigured = new ExternalCertificateSubscription();
            Assert.AreEqual("-", unconfigured.RemoteNameOrReferenceDisplay);
        }
    }
}
