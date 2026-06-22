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
            stateProvider.Verify(x => x.GetManagedInstanceStatusSummary(It.IsAny<string>()), Times.Never);
        }

        private static void InvokeDoWork(ManagementWorker worker)
        {
            var method = typeof(ManagementWorker).GetMethod("DoWork", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(method, "Expected ManagementWorker.DoWork to exist.");

            method.Invoke(worker, [null]);
        }
    }
}
