using System;
using System.Linq;
using System.Threading.Tasks;
using Certify.Server.Hub.Api.Models.Acme;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.Services.Acme;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// The RFC 8555 error types the hub Managed ACME service reports, so a client can act on a failure.
    /// </summary>
    [TestClass]
    public class AcmeErrorMappingTests
    {
        private const string RequestUrl = "https://hub.example.com/acme/new-order";

        [TestMethod]
        [Description("A request signed with a kid which is not a registered account is accountDoesNotExist")]
        public async Task UnknownKid_IsAccountDoesNotExist()
        {
            var (validator, config) = CreateValidator();
            using var jws = new AcmeTestJws();

            var payload = jws.SignWithKid("https://hub.example.com/acme/account/not-registered", RequestUrl, await IssueNonce(config), new { });

            var exception = await Assert.ThrowsExactlyAsync<AcmeRequestException>(() => validator.DecodeJwsPayload<object>(payload, RequestUrl));

            Assert.AreEqual(AcmeErrorResponseService.AcmeErrorTypes.AccountDoesNotExist, exception.ErrorType);
        }

        [TestMethod]
        [Description("A request signed with an unsupported algorithm is badSignatureAlgorithm")]
        public async Task UnsupportedAlgorithm_IsBadSignatureAlgorithm()
        {
            var (validator, config) = CreateValidator();
            using var jws = new AcmeTestJws();

            var payload = jws.SignWithJwk("https://hub.example.com/acme/new-account", await IssueNonce(config), new { }, alg: "HS256");

            var exception = await Assert.ThrowsExactlyAsync<AcmeRequestException>(
                () => validator.DecodeJwsPayload<object>(payload, "https://hub.example.com/acme/new-account", requireAccountKid: false));

            Assert.AreEqual(AcmeErrorResponseService.AcmeErrorTypes.BadSignatureAlgorithm, exception.ErrorType);
        }

        [TestMethod]
        [Description("badSignatureAlgorithm lists the algorithms the server accepts")]
        public void BadSignatureAlgorithm_ListsSupportedAlgorithms()
        {
            var result = (ObjectResult)AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.BadSignatureAlgorithm, "Unsupported JWS algorithm: HS256");

            Assert.AreEqual(400, result.StatusCode);
            CollectionAssert.AreEquivalent(AcmeKeyPolicy.SupportedSignatureAlgorithms, ((AcmeError)result.Value).Algorithms);
        }

        [TestMethod]
        [DataRow(AcmeErrorResponseService.AcmeErrorTypes.Malformed, 400)]
        [DataRow(AcmeErrorResponseService.AcmeErrorTypes.AccountDoesNotExist, 400)]
        [DataRow(AcmeErrorResponseService.AcmeErrorTypes.OrderNotFound, 404)]
        [DataRow(AcmeErrorResponseService.AcmeErrorTypes.Unauthorized, 403)]
        [DataRow(AcmeErrorResponseService.AcmeErrorTypes.ServerInternal, 500)]
        [Description("Error responses are problem documents")]
        public void ErrorResponses_AreProblemJson(string errorType, int statusCode)
        {
            var result = (ObjectResult)AcmeErrorResponseService.CreateAcmeError(errorType, "detail");

            Assert.AreEqual(statusCode, result.StatusCode);
            CollectionAssert.AreEqual(new[] { AcmeErrorResponseService.ProblemContentType }, result.ContentTypes.ToArray());
            Assert.IsNull(((AcmeError)result.Value).Algorithms, "algorithms is only sent with badSignatureAlgorithm");
        }

        private static (AcmeJwsValidator Validator, AcmeServerConfig Config) CreateValidator()
        {
            var config = new AcmeServerConfig(new FakeConfigurationStore(), "acme-tests");
            return (new AcmeJwsValidator(NullLogger<AcmeJwsValidator>.Instance, config), config);
        }

        private static async Task<string> IssueNonce(AcmeServerConfig config)
        {
            var nonce = Guid.NewGuid().ToString("N");
            await config.StoreAcmeNonce(nonce, DateTime.UtcNow);
            return nonce;
        }
    }
}
