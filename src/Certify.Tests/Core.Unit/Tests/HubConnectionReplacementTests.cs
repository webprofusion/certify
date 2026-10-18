using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Models.Providers;
using Certify.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// A management hub client stays with the url it was created for, so an instance which joins (or is asked to
    /// rejoin) a hub at another url must replace its client rather than keep reconnecting to the old hub
    /// </summary>
    [TestClass]
    public class HubConnectionReplacementTests
    {
        private const string HubAUri = "https://hub-a.example.com/api/internal/managementhub";

        // nothing listens here, so a connection attempt by a real client fails straight away
        private const string HubBApi = "https://127.0.0.1:1";
        private const string HubBUri = "https://127.0.0.1:1/api/internal/managementhub";

        private static void SetPrivateField(CertifyManager manager, string fieldName, object value)
        {
            var field = typeof(CertifyManager).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{fieldName} should be available for testing");
            field.SetValue(manager, value);
        }

        private static object GetPrivateField(CertifyManager manager, string fieldName)
        {
            var field = typeof(CertifyManager).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{fieldName} should be available for testing");
            return field.GetValue(manager);
        }

        private static Task InvokePrivate(CertifyManager manager, string methodName, params object[] args)
        {
            var method = typeof(CertifyManager).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{methodName} should be available for testing");
            return (Task)method.Invoke(manager, args);
        }

        private static CertifyManager CreateManager(string hubApi)
        {
            var manager = new CertifyManager();

            typeof(CertifyManager)
                .GetProperty("_serviceLog", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(manager, new Mock<ILog>().Object);

            SetPrivateField(manager, "_serverConfig", new Certify.Shared.ServiceConfig
            {
                ManagementServerHubAPI = hubApi,
                ManagementServerHubEndpoint = CertifyManager.DefaultMgmtHubEndpoint
            });

            // a current token lets a connection be started without asking the hub for one
            SetPrivateField(manager, "_cachedHubConnectionToken", "hub-connection-token");
            SetPrivateField(manager, "_cachedHubConnectionTokenExpiry", DateTimeOffset.UtcNow.AddHours(1));

            return manager;
        }

        private static Mock<IManagementServerClient> AttachClient(CertifyManager manager, string hubUri, bool isConnected)
        {
            var client = new Mock<IManagementServerClient>();
            client.Setup(c => c.IsConnected()).Returns(isConnected);
            client.Setup(c => c.Disconnect()).Returns(Task.CompletedTask);
            client.Setup(c => c.ConnectAsync(It.IsAny<Func<CancellationToken, Task<string>>>())).Returns(Task.CompletedTask);

            SetPrivateField(manager, "_managementServerClient", client.Object);
            SetPrivateField(manager, "_managementServerClientHubUri", hubUri);

            return client;
        }

        [TestMethod, Description("A connected client for another hub is closed and dropped, with its handlers detached")]
        public async Task ConnectedClientForPreviousHubIsReplaced()
        {
            var manager = CreateManager(HubBApi);
            var client = AttachClient(manager, HubAUri, isConnected: true);

            await InvokePrivate(manager, "EnsureMgmtHubConnection", null, false, false);

            client.Verify(c => c.Disconnect(), Times.Once);
            client.VerifyRemove(c => c.OnGetCommandResult -= It.IsAny<Func<InstanceCommandRequest, Task<InstanceCommandResult>>>(), Times.Once);
            client.VerifyRemove(c => c.OnConnectionClosed -= It.IsAny<Action>(), Times.Once);
            client.Verify(c => c.SendInstanceInfo(It.IsAny<Guid>(), It.IsAny<bool>()), Times.Never, "No heartbeat goes to the previous hub");
            Assert.AreNotSame(client.Object, GetPrivateField(manager, "_managementServerClient"));
        }

        [TestMethod, Description("A connected client for the configured hub is kept and sent a heartbeat")]
        public async Task ConnectedClientForCurrentHubIsKept()
        {
            var manager = CreateManager(HubBApi);
            var client = AttachClient(manager, HubBUri, isConnected: true);

            await InvokePrivate(manager, "EnsureMgmtHubConnection", null, false, false);

            client.Verify(c => c.Disconnect(), Times.Never);
            client.Verify(c => c.SendInstanceInfo(It.IsAny<Guid>(), false), Times.Once);
            Assert.AreSame(client.Object, GetPrivateField(manager, "_managementServerClient"));
        }

        [TestMethod, Description("An explicit join replaces a connected client even for the same hub")]
        public async Task JoinReplacesConnectedClientForSameHub()
        {
            var manager = CreateManager(HubBApi);
            var client = AttachClient(manager, HubBUri, isConnected: true);

            await InvokePrivate(manager, "EnsureMgmtHubConnection", null, false, true);

            client.Verify(c => c.Disconnect(), Times.Once);
            Assert.AreNotSame(client.Object, GetPrivateField(manager, "_managementServerClient"));
        }

        [TestMethod, Description("Starting a connection to another hub creates a new client for that hub")]
        public async Task StartingConnectionToAnotherHubCreatesNewClient()
        {
            var manager = CreateManager(HubBApi);
            var client = AttachClient(manager, HubAUri, isConnected: false);

            await InvokePrivate(manager, "StartManagementHubConnection", HubBUri);

            client.Verify(c => c.ConnectAsync(It.IsAny<Func<CancellationToken, Task<string>>>()), Times.Never, "The old client must not reconnect to the previous hub");
            client.Verify(c => c.Disconnect(), Times.Once);
            Assert.IsInstanceOfType<ManagementServerClient>(GetPrivateField(manager, "_managementServerClient"));
            Assert.AreEqual(HubBUri, GetPrivateField(manager, "_managementServerClientHubUri"));
        }

        [TestMethod, Description("Starting a connection to the same hub reconnects the existing client")]
        public async Task StartingConnectionToSameHubReusesClient()
        {
            var manager = CreateManager(HubBApi);
            var client = AttachClient(manager, HubBUri, isConnected: false);

            await InvokePrivate(manager, "StartManagementHubConnection", HubBUri);

            client.Verify(c => c.ConnectAsync(It.IsAny<Func<CancellationToken, Task<string>>>()), Times.Once);
            client.Verify(c => c.Disconnect(), Times.Never);
            Assert.AreSame(client.Object, GetPrivateField(manager, "_managementServerClient"));
        }

        [TestMethod, Description("The hub connection url is resolved from the environment when set, otherwise from service config")]
        public void HubUriIsResolvedFromEnvironmentOrConfig()
        {
            Assert.AreEqual(("https://hub.example.com", "https://hub.example.com/api/internal/managementhub"),
                CertifyManager.ResolveManagementHubUri(null, null, "https://hub.example.com/", "/api/internal/managementhub/"));

            Assert.AreEqual(("", ""), CertifyManager.ResolveManagementHubUri(null, null, null, null));

            Assert.AreEqual(("https://env.example.com/", "https://env.example.com/api/internal/managementhub"),
                CertifyManager.ResolveManagementHubUri("https://env.example.com/api/internal/managementhub", null, "https://hub.example.com", "x"));

            Assert.AreEqual(("https://env.example.com", "https://env.example.com/custom"),
                CertifyManager.ResolveManagementHubUri("https://env.example.com", "/custom", "https://hub.example.com", "x"));
        }

        [TestMethod, Description("A rejoin command without a joining credential is refused rather than failing")]
        public async Task RejoinWithoutJoiningCredentialIsRefused()
        {
            var manager = CreateManager(HubBApi);

            var result = await manager.PerformHubCommandWithResult(
                new InstanceCommandRequest(ManagementHubCommands.RejoinManagementHub) { Value = "{}" });

            var actionResult = result.ObjectValue as ActionResult;
            Assert.IsNotNull(actionResult);
            Assert.IsFalse(actionResult.IsSuccess);
        }

        [TestMethod, Description("A certificate request for an unknown managed certificate is not started")]
        public async Task RequestForUnknownItemIsNotStarted()
        {
            var manager = CreateManager(HubBApi);

            var itemStore = new Mock<IManagedItemStore>();
            itemStore.Setup(s => s.GetById(It.IsAny<string>())).ReturnsAsync((ManagedCertificate)null);
            SetPrivateField(manager, "_itemManager", itemStore.Object);

            var args = new KeyValuePair<string, string>[] { new("managedCertId", "unknown-item") };

            var result = await manager.PerformHubCommandWithResult(
                new InstanceCommandRequest(ManagementHubCommands.PerformManagedItemRequest, args));

            Assert.IsFalse((bool)result.ObjectValue);
        }
    }
}
