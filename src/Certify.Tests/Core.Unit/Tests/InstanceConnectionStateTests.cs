using System;
using System.Linq;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// A dropped instance was reported as connected until the hub restarted, because marking it disconnected
    /// went back through the connection update, which always set the status to connected.
    /// </summary>
    [TestClass]
    public class InstanceConnectionStateTests
    {
        private const string InstanceId = "instance-1";

        private static InstanceManagementStateProvider CreateConnectedProvider()
        {
            var stateProvider = new InstanceManagementStateProvider(NullLogger<InstanceManagementStateProvider>.Instance);

            stateProvider.UpdateInstanceConnectionInfo("conn-1", new ManagedInstanceInfo
            {
                InstanceId = InstanceId,
                ConnectionStatus = ConnectionStatus.Connected,
                DateLastReported = DateTimeOffset.UtcNow
            });

            return stateProvider;
        }

        [TestMethod]
        public void Disconnect_ExcludesInstanceFromConnectedInstances()
        {
            var stateProvider = CreateConnectedProvider();

            stateProvider.UpdateInstanceConnectionStatus(InstanceId, ConnectionStatus.Disconnected);

            Assert.AreEqual(0, stateProvider.GetConnectedInstances().Count);
            Assert.IsNull(stateProvider.GetConnectionIdForInstance(InstanceId), "Commands should not be sent to a disconnected instance.");
            Assert.AreEqual(1, stateProvider.GetInstancesNotSeenSince(DateTimeOffset.UtcNow.AddMinutes(1)).Count(), "A disconnected instance should still be tracked so its cached state can expire.");
        }

        [TestMethod]
        public void Reconnect_OnNewConnection_ReportsConnected()
        {
            var stateProvider = CreateConnectedProvider();

            stateProvider.UpdateInstanceConnectionStatus(InstanceId, ConnectionStatus.Disconnected);
            stateProvider.UpdateInstanceConnectionInfo("conn-2", new ManagedInstanceInfo
            {
                InstanceId = InstanceId,
                ConnectionStatus = ConnectionStatus.Connected,
                DateLastReported = DateTimeOffset.UtcNow
            });

            var connected = stateProvider.GetConnectedInstances();

            Assert.AreEqual(1, connected.Count);
            Assert.AreEqual(ConnectionStatus.Connected, connected[0].ConnectionStatus);
            Assert.AreEqual("conn-2", stateProvider.GetConnectionIdForInstance(InstanceId));
        }

        [TestMethod]
        public void LateUpdate_FromDisconnectedConnection_DoesNotReportConnected()
        {
            var stateProvider = CreateConnectedProvider();

            stateProvider.UpdateInstanceConnectionStatus(InstanceId, ConnectionStatus.Disconnected);
            stateProvider.UpdateInstanceConnectionInfo("conn-1", new ManagedInstanceInfo
            {
                InstanceId = InstanceId,
                DateLastReported = DateTimeOffset.UtcNow
            });

            Assert.AreEqual(0, stateProvider.GetConnectedInstances().Count);
        }
    }
}
