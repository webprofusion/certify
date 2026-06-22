using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Certify.Config;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    [TestClass]
    public class BindableBaseChangeTrackingTests
    {
        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public void ResetIsChanged_AttachesExistingNestedBindableProperties()
        {
            var managedCertificate = new ManagedCertificate();
            managedCertificate.ResetIsChanged(false);

            managedCertificate.RequestConfig.PrimaryDomain = "example.com";

            Assert.IsTrue(managedCertificate.IsChanged);
        }

        [TestMethod]
        public void ResetIsChanged_AttachesExistingDeploymentTaskItems()
        {
            var managedCertificate = new ManagedCertificate
            {
                PreRequestTasks = new ObservableCollection<DeploymentTaskConfig>
                {
                    new DeploymentTaskConfig
                    {
                        Id = "task-1",
                        TaskName = "Original Task"
                    }
                }
            };

            managedCertificate.ResetIsChanged(false);

            managedCertificate.PreRequestTasks[0].TaskName = "Updated Task";

            Assert.IsTrue(managedCertificate.IsChanged);
        }

        [TestMethod]
        public void NestedChange_BubblesThroughMultipleLevels()
        {
            var managedCertificate = new ManagedCertificate();
            managedCertificate.RequestConfig.Challenges.Add(new CertRequestChallengeConfig { ChallengeType = "http-01" });
            managedCertificate.ResetIsChanged(false);

            // mutate a grandchild item (root -> RequestConfig -> Challenges[0])
            managedCertificate.RequestConfig.Challenges[0].ChallengeType = "dns-01";

            Assert.IsTrue(managedCertificate.IsChanged, "Change to a grandchild item should bubble to the root.");
            Assert.IsTrue(managedCertificate.RequestConfig.IsChanged, "The intermediate node should also be marked changed.");
        }

        [TestMethod]
        public void AddingItemToNestedCollection_MarksRootChanged()
        {
            var managedCertificate = new ManagedCertificate();
            managedCertificate.ResetIsChanged(false);

            managedCertificate.RequestConfig.Challenges.Add(new CertRequestChallengeConfig { ChallengeType = "http-01" });

            Assert.IsTrue(managedCertificate.IsChanged);
        }

        [TestMethod]
        public void MutatingItemAddedAfterReset_MarksRootChanged()
        {
            var managedCertificate = new ManagedCertificate();
            managedCertificate.ResetIsChanged(false);

            var challenge = new CertRequestChallengeConfig { ChallengeType = "http-01" };
            managedCertificate.RequestConfig.Challenges.Add(challenge);
            managedCertificate.ResetIsChanged(false);

            // the item added after the reset should still be tracked
            challenge.ChallengeType = "dns-01";

            Assert.IsTrue(managedCertificate.IsChanged);
        }

        [TestMethod]
        public void MutatingRemovedItem_DoesNotMarkRootChanged()
        {
            var managedCertificate = new ManagedCertificate();
            var challenge = new CertRequestChallengeConfig { ChallengeType = "http-01" };
            managedCertificate.RequestConfig.Challenges.Add(challenge);
            managedCertificate.ResetIsChanged(false);

            managedCertificate.RequestConfig.Challenges.Remove(challenge);
            managedCertificate.ResetIsChanged(false);

            // the removed item should no longer be tracked
            challenge.ChallengeType = "dns-01";

            Assert.IsFalse(managedCertificate.IsChanged);
        }

        [TestMethod]
        public void ResetIsChanged_ClearsNestedChangedFlags()
        {
            var managedCertificate = new ManagedCertificate();
            managedCertificate.RequestConfig.Challenges.Add(new CertRequestChallengeConfig { ChallengeType = "http-01" });

            managedCertificate.RequestConfig.Challenges[0].ChallengeType = "dns-01";
            Assert.IsTrue(managedCertificate.RequestConfig.Challenges[0].IsChanged);

            managedCertificate.ResetIsChanged(false);

            Assert.IsFalse(managedCertificate.IsChanged);
            Assert.IsFalse(managedCertificate.RequestConfig.IsChanged);
            Assert.IsFalse(managedCertificate.RequestConfig.Challenges[0].IsChanged);
        }

        [TestMethod, Description("Measure change tracking impact on a deeply nested model with a large (10,000 item) DomainOptions collection where a single random item changes")]
        public void LargeDomainOptions_SingleRandomChange_BubblesToRootEfficiently()
        {
            const int itemCount = 10000;

            // build a complex managed certificate with a large nested collection of trackable items
            var buildTimer = Stopwatch.StartNew();
            var managedCertificate = new ManagedCertificate
            {
                Name = "large-scenario.example.com",
                DomainOptions = new ObservableCollection<DomainOption>(
                    Enumerable.Range(0, itemCount).Select(i => new DomainOption
                    {
                        Domain = $"host{i}.example.com",
                        Title = $"host{i}.example.com",
                        IsPrimaryDomain = i == 0,
                        IsSelected = i == 0
                    }))
            };
            buildTimer.Stop();

            // establish a clean baseline (clears nested flags and (re)attaches change tracking subscriptions)
            var resetTimer = Stopwatch.StartNew();
            managedCertificate.ResetIsChanged(false);
            resetTimer.Stop();

            Assert.IsFalse(managedCertificate.IsChanged, "Model should be clean immediately after reset.");
            Assert.IsTrue(managedCertificate.DomainOptions.All(d => !d.IsChanged), "All nested items should be clean after reset.");

            // mutate exactly one randomly chosen item (select it)
            var randomIndex = new Random(12345).Next(itemCount);
            var target = managedCertificate.DomainOptions[randomIndex];

            var changeTimer = Stopwatch.StartNew();
            target.IsSelected = !target.IsSelected;
            changeTimer.Stop();

            // a single nested change must bubble up to mark the whole model changed
            Assert.IsTrue(target.IsChanged, "The mutated item should be marked changed.");
            Assert.IsTrue(managedCertificate.IsChanged, "A single nested change should bubble up to the root model.");

            // only the one changed item should be dirty; the rest of the large graph must remain clean
            Assert.AreEqual(1, managedCertificate.DomainOptions.Count(d => d.IsChanged), "Only the single mutated item should be marked changed.");

            TestContext.WriteLine($"Items: {itemCount:N0}");
            TestContext.WriteLine($"Build graph:           {buildTimer.ElapsedMilliseconds} ms");
            TestContext.WriteLine($"ResetIsChanged(false): {resetTimer.ElapsedMilliseconds} ms (clears flags + (re)subscribes)");
            TestContext.WriteLine($"Single random change:  {changeTimer.Elapsed.TotalMilliseconds:F3} ms (bubble to root)");
        }
    }
}
