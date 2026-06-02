using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Management;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Tests.Server.Core.Unit
{
    [TestClass]
    public class ManagementAPITests
    {
        [TestMethod]
        public async Task RejoinManagedInstance_ReturnsWarning_WhenInstanceIsNotConnected()
        {
            var stateProvider = new Mock<IInstanceManagementStateProvider>();
            stateProvider.Setup(x => x.GetManagementHubInstanceId()).Returns("hub-instance");
            stateProvider.Setup(x => x.GetConnectedInstances()).Returns([]);

            var hubClient = new Mock<IInstanceManagementHub>();
            var hubClients = new Mock<IHubClients<IInstanceManagementHub>>();
            var hubContext = new Mock<IHubContext<InstanceManagementHub, IInstanceManagementHub>>();
            hubContext.SetupGet(x => x.Clients).Returns(hubClients.Object);

            var managementApi = new ManagementAPI(
                stateProvider.Object,
                hubContext.Object,
                CreateManagerMock().Object,
                Mock.Of<ILogger<ManagementAPI>>());

            var result = await managementApi.RejoinManagedInstance("remote-1", null);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsWarning);
            Assert.AreEqual("Managed instance is not currently connected, so the rejoin command could not be sent.", result.Message);
            hubClient.Verify(x => x.SendCommandRequest(It.IsAny<InstanceCommandRequest>()), Times.Never);
        }

        [TestMethod]
        public async Task RejoinAllManagedInstances_SendsRejoinCommandToEligibleConnectedInstances()
        {
            var connectedInstances = new List<ManagedInstanceInfo>
            {
                new() { Id = "hub-instance", InstanceId = "hub-instance", Title = "Hub" },
                new() { Id = "remote-1", InstanceId = "remote-1", Title = "Remote 1" }
            };

            var stateProvider = new Mock<IInstanceManagementStateProvider>();
            stateProvider.Setup(x => x.GetManagementHubInstanceId()).Returns("hub-instance");
            stateProvider.Setup(x => x.GetConnectedInstances()).Returns(connectedInstances);
            stateProvider.Setup(x => x.GetConnectionIdForInstance("remote-1")).Returns("conn-1");

            var hubClient = new Mock<IInstanceManagementHub>();
            var hubClients = new Mock<IHubClients<IInstanceManagementHub>>();
            hubClients.Setup(x => x.Client("conn-1")).Returns(hubClient.Object);

            var hubContext = new Mock<IHubContext<InstanceManagementHub, IInstanceManagementHub>>();
            hubContext.SetupGet(x => x.Clients).Returns(hubClients.Object);

            var managementApi = new ManagementAPI(
                stateProvider.Object,
                hubContext.Object,
                CreateManagerMock().Object,
                Mock.Of<ILogger<ManagementAPI>>());

            var result = await managementApi.RejoinAllManagedInstances(null);

            Assert.IsTrue(result.IsSuccess == false ? result.IsWarning : true);
            Assert.IsTrue(result.Message.Contains("Rejoin requested for 1 managed instance", StringComparison.Ordinal));

            hubClient.Verify(x => x.SendCommandRequest(It.Is<InstanceCommandRequest>(cmd =>
                cmd.CommandType == ManagementHubCommands.RejoinManagementHub
                && HasExpectedRejoinPayload(cmd, "join-client", "join-secret"))), Times.Once);
        }

        private static bool HasExpectedRejoinPayload(InstanceCommandRequest command, string clientId, string secret)
        {
            var payload = JsonSerializer.Deserialize<ManagementHubRejoinRequest>(command.Value ?? "{}", Certify.Shared.JsonOptions.DefaultJsonSerializerOptions);
            return payload?.JoiningCredential.ClientId == clientId
                && payload.JoiningCredential.Secret == secret
                && payload.ReissueRequestAuthSecret;
        }

        private static Mock<ICertifyManager> CreateManagerMock()
        {
            var accessControl = new Mock<IAccessControl>();
            accessControl
                .Setup(x => x.GetAssignedAccessTokens(It.IsAny<string>()))
                .ReturnsAsync([
                    new AssignedAccessToken
                    {
                        Title = "Managed Instance Hub Joining Key",
                        AccessTokens =
                        [
                            new AccessToken
                            {
                                ClientId = "join-client",
                                Secret = "join-secret",
                                DateCreated = DateTimeOffset.UtcNow
                            }
                        ]
                    }
                ]);

            var manager = new Mock<ICertifyManager>();
            manager.Setup(x => x.GetCurrentAccessControl()).ReturnsAsync(accessControl.Object);
            return manager;
        }
    }
}
