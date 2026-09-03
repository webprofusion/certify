using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Hub;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for what a certificate subscription sends to its management hub and what it makes of the reply. These run
    /// the real generated hub client over a stubbed transport, so the request headers, the status handling and the
    /// version header parsing are the production ones rather than a stand in
    /// </summary>
    [TestClass]
    public class SubscriptionFetchTransportTests
    {
        private const string HubApiBase = "https://hub.example.com";

        /// <summary>
        /// What the hub actually received, captured at send time: the request message itself is not safe to read once
        /// the send has completed
        /// </summary>
        private sealed class CapturedRequest
        {
            public Uri RequestUri { get; init; }
            public Dictionary<string, string[]> Headers { get; init; }

            public string SingleHeader(string name) => Headers.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;
            public bool HasHeader(string name) => Headers.ContainsKey(name);
        }

        private sealed class StubHubTransport : HttpMessageHandler
        {
            private readonly Func<HttpResponseMessage> _respond;

            public StubHubTransport(Func<HttpResponseMessage> respond) => _respond = respond;

            public List<CapturedRequest> Requests { get; } = new();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(new CapturedRequest
                {
                    RequestUri = request.RequestUri,
                    Headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase)
                });

                return Task.FromResult(_respond());
            }
        }

        private static HttpResponseMessage CertificateResponse(byte[] payload, string eTag = null, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(statusCode) { Content = new ByteArrayContent(payload) };

            if (eTag != null)
            {
                response.Headers.TryAddWithoutValidation("ETag", eTag);
            }

            return response;
        }

        private static StubHubTransport Responding(Func<HttpResponseMessage> respond) => new(respond);

        private static StubHubTransport Throwing(Exception exception) => new(() => throw exception);

        private static CertifyManager CreateManager(StubHubTransport transport)
        {
            var manager = new CertifyManager(transport);

            // a joining secret satisfies credential resolution without a credentials store, and a cached request auth
            // secret keeps the request context from reaching for one
            SetPrivateField(manager, "_mgmtHubJoiningSecret", new ClientSecret { ClientId = "client-id", Secret = "client-secret" });
            SetPrivateField(manager, "_mgmtHubRequestAuthSecret", "request-auth-secret");

            return manager;
        }

        private static void SetPrivateField(CertifyManager manager, string fieldName, object value)
        {
            var field = typeof(CertifyManager).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{fieldName} should be available for testing");
            field.SetValue(manager, value);
        }

        private static ManagedCertificate CreateSubscription(string lastSourceVersion = null)
        {
            return new ManagedCertificate
            {
                Id = "subscriber-item",
                Name = "Subscriber Item",
                ItemType = ManagedCertificateType.SSL_ExternalSubscription,
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                    ExternalReference = "instance-7/managed-cert-9",
                    SourceConnection = HubApiBase,
                    LastSourceVersion = lastSourceVersion
                }
            };
        }

        private sealed class FetchOutcome
        {
            public bool IsSuccess { get; init; }
            public bool HasUpdate { get; init; }
            public string SourceVersion { get; init; }
            public string Message { get; init; }
            public int PayloadLength { get; init; }
        }

        private static async Task<FetchOutcome> Fetch(CertifyManager manager, ManagedCertificate item, bool ignoreCurrentVersion = false)
        {
            var method = typeof(CertifyManager).GetMethod("FetchExternalCertificateAsset", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "FetchExternalCertificateAsset should be available for testing");

            var task = (Task)method.Invoke(manager, new object[] { item, item.ExternalSource, CancellationToken.None, ignoreCurrentVersion });
            await task;

            var result = task.GetType().GetProperty("Result").GetValue(task);
            var resultType = result.GetType();
            var payload = (byte[])resultType.GetProperty("CertificateData").GetValue(result);

            return new FetchOutcome
            {
                IsSuccess = (bool)resultType.GetProperty("IsSuccess").GetValue(result),
                HasUpdate = (bool)resultType.GetProperty("HasUpdate").GetValue(result),
                SourceVersion = (string)resultType.GetProperty("SourceVersion").GetValue(result),
                Message = (string)resultType.GetProperty("Message").GetValue(result),
                PayloadLength = payload?.Length ?? 0
            };
        }

        [TestMethod, Description("A certificate returned by the hub is fetched with the version the hub declared")]
        public async Task CertificateReturnedByTheHubIsFetched()
        {
            var payload = Encoding.UTF8.GetBytes("pfx-bytes");
            var transport = Responding(() => CertificateResponse(payload, eTag: "\"abc123\""));

            var result = await Fetch(CreateManager(transport), CreateSubscription(lastSourceVersion: "older"));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(result.HasUpdate);
            Assert.AreEqual(payload.Length, result.PayloadLength);

            // the hub quotes its ETags, and the quotes are not part of the version we store and compare against
            Assert.AreEqual("abc123", result.SourceVersion);
        }

        [TestMethod, Description("The certificate is requested from the instance and item named by the subscription reference")]
        public async Task RequestGoesToTheReferencedInstanceAndItem()
        {
            var transport = Responding(() => CertificateResponse(Encoding.UTF8.GetBytes("pfx-bytes"), eTag: "v1"));

            await Fetch(CreateManager(transport), CreateSubscription());

            var request = transport.Requests.Single();

            Assert.Contains("instance-7", request.RequestUri.AbsolutePath, "The instance from the subscription reference selects whose certificate is fetched");
            Assert.Contains("managed-cert-9", request.RequestUri.AbsolutePath);
            Assert.EndsWith("pfx", request.RequestUri.AbsolutePath, "A subscription deploys a PFX");
        }

        [TestMethod, Description("The subscription authenticates itself to the hub")]
        public async Task RequestCarriesTheSourceCredentials()
        {
            var transport = Responding(() => CertificateResponse(Encoding.UTF8.GetBytes("pfx-bytes"), eTag: "v1"));

            await Fetch(CreateManager(transport), CreateSubscription());

            var request = transport.Requests.Single();

            Assert.AreEqual("client-id", request.SingleHeader("X-Client-ID"));
            Assert.AreEqual("client-secret", request.SingleHeader("X-Client-Secret"));
        }

        [TestMethod, Description("The version already held is sent so the hub can answer that nothing changed")]
        public async Task RequestSendsTheVersionAlreadyHeld()
        {
            var transport = Responding(() => CertificateResponse(Encoding.UTF8.GetBytes("pfx-bytes"), eTag: "v2"));

            await Fetch(CreateManager(transport), CreateSubscription(lastSourceVersion: "v1"));

            // this is what lets the hub reply 304 rather than send a certificate we already hold
            Assert.AreEqual("v1", transport.Requests.Single().SingleHeader("If-None-Match"));
        }

        [TestMethod, Description("A request which fetches regardless does not ask the hub to skip the version held")]
        public async Task RequestIgnoringTheCurrentVersionDoesNotSendIt()
        {
            var transport = Responding(() => CertificateResponse(Encoding.UTF8.GetBytes("pfx-bytes"), eTag: "v1"));

            await Fetch(CreateManager(transport), CreateSubscription(lastSourceVersion: "v1"), ignoreCurrentVersion: true);

            // a manual request and the access test both want the certificate sent, so a 304 would defeat them
            Assert.IsFalse(transport.Requests.Single().HasHeader("If-None-Match"));
        }

        [TestMethod, Description("A hub reporting nothing changed is a successful check with no update")]
        public async Task NotModifiedIsACheckWithNoUpdate()
        {
            var transport = Responding(() => new HttpResponseMessage(HttpStatusCode.NotModified));

            var result = await Fetch(CreateManager(transport), CreateSubscription(lastSourceVersion: "v1"));

            Assert.IsTrue(result.IsSuccess, "The hub answered, so attempts against it are not failing");
            Assert.IsFalse(result.HasUpdate);
            Assert.AreEqual("v1", result.SourceVersion, "The item still holds the version it held before the check");
        }

        [TestMethod, Description("A hub refusing the request reports the status it returned")]
        public async Task RejectedRequestReportsTheHubStatus()
        {
            var transport = Responding(() => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("instance is not permitted to pull this certificate")
            });

            var result = await Fetch(CreateManager(transport), CreateSubscription());

            Assert.IsFalse(result.IsSuccess);
            Assert.Contains("403", result.Message, "The operator needs the status to tell a permissions problem from an outage");
            Assert.Contains("not permitted", result.Message, "The hub's own explanation is carried through");
        }

        [TestMethod, Description("A hub error reports the status rather than being taken as no update")]
        public async Task HubErrorIsAFailedFetch()
        {
            var transport = Responding(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("unhandled")
            });

            var result = await Fetch(CreateManager(transport), CreateSubscription(lastSourceVersion: "v1"));

            Assert.IsFalse(result.IsSuccess);
            Assert.IsFalse(result.HasUpdate);
            Assert.Contains("500", result.Message);
        }

        [TestMethod, Description("A hub which cannot be reached reports a connectivity problem")]
        public async Task UnreachableHubReportsConnectivity()
        {
            var transport = Throwing(new HttpRequestException("No such host is known"));

            var result = await Fetch(CreateManager(transport), CreateSubscription());

            Assert.IsFalse(result.IsSuccess);
            Assert.Contains(HubApiBase, result.Message, "The message names the hub which could not be reached");
            Assert.Contains("connectivity", result.Message);
        }

        [TestMethod, Description("A request which times out is a failed fetch rather than an escaping exception")]
        public async Task TimedOutRequestIsAFailedFetch()
        {
            // a request timeout surfaces as TaskCanceledException, which is neither an ApiException nor an
            // HttpRequestException - without the catch all it would escape the fetch entirely
            var transport = Throwing(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

            var result = await Fetch(CreateManager(transport), CreateSubscription());

            Assert.IsFalse(result.IsSuccess);
            Assert.IsFalse(result.HasUpdate);
            Assert.Contains("Unexpected error", result.Message);
        }

        [TestMethod, Description("A hub returning an empty body is a failed fetch, not an empty certificate")]
        public async Task EmptyBodyIsAFailedFetch()
        {
            var transport = Responding(() => CertificateResponse(Array.Empty<byte>(), eTag: "v2"));

            var result = await Fetch(CreateManager(transport), CreateSubscription(lastSourceVersion: "v1"));

            Assert.IsFalse(result.IsSuccess);
            Assert.Contains("empty certificate payload", result.Message);
        }

        [TestMethod, Description("A hub which sends no version identifies the certificate by its content")]
        public async Task ResponseWithoutAnETagIsIdentifiedByItsContent()
        {
            var transport = Responding(() => CertificateResponse(Encoding.UTF8.GetBytes("pfx-bytes")));

            var result = await Fetch(CreateManager(transport), CreateSubscription());

            Assert.IsTrue(result.HasUpdate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.SourceVersion), "A version marker is always recorded, so the next check has something to compare against");
        }

        [TestMethod, Description("A hub returning the version already held reports no update")]
        public async Task ResponseMatchingTheVersionHeldIsNotAnUpdate()
        {
            // a hub which serves the certificate rather than answering 304 must not cause a redeployment
            var transport = Responding(() => CertificateResponse(Encoding.UTF8.GetBytes("pfx-bytes"), eTag: "\"v1\""));

            var result = await Fetch(CreateManager(transport), CreateSubscription(lastSourceVersion: "v1"));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsFalse(result.HasUpdate);
        }
    }
}
