using System;
using System.Collections.ObjectModel;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for how often a subscription with an update waiting is attempted. An update is applied on the next pass,
    /// which is the point of being told about one, but an update which cannot be applied must not be retried on every
    /// pass indefinitely
    /// </summary>
    [TestClass]
    public class SubscriptionRetryPacingTests
    {
        private static ManagedCertificate CreateSubscriptionWithPendingUpdate(int subscriptionFailureCount = 0, DateTimeOffset? dateLastPoll = null, int pollIntervalMinutes = 30)
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
                DateLastRenewalAttempt = now.AddMinutes(-5),
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                    ExternalReference = "instance-a/cert-1",
                    PollIntervalMinutes = pollIntervalMinutes,
                    PendingSourceVersion = "v2",
                    SubscriptionFailureCount = subscriptionFailureCount,
                    DateLastPoll = dateLastPoll
                },
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "sub.example.com",
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>()
                }
            };
        }

        [TestMethod, Description("A newly notified update is attempted on the next pass")]
        public void NewPendingUpdateIsAttemptedImmediately()
        {
            var item = CreateSubscriptionWithPendingUpdate();

            Assert.IsTrue(CertifyManager.IsPendingSubscriptionUpdateRetryDue(item.ExternalSource));
            Assert.IsTrue(CertifyManager.ShouldProcessSubscription(item, item.ExternalSource));
        }

        [TestMethod, Description("The first few failed attempts are retried at the normal pass cadence")]
        public void EarlyFailedAttemptsAreNotHeldBack()
        {
            var now = DateTimeOffset.UtcNow;
            var item = CreateSubscriptionWithPendingUpdate(
                subscriptionFailureCount: LifetimeHealthThresholds.FailuresBeforeBackoff - 1,
                dateLastPoll: now.AddMinutes(-5));

            Assert.IsTrue(CertifyManager.IsPendingSubscriptionUpdateRetryDue(item.ExternalSource, now),
                "A brief problem at the source should recover within a pass or two");
        }

        [TestMethod, Description("An update which keeps failing is spaced out instead of retried every pass")]
        public void RepeatedlyFailingUpdateIsBackedOff()
        {
            var now = DateTimeOffset.UtcNow;
            var item = CreateSubscriptionWithPendingUpdate(
                subscriptionFailureCount: LifetimeHealthThresholds.FailuresBeforeBackoff,
                dateLastPoll: now.AddMinutes(-5));

            Assert.IsFalse(CertifyManager.IsPendingSubscriptionUpdateRetryDue(item.ExternalSource, now),
                "Five minutes after the last failure is too soon once attempts have started failing");

            Assert.IsFalse(CertifyManager.ShouldProcessSubscription(item, item.ExternalSource, now),
                "The pass should leave the item alone entirely while it is held");
        }

        [TestMethod, Description("A held update is attempted again once its wait has elapsed")]
        public void HeldUpdateIsAttemptedOnceWaitElapses()
        {
            var now = DateTimeOffset.UtcNow;
            var item = CreateSubscriptionWithPendingUpdate(
                subscriptionFailureCount: LifetimeHealthThresholds.FailuresBeforeBackoff,
                dateLastPoll: now.AddMinutes(-31));

            Assert.IsTrue(CertifyManager.IsPendingSubscriptionUpdateRetryDue(item.ExternalSource, now));
        }

        [TestMethod, Description("The wait between attempts grows with consecutive failures, up to a ceiling")]
        public void RetryWaitGrowsAndIsCapped()
        {
            var source = CreateSubscriptionWithPendingUpdate(pollIntervalMinutes: 30).ExternalSource;

            source.SubscriptionFailureCount = LifetimeHealthThresholds.FailuresBeforeBackoff;
            var firstWait = CertifyManager.GetPendingSubscriptionUpdateRetryWaitMinutes(source);

            source.SubscriptionFailureCount = LifetimeHealthThresholds.FailuresBeforeBackoff + 2;
            var laterWait = CertifyManager.GetPendingSubscriptionUpdateRetryWaitMinutes(source);

            Assert.AreEqual(30, firstWait, "The first held attempt waits the subscription's own poll interval");
            Assert.IsGreaterThan(firstWait, laterWait, "Further failures space the attempts out further");

            source.SubscriptionFailureCount = 1000;

            Assert.AreEqual(48 * 60, CertifyManager.GetPendingSubscriptionUpdateRetryWaitMinutes(source),
                "The wait is capped at 48hrs however many times it has failed");
        }

        [TestMethod, Description("The retry pacing uses the subscription's own failure count, not the item's overall one")]
        public void PacingIsIndependentOfUnrelatedFailures()
        {
            var now = DateTimeOffset.UtcNow;

            // an item whose deployment task has been failing for days, but whose source is answering fine
            var item = CreateSubscriptionWithPendingUpdate(subscriptionFailureCount: 0, dateLastPoll: now.AddMinutes(-1));
            item.RenewalFailureCount = 50;

            Assert.IsTrue(CertifyManager.IsPendingSubscriptionUpdateRetryDue(item.ExternalSource, now),
                "A broken deployment task must not delay a certificate update which is perfectly fine");
        }
    }
}
