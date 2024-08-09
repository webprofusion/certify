using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Certify.ACME.Anvil;
using Certify.ACME.Anvil.Acme;
using Certify.Models;
using Certify.Providers.ACME.Anvil;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class MiscAcmeTests
    {

        [TestMethod, Description("Test Directory Query")]
        public async Task TestAcmeDirectory()
        {

            var directoryJson = """
                 
                {
                  "newNonce": "https://acme.dev.certifytheweb.com/v2/newNonce",
                  "newAccount": "https://acme.dev.certifytheweb.com/v2/newAccount",
                  "newOrder": "https://acme.dev.certifytheweb.com/v2/newOrder",
                  "revokeCert": "https://acme.dev.certifytheweb.com/v2/revokeCert",
                  "keyChange": "https://acme.dev.certifytheweb.com/v2/keyChange",
                  "meta": {
                    "termsOfService": "https://acme.dev.certifytheweb.com/v2/tc.pdf",
                    "website": "https://certifytheweb.com",
                    "caaIdentities": ["certifytheweb.com"],
                    "externalAccountRequired": false
                  }
                }

                """;

            var mockMessageHandler = new Mock<HttpMessageHandler>();

            mockMessageHandler
                .Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(directoryJson.Trim(), Encoding.UTF8, "application/json")
                });

            using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddDebug());
            var logger = factory.CreateLogger(nameof(MiscTests));

            var loggingHandler = new LoggingHandler(mockMessageHandler.Object, new Loggy(logger), maxRequestsPerSecond: 2);
            var customHttpClient = new System.Net.Http.HttpClient(loggingHandler);

            var acmeHttpClient = new AcmeHttpClient(WellKnownServers.LetsEncryptStaging, customHttpClient);

            var acmeContext = new AcmeContext(WellKnownServers.LetsEncryptStagingV2, http: acmeHttpClient);

            var dir = await acmeContext.GetDirectory(throwOnError: true);

            Assert.IsNotNull(dir);
        }

        [TestMethod, Description("Test Directory Query Rate Limit 429")]
        public async Task TestAcmeDirectoryRateLimit()
        {
            // Some CAs have different type of rate limit, occasionally it's at the server or traffic manager level
            // and is not aware of ACME problem responses etc. This example matches ZeroSSLs rate limit behaviour (if it encounters more than 7 requests per second)

            var directoryResponseRateLimited = """
                 
                <html>
                <head><title>429 Too Many Requests</title></head>
                <body>
                <center><h1>429 Too Many Requests</h1></center>
                <hr><center>nginx</center>
                </body>
                </html>

                """;

            // test message gets disposed after being consumed so we generate a new one for every call
            var rateLimitedResponseMessageFactory = () =>
            {
                var msg = new HttpResponseMessage
                {
                    Content = new StringContent(directoryResponseRateLimited.Trim(), Encoding.UTF8, "text/html"),
                    StatusCode = (HttpStatusCode)429
                };
                msg.Headers.Add("Retry-After", "5");
                return msg;
            };

            var mockMessageHandler = new Mock<HttpMessageHandler>();

            mockMessageHandler
                .Protected()
                .SetupSequence<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(rateLimitedResponseMessageFactory())
                .ReturnsAsync(rateLimitedResponseMessageFactory())
                .ReturnsAsync(rateLimitedResponseMessageFactory());

            using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddDebug());
            var logger = factory.CreateLogger(nameof(MiscTests));

            var loggingHandler = new LoggingHandler(mockMessageHandler.Object, new Loggy(logger), maxRequestsPerSecond: 2);
            var customHttpClient = new System.Net.Http.HttpClient(loggingHandler);

            var acmeHttpClient = new AcmeHttpClient(WellKnownServers.LetsEncryptStaging, customHttpClient);

            var acmeContext = new AcmeContext(WellKnownServers.LetsEncryptStagingV2, http: acmeHttpClient);
            acmeContext.AutoRetryAttempts = 2;

            try
            {
                await acmeContext.GetDirectory(throwOnError: true);
            }
            catch (AcmeRequestException ex)
            {
                Assert.AreEqual("urn:ietf:params:acme:error:rateLimited", ex.Error.Type);
            }
        }

        [TestMethod, Description("Test Directory Query Rate Limit With Auto Retry")]
        public async Task TestAcmeDirectoryRateLimitWithRetry()
        {
            // Some CAs have different type of rate limit, occasionally it's at the server or traffic manager level
            // and is not aware of ACME problem responses etc. This example matches ZeroSSLs rate limit behaviour (if it encounters more than 7 requests per second)

            var directoryResponseRateLimited = """
                 
                <html>
                <head><title>429 Too Many Requests</title></head>
                <body>
                <center><h1>429 Too Many Requests</h1></center>
                <hr><center>nginx</center>
                </body>
                </html>

                """;

            var directoryJson = """
                 
                {
                  "newNonce": "https://acme.dev.certifytheweb.com/v2/newNonce",
                  "newAccount": "https://acme.dev.certifytheweb.com/v2/newAccount",
                  "newOrder": "https://acme.dev.certifytheweb.com/v2/newOrder",
                  "revokeCert": "https://acme.dev.certifytheweb.com/v2/revokeCert",
                  "keyChange": "https://acme.dev.certifytheweb.com/v2/keyChange",
                  "meta": {
                    "termsOfService": "https://acme.dev.certifytheweb.com/v2/tc.pdf",
                    "website": "https://certifytheweb.com",
                    "caaIdentities": ["certifytheweb.com"],
                    "externalAccountRequired": false
                  }
                }

                """;

            // test message gets disposed after being consumed so we generate a new one for every call
            var rateLimitedResponseMessageFactory = (int retryAfter) =>
            {
                var msg = new HttpResponseMessage
                {
                    Content = new StringContent(directoryResponseRateLimited.Trim(), Encoding.UTF8, "text/html"),
                    StatusCode = (HttpStatusCode)429
                };

                // optionally include retry-after header
                if (retryAfter > 0)
                {
                    msg.Headers.Add("Retry-After", "5");
                }

                return msg;
            };

            var directoryResponseMessage = new HttpResponseMessage
            {
                Content = new StringContent(directoryJson.Trim(), Encoding.UTF8, "application/json"),
                StatusCode = (HttpStatusCode)200
            };

            var mockMessageHandler = new Mock<HttpMessageHandler>();

            mockMessageHandler.Protected()
                .SetupSequence<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(rateLimitedResponseMessageFactory(5))
                .ReturnsAsync(rateLimitedResponseMessageFactory(0))
                .ReturnsAsync(directoryResponseMessage);

            using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddDebug());

            var logger = factory.CreateLogger(nameof(MiscTests));

            var loggingHandler = new LoggingHandler(mockMessageHandler.Object, new Loggy(logger), maxRequestsPerSecond: 2);
            var customHttpClient = new System.Net.Http.HttpClient(loggingHandler);

            var acmeHttpClient = new AcmeHttpClient(WellKnownServers.LetsEncryptStaging, customHttpClient);

            var acmeContext = new AcmeContext(WellKnownServers.LetsEncryptStagingV2, http: acmeHttpClient);

            ACME.Anvil.Acme.Resource.Directory dir = default;
            try
            {
                dir = await acmeContext.GetDirectory(throwOnError: false);
            }
            catch (AcmeRequestException ex)
            {
                Assert.AreEqual("urn:ietf:params:acme:error:rateLimited", ex.Error.Type);
            }

            Assert.IsNotNull(dir);
            Assert.IsNotNull(dir.NewOrder);
        }
    }
}
