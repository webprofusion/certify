using System;
using System.Net.Http;
using System.Reflection;
using Certify.Management;
using Certify.Models.Hub;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for whether the management hub's own certificate is validated when this instance calls its API. That
    /// connection carries the instance's client secret and the certificates it pulls, so which way this setting falls
    /// decides whether the connection can be intercepted. The polarity is pinned here because the code and the setting
    /// name have to keep agreeing: setting it requires trust rather than waiving it
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class HubApiCertificateTrustTests
    {
        private string _originalValue;

        [TestInitialize]
        public void Setup() => _originalValue = Environment.GetEnvironmentVariable(HubCertificateTrust.RequireTrustedVariable);

        [TestCleanup]
        public void Cleanup() => Environment.SetEnvironmentVariable(HubCertificateTrust.RequireTrustedVariable, _originalValue);

        private static HttpClientHandler CreateHandler(string requireTrusted)
        {
            Environment.SetEnvironmentVariable(HubCertificateTrust.RequireTrustedVariable, requireTrusted);

            var method = typeof(CertifyManager).GetMethod("CreateHubApiMessageHandler", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "CreateHubApiMessageHandler should be available for testing");

            return (HttpClientHandler)method.Invoke(null, null);
        }

        [TestMethod, Description("Requiring a trusted hub certificate applies the platform's normal validation")]
        public void RequiringTrustValidatesTheHubCertificate()
        {
            using var handler = CreateHandler("true");

            // no custom callback means the platform decides, which is what rejects a hub certificate this machine
            // does not trust
            Assert.IsNull(handler.ServerCertificateCustomValidationCallback,
                $"Setting {HubCertificateTrust.RequireTrustedVariable} must turn hub certificate validation on, not off");
        }

        [TestMethod, Description("By default the hub's certificate is accepted without validation")]
        public void HubCertificateIsAcceptedWithoutValidationByDefault()
        {
            using var handler = CreateHandler(null);

            var callback = handler.ServerCertificateCustomValidationCallback;

            Assert.AreSame(HttpClientHandler.DangerousAcceptAnyServerCertificateValidator, callback,
                "A hub is commonly reached over a private CA or self signed certificate, so validation is off unless asked for");
        }

        [TestMethod, Description("Only the exact opt in value turns hub certificate validation on")]
        [DataRow("")]
        [DataRow("false")]
        [DataRow("1")]
        [DataRow("True")]
        public void OtherValuesLeaveValidationOff(string requireTrusted)
        {
            using var handler = CreateHandler(requireTrusted);

            // a value which does not turn validation on must leave the connection working rather than half configured
            Assert.IsNotNull(handler.ServerCertificateCustomValidationCallback);
        }
    }
}
