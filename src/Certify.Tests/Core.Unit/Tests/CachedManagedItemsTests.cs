using System.Linq;
using Certify.Models;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Listing an instance's certificates could fail with "Collection was modified" when an item notification changed
    /// the hub's cached list while a request was reading it.
    /// </summary>
    [TestClass]
    public class CachedManagedItemsTests
    {
        private const string InstanceId = "instance-1";

        [TestMethod]
        public void ItemChanges_LeaveAListBeingReadUnchanged()
        {
            var stateProvider = new InstanceManagementStateProvider(NullLogger<InstanceManagementStateProvider>.Instance);

            stateProvider.UpdateInstanceItemInfo(InstanceId, [
                new ManagedCertificate { Id = "a", Name = "A" },
                new ManagedCertificate { Id = "b", Name = "B" }
            ]);

            var beingRead = stateProvider.GetManagedInstanceItems()[InstanceId].Items;

            foreach (var _ in beingRead)
            {
                stateProvider.UpdateCachedManagedInstanceItem(InstanceId, new ManagedCertificate { Id = "c", Name = "C" });
                stateProvider.UpdateCachedManagedInstanceItem(InstanceId, new ManagedCertificate { Id = "a", Name = "A2" });
                stateProvider.DeleteCachedManagedInstanceItem(InstanceId, "b");
            }

            CollectionAssert.AreEqual(new[] { "a", "b" }, beingRead.Select(i => i.Id).ToList());
            Assert.AreEqual("A", beingRead[0].Name);

            var current = stateProvider.GetManagedInstanceItems()[InstanceId].Items;

            CollectionAssert.AreEqual(new[] { "a", "c" }, current.Select(i => i.Id).ToList(), "An updated item should keep its position, a new one is added and a deleted one removed.");
            Assert.AreEqual("A2", current[0].Name);
        }

        [TestMethod]
        public void FullListKeepsItemChangesReportedWhileItWasFetched()
        {
            var stateProvider = new InstanceManagementStateProvider(NullLogger<InstanceManagementStateProvider>.Instance);

            stateProvider.UpdateInstanceItemInfo(InstanceId, [
                new ManagedCertificate { Id = "a", Name = "A" },
                new ManagedCertificate { Id = "b", Name = "B" }
            ]);

            // reported before the full list is requested, so the list includes it
            stateProvider.UpdateCachedManagedInstanceItem(InstanceId, new ManagedCertificate { Id = "b", Name = "B2" });

            var changeSequence = stateProvider.GetItemChangeSequence();

            // reported while the instance puts the list together
            stateProvider.UpdateCachedManagedInstanceItem(InstanceId, new ManagedCertificate { Id = "a", Name = "A2" });
            stateProvider.UpdateCachedManagedInstanceItem(InstanceId, new ManagedCertificate { Id = "d", Name = "D" });
            stateProvider.DeleteCachedManagedInstanceItem(InstanceId, "c");

            // the list was put together before those changes
            stateProvider.UpdateInstanceItemInfo(InstanceId, [
                new ManagedCertificate { Id = "a", Name = "A" },
                new ManagedCertificate { Id = "b", Name = "B3" },
                new ManagedCertificate { Id = "c", Name = "C" }
            ], changeSequence);

            var current = stateProvider.GetManagedInstanceItems()[InstanceId].Items;

            CollectionAssert.AreEqual(new[] { "a", "b", "d" }, current.Select(i => i.Id).ToList());
            Assert.AreEqual("A2", current[0].Name, "A change reported while the list was fetched is newer than the listed copy.");
            Assert.AreEqual("B3", current[1].Name, "A change reported before the list was requested is in the list.");

            // a later list includes everything reported before it was requested
            stateProvider.UpdateInstanceItemInfo(InstanceId, [new ManagedCertificate { Id = "a", Name = "A4" }], stateProvider.GetItemChangeSequence());

            Assert.AreEqual("A4", stateProvider.GetManagedInstanceItems()[InstanceId].Items.Single().Name);
        }
    }
}
