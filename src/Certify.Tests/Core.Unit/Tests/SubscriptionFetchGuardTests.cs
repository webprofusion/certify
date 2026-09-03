using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for the checks a certificate subscription makes before it contacts its source. Each one is a
    /// misconfiguration the operator has to be told about: a subscription which cannot say why it is not updating
    /// looks the same as one which has nothing to update
    /// </summary>
    [TestClass]
    public class SubscriptionFetchGuardTests
    {
        private sealed class FetchOutcome
        {
            public bool IsSuccess { get; init; }
            public bool HasUpdate { get; init; }
            public string Message { get; init; }
        }

        private static ManagedCertificate CreateSubscription(string sourceType, string externalReference = "instance-1/managed-cert-1", string sourceConnection = null)
        {
            return new ManagedCertificate
            {
                Id = "subscriber-item",
                Name = "Subscriber Item",
                ItemType = ManagedCertificateType.SSL_ExternalSubscription,
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = sourceType,
                    RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                    ExternalReference = externalReference,
                    SourceConnection = sourceConnection
                }
            };
        }

        private static async Task<FetchOutcome> InvokeFetch(CertifyManager manager, ManagedCertificate item)
        {
            var method = typeof(CertifyManager).GetMethod("FetchExternalCertificateAsset", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "FetchExternalCertificateAsset should be available for testing");

            var task = (Task)method.Invoke(manager, new object[] { item, item.ExternalSource, CancellationToken.None, false });
            await task;

            var result = task.GetType().GetProperty("Result").GetValue(task);
            var resultType = result.GetType();

            return new FetchOutcome
            {
                IsSuccess = (bool)resultType.GetProperty("IsSuccess").GetValue(result),
                HasUpdate = (bool)resultType.GetProperty("HasUpdate").GetValue(result),
                Message = (string)resultType.GetProperty("Message").GetValue(result)
            };
        }

        private static string InvokeGetCredentialValue(Dictionary<string, string> credentials, params string[] keys)
        {
            var method = typeof(CertifyManager).GetMethod("GetCredentialValue", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "GetCredentialValue should be available for testing");

            return (string)method.Invoke(null, new object[] { credentials, keys });
        }

        [TestMethod, Description("A source type this instance cannot fetch from is reported rather than silently skipped")]
        public async Task UnsupportedSourceTypeIsReported()
        {
            var result = await InvokeFetch(new CertifyManager(), CreateSubscription("AzureKeyVault"));

            Assert.IsFalse(result.IsSuccess);
            Assert.Contains("AzureKeyVault", result.Message, "The operator has to be told which source type was configured");
        }

        [TestMethod, Description("A subscription with no source type chosen yet does not attempt a fetch")]
        public async Task MissingSourceTypeIsReported()
        {
            var result = await InvokeFetch(new CertifyManager(), CreateSubscription(sourceType: null));

            Assert.IsFalse(result.IsSuccess);
            Assert.IsFalse(result.HasUpdate, "Nothing was fetched, so nothing may be presented as an update to deploy");
        }

        [TestMethod, Description("A Management Hub reference which is not in the expected format is reported with the format expected")]
        [DataRow("just-an-instance-id")]
        [DataRow("")]
        [DataRow(null)]
        public async Task MalformedHubReferenceIsReported(string externalReference)
        {
            var result = await InvokeFetch(new CertifyManager(), CreateSubscription(ExternalCertificateSourceTypes.ManagementHub, externalReference));

            Assert.IsFalse(result.IsSuccess);
            Assert.Contains("instanceId", result.Message, "The message names the format so the operator can correct the reference");
        }

        [TestMethod, Description("A Management Hub subscription with nowhere to connect to is reported")]
        public async Task MissingHubConnectionIsReported()
        {
            // no per subscription connection, and this instance has no hub configured either
            var result = await InvokeFetch(new CertifyManager(), CreateSubscription(ExternalCertificateSourceTypes.ManagementHub));

            Assert.IsFalse(result.IsSuccess);
            Assert.Contains("not configured", result.Message);
        }

        [TestMethod, Description("A Management Hub subscription with no usable credentials is reported before any request is made")]
        public async Task MissingHubCredentialsAreReported()
        {
            var item = CreateSubscription(ExternalCertificateSourceTypes.ManagementHub, sourceConnection: "https://hub.example.com");

            var result = await InvokeFetch(new CertifyManager(), item);

            Assert.IsFalse(result.IsSuccess);
            Assert.Contains("credentials", result.Message, "A subscription which cannot authenticate has to say so rather than report a transport failure");
        }

        [TestMethod, Description("The source type is matched regardless of case and surrounding whitespace")]
        public async Task SourceTypeMatchingToleratesCaseAndWhitespace()
        {
            var item = CreateSubscription("  managementhub  ", sourceConnection: "https://hub.example.com");

            var result = await InvokeFetch(new CertifyManager(), item);

            // reaching the credential check proves the source type was recognised: an unrecognised one is rejected first
            Assert.Contains("credentials", result.Message, "A source type stored with different casing must still route to the hub source");
        }

        [TestMethod, Description("Source credentials are read under any of the key names a credential may be stored with")]
        public void CredentialValuesAreReadUnderEitherKeyName()
        {
            Assert.AreEqual("id-1", InvokeGetCredentialValue(new Dictionary<string, string> { { "clientid", "id-1" } }, "clientid", "client_id"));
            Assert.AreEqual("id-2", InvokeGetCredentialValue(new Dictionary<string, string> { { "client_id", "id-2" } }, "clientid", "client_id"));
            Assert.AreEqual("secret-1", InvokeGetCredentialValue(new Dictionary<string, string> { { "password", "secret-1" } }, "secret", "client_secret", "password"));
        }

        [TestMethod, Description("The first key name given wins when a credential holds more than one of them")]
        public void CredentialValueUsesTheFirstMatchingKey()
        {
            var credentials = new Dictionary<string, string> { { "client_id", "second" }, { "clientid", "first" } };

            Assert.AreEqual("first", InvokeGetCredentialValue(credentials, "clientid", "client_id"));
        }

        [TestMethod, Description("A blank credential value is skipped rather than used as the credential")]
        public void BlankCredentialValueIsSkipped()
        {
            var credentials = new Dictionary<string, string> { { "clientid", "   " }, { "client_id", "actual-id" } };

            // a blank value would otherwise be sent as the client id and rejected by the hub as bad credentials rather
            // than reported as the missing setting it is
            Assert.AreEqual("actual-id", InvokeGetCredentialValue(credentials, "clientid", "client_id"));
        }

        [TestMethod, Description("A credential with none of the expected keys yields nothing")]
        public void CredentialWithNoMatchingKeyYieldsNothing()
        {
            Assert.IsNull(InvokeGetCredentialValue(new Dictionary<string, string> { { "username", "someone" } }, "clientid", "client_id"));
        }
    }
}
