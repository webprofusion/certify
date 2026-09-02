using System;
using System.Collections.ObjectModel;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for how often a subscription is attempted. An update the source has announced is applied on the next
    /// pass, which is the point of being told about one, but an attempt which keeps failing is spaced out by the same
    /// failure hold as any other attempt for the item rather than repeated on every pass indefinitely
    /// </summary>
    [TestClass]
    public class SubscriptionRetryPacingTests
    {
        private static ManagedCertificate CreateSubscription(int renewalFailureCount = 0, DateTimeOffset? dateLastRenewalAttempt = null, string? retrievalMode = null, string? pendingVersion = "v2", DateTimeOffset? dateLastPoll = null)
        {
            var now = DateTimeOffset.UtcNow;

            return new ManagedCertificate
            {
                Id = "subscriber-item",
                Name = "Subscriber Item",
                IncludeInAutoRenew = true,
                ItemType = ManagedCertificateType.SSL_ExternalSubscription,
                DateStart = now.AddDays(-1),
                DateRenewed = now.AddDays(-1),
                DateExpiry = now.AddDays(6),
                DateLastRenewalAttempt = dateLastRenewalAttempt ?? now.AddMinutes(-5),
                LastRenewalStatus = renewalFailureCount > 0 ? RequestState.Error : RequestState.Success,
                RenewalFailureCount = renewalFailureCount,
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = retrievalMode ?? ExternalCertificateRetrievalModes.Auto,
                    ExternalReference = "instance-a/cert-1",
                    PollIntervalMinutes = 30,
                    PendingSourceVersion = pendingVersion,
                    LastSourceVersion = "v1",
                    DateLastPoll = dateLastPoll
                },
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "sub.example.com",
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>()
                }
            };
        }

        [TestMethod, Description("A newly announced update is attempted on the next pass")]
        public void NewPendingUpdateIsAttemptedImmediately()
        {
            var item = CreateSubscription();

            Assert.IsTrue(CertifyManager.ShouldProcessSubscription(item, item.ExternalSource));
        }

        [TestMethod, Description("The first few failed attempts are retried at the normal pass cadence")]
        public void EarlyFailedAttemptsAreNotHeldBack()
        {
            var now = DateTimeOffset.UtcNow;
            var item = CreateSubscription(renewalFailureCount: LifetimeHealthThresholds.FailuresBeforeBackoff - 1, dateLastRenewalAttempt: now.AddMinutes(-5));

            Assert.IsFalse(ManagedCertificate.IsHeldByFailureBackoff(item, now));
            Assert.IsTrue(CertifyManager.ShouldProcessSubscription(item, item.ExternalSource, now),
                "A brief problem at the source should recover within a pass or two");
        }

        [TestMethod, Description("An update which keeps failing is spaced out instead of retried every pass")]
        public void RepeatedlyFailingUpdateIsBackedOff()
        {
            var now = DateTimeOffset.UtcNow;
            var item = CreateSubscription(renewalFailureCount: LifetimeHealthThresholds.FailuresBeforeBackoff, dateLastRenewalAttempt: now.AddMinutes(-5));

            Assert.IsTrue(ManagedCertificate.IsHeldByFailureBackoff(item, now),
                "Five minutes after the last failure is too soon once attempts have started failing");
            Assert.IsFalse(CertifyManager.ShouldProcessSubscription(item, item.ExternalSource, now),
                "The pass should leave the item alone entirely while it is held");
        }

        [TestMethod, Description("A held update is attempted again once its wait has elapsed")]
        public void HeldUpdateIsAttemptedOnceWaitElapses()
        {
            var now = DateTimeOffset.UtcNow;
            var item = CreateSubscription(renewalFailureCount: LifetimeHealthThresholds.FailuresBeforeBackoff, dateLastRenewalAttempt: now.AddMinutes(-5));

            var backoff = ManagedCertificate.CalculateFailureBackoff(item);

            Assert.IsGreaterThan(0, backoff.WaitHrs);
            Assert.IsTrue(CertifyManager.ShouldProcessSubscription(item, item.ExternalSource, backoff.NextAttemptByDate));
        }

        [TestMethod, Description("A pull-only subscription whose polls keep failing is held by the same back off")]
        public void FailingPullOnlyPollIsHeldByTheSameBackOff()
        {
            // a poll which keeps failing would otherwise contact the source at every poll interval for as long as the
            // problem lasted, with the item's failure count climbing on every one
            var now = DateTimeOffset.UtcNow;
            var item = CreateSubscription(renewalFailureCount: LifetimeHealthThresholds.FailuresBeforeBackoff, dateLastRenewalAttempt: now.AddMinutes(-5), retrievalMode: ExternalCertificateRetrievalModes.Pull, pendingVersion: null, dateLastPoll: now.AddMinutes(-60));

            Assert.IsTrue(CertifyManager.ShouldPollSource(item, item.ExternalSource, now), "On its own the poll interval says the source is due to be polled");
            Assert.IsFalse(CertifyManager.ShouldProcessSubscription(item, item.ExternalSource, now), "The hold keeps the failing poll spaced out");

            item.RenewalFailureCount = 0;
            item.LastRenewalStatus = RequestState.Success;

            Assert.IsTrue(CertifyManager.ShouldProcessSubscription(item, item.ExternalSource, now), "A subscription which is not failing polls on its interval");
        }

        [TestMethod, Description("A genuinely new version announced by the source clears the hold, because it is new work")]
        public void NewVersionClearsTheHold()
        {
            var now = DateTimeOffset.UtcNow;
            var item = CreateSubscription(renewalFailureCount: 10, dateLastRenewalAttempt: now.AddMinutes(-5), pendingVersion: null);

            Assert.IsTrue(ManagedCertificate.IsHeldByFailureBackoff(item, now), "After repeated failures the item is backing off");

            Assert.IsTrue(CertifyManager.TryRecordPendingSubscriptionUpdate(item, item.ExternalSource, "v2"), "A version the item does not hold is recorded as pending");

            Assert.AreEqual("v2", item.ExternalSource.PendingSourceVersion);
            Assert.AreEqual(0, item.RenewalFailureCount, "The new version is attempted without waiting out the hold for the old one");
            Assert.IsFalse(ManagedCertificate.IsHeldByFailureBackoff(item, now));
            Assert.IsTrue(CertifyManager.ShouldProcessSubscription(item, item.ExternalSource, now), "The update is fetched on the next pass");

            item.RenewalFailureCount = 3;

            Assert.IsFalse(CertifyManager.TryRecordPendingSubscriptionUpdate(item, item.ExternalSource, "v2"), "The same version announced again is already recorded");
            Assert.AreEqual(3, item.RenewalFailureCount, "A repeated announcement does not clear anything");
        }

        [TestMethod, Description("A version the item already holds is not recorded as an update")]
        public void AlreadyHeldVersionIsNotRecorded()
        {
            var item = CreateSubscription(renewalFailureCount: 3, pendingVersion: null);

            Assert.IsFalse(CertifyManager.TryRecordPendingSubscriptionUpdate(item, item.ExternalSource, "v1"), "v1 is the version last deployed");
            Assert.IsNull(item.ExternalSource.PendingSourceVersion);
            Assert.AreEqual(3, item.RenewalFailureCount);
        }
    }
}
