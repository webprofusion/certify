using System;
using System.Collections.Generic;
using System.Reflection;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Reporting;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Tests.Server.Core.Unit
{
    [TestClass]
    public class ManagementWorkerTests
    {
        [TestMethod]
        public void RemoveManagedInstanceRuntimeState_RemovesCachedItemsAndStatusSummary()
        {
            var stateProvider = new InstanceManagementStateProvider(Mock.Of<ILogger<InstanceManagementStateProvider>>());
            var instanceId = "instance-1";

            stateProvider.UpdateInstanceConnectionInfo("conn-1", new ManagedInstanceInfo
            {
                InstanceId = instanceId,
                Title = "Instance 1",
                DateLastReported = DateTimeOffset.UtcNow
            });
            stateProvider.UpdateInstanceItemInfo(instanceId,
            [
                new ManagedCertificate { Id = "cert-1", Name = "Cert 1" }
            ]);
            stateProvider.UpdateInstanceStatusSummary(instanceId, new StatusSummary { InstanceId = instanceId, Total = 1 });

            stateProvider.RemoveManagedInstanceRuntimeState(instanceId);

            Assert.IsFalse(stateProvider.HasItemsForManagedInstance(instanceId));
            Assert.IsFalse(stateProvider.HasStatusSummaryForManagedInstance(instanceId));
            Assert.AreEqual(1, stateProvider.GetConnectedInstances().Count, "Connection tracking should remain intact after cache eviction.");
        }

        [TestMethod]
        public void DoWork_EvictsRuntimeCache_ForInstancesNotSeenSinceExpiry()
        {
            var staleInstance = new ManagedInstanceInfo
            {
                InstanceId = "stale-1",
                Title = "Stale Instance",
                DateLastReported = DateTimeOffset.UtcNow.AddMinutes(-31)
            };

            var stateProvider = new Mock<IInstanceManagementStateProvider>();
            stateProvider.Setup(x => x.GetConnectedInstances()).Returns([staleInstance]);
            stateProvider
                .Setup(x => x.GetInstancesNotSeenSince(It.IsAny<DateTimeOffset>()))
                .Returns([staleInstance]);

            var hubContext = new Mock<IHubContext<InstanceManagementHub>>();
            var hubClientContext = new Mock<IHubContext<InstanceManagementHub, IInstanceManagementHub>>();
            hubClientContext.SetupGet(x => x.Clients).Returns(Mock.Of<IHubClients<IInstanceManagementHub>>());

            var managementApi = new ManagementAPI(
                stateProvider.Object,
                hubClientContext.Object,
                Mock.Of<ILogger<ManagementAPI>>());

            var worker = new ManagementWorker(
                Mock.Of<ILogger<ManagementWorker>>(),
                hubContext.Object,
                stateProvider.Object,
                managementApi);

            InvokeDoWork(worker);

            stateProvider.Verify(x => x.RemoveManagedInstanceRuntimeState("stale-1"), Times.Once);
            stateProvider.Verify(x => x.GetInstancesNotSeenSince(It.Is<DateTimeOffset>(cutoff => cutoff <= DateTimeOffset.UtcNow.AddMinutes(-29) && cutoff >= DateTimeOffset.UtcNow.AddMinutes(-31))), Times.Once);
            stateProvider.Verify(x => x.TakeItemRefreshIfDue(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
        }

        [TestMethod]
        [Description("A status summary whose LastUpdateId differs from the previous one makes the instance's items due a full refresh, once")]
        public void StatusSummary_LastUpdateIdChanged_MakesItemRefreshDue()
        {
            var stateProvider = new InstanceManagementStateProvider(Mock.Of<ILogger<InstanceManagementStateProvider>>());
            var instanceId = "instance-1";

            stateProvider.UpdateInstanceStatusSummary(instanceId, new StatusSummary { InstanceId = instanceId, LastUpdateId = 5 });
            Assert.IsFalse(stateProvider.TakeItemRefreshIfDue(instanceId, TimeSpan.Zero), "the first summary has nothing to compare with");

            stateProvider.UpdateInstanceStatusSummary(instanceId, new StatusSummary { InstanceId = instanceId, LastUpdateId = 5 });
            Assert.IsFalse(stateProvider.TakeItemRefreshIfDue(instanceId, TimeSpan.Zero), "nothing has changed");

            stateProvider.UpdateInstanceStatusSummary(instanceId, new StatusSummary { InstanceId = instanceId, LastUpdateId = 7 });
            Assert.IsTrue(stateProvider.TakeItemRefreshIfDue(instanceId, TimeSpan.Zero));
            Assert.IsFalse(stateProvider.TakeItemRefreshIfDue(instanceId, TimeSpan.Zero), "a refresh is taken once");

            stateProvider.UpdateInstanceStatusSummary(instanceId, new StatusSummary { InstanceId = instanceId, LastUpdateId = 0 });
            Assert.IsTrue(stateProvider.TakeItemRefreshIfDue(instanceId, TimeSpan.Zero), "a restarted instance counts from zero again");
        }

        [TestMethod]
        [Description("An instance's items are not refreshed again within the interval, but the refresh stays due until they are")]
        public void TakeItemRefreshIfDue_WithinInterval_StaysDue()
        {
            var stateProvider = new InstanceManagementStateProvider(Mock.Of<ILogger<InstanceManagementStateProvider>>());
            var instanceId = "instance-1";

            stateProvider.MarkItemRefreshDue(instanceId);
            Assert.IsTrue(stateProvider.TakeItemRefreshIfDue(instanceId, TimeSpan.FromMinutes(2)));

            stateProvider.MarkItemRefreshDue(instanceId);
            Assert.IsFalse(stateProvider.TakeItemRefreshIfDue(instanceId, TimeSpan.FromMinutes(2)), "a refresh was taken within the interval");
            Assert.IsTrue(stateProvider.TakeItemRefreshIfDue(instanceId, TimeSpan.Zero), "the refresh is still due once the interval has passed");
        }

        [TestMethod]
        [Description("An instance which does not send its items when they are due a refresh is asked again later")]
        public void DoWork_ItemRefreshNotAnswered_IsDueAgain()
        {
            var instance = new ManagedInstanceInfo
            {
                InstanceId = "instance-1",
                Title = "Instance 1",
                DateLastReported = DateTimeOffset.UtcNow
            };

            var stateProvider = new Mock<IInstanceManagementStateProvider>();
            stateProvider.Setup(x => x.GetConnectedInstances()).Returns([instance]);
            stateProvider.Setup(x => x.GetInstancesNotSeenSince(It.IsAny<DateTimeOffset>())).Returns([]);
            stateProvider.Setup(x => x.TakeItemRefreshIfDue("instance-1", It.IsAny<TimeSpan>())).Returns(true);

            var hubClientContext = new Mock<IHubContext<InstanceManagementHub, IInstanceManagementHub>>();
            hubClientContext.SetupGet(x => x.Clients).Returns(Mock.Of<IHubClients<IInstanceManagementHub>>());

            var worker = new ManagementWorker(
                Mock.Of<ILogger<ManagementWorker>>(),
                Mock.Of<IHubContext<InstanceManagementHub>>(),
                stateProvider.Object,
                new ManagementAPI(stateProvider.Object, hubClientContext.Object, Mock.Of<ILogger<ManagementAPI>>()));

            // the instance has no connection to send the request on, so no items come back
            InvokeDoWork(worker);

            stateProvider.Verify(x => x.MarkItemRefreshDue("instance-1"), Times.Once);
            stateProvider.Verify(x => x.UpdateInstanceItemInfo(It.IsAny<string>(), It.IsAny<List<ManagedCertificate>>()), Times.Never);
        }

        private static void InvokeDoWork(ManagementWorker worker)
        {
            var method = typeof(ManagementWorker).GetMethod("DoWork", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(method, "Expected ManagementWorker.DoWork to exist.");

            method.Invoke(worker, [null]);
        }
    }
}
