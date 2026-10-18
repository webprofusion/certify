using System;
using System.Linq;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Server.Hub.Api.Controllers.acme;
using Certify.Server.Hub.Api.Models.Acme;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.Services.Acme;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// Registering with the hub Managed ACME service: a key which already has an account gets that account back
    /// (RFC 8555 Section 7.3.1), and an external account binding key registers one account only.
    /// </summary>
    [TestClass]
    public class AcmeAccountRegistrationTests
    {
        private const string Host = "hub.example.com";
        private const string NewAccountUrl = $"https://{Host}/acme/new-account";

        [TestMethod]
        [Description("A key which already has an account gets that account back, without an external account binding")]
        public async Task NewAccount_ExistingKey_ReturnsTheAccount()
        {
            var fixture = new Fixture();
            using var jws = new AcmeTestJws();
            var accountKid = await fixture.StoreAccount(jws.PublicKey);

            var controller = fixture.CreateController();
            var result = await controller.NewAccount(jws.SignWithJwk(NewAccountUrl, fixture.IssueNonce(), new { termsOfServiceAgreed = true }));

            Assert.IsInstanceOfType<OkObjectResult>(result, "an existing account is 200, not 201");
            Assert.AreEqual(accountKid, controller.Response.Headers.Location.ToString());
            Assert.AreEqual(1, (await fixture.Config.GetAccounts()).Count, "no second account is created");
        }

        [TestMethod]
        [Description("onlyReturnExisting returns the account for a registered key")]
        public async Task NewAccount_OnlyReturnExisting_ReturnsTheAccount()
        {
            var fixture = new Fixture();
            using var jws = new AcmeTestJws();
            var accountKid = await fixture.StoreAccount(jws.PublicKey);

            var controller = fixture.CreateController();
            var result = await controller.NewAccount(jws.SignWithJwk(NewAccountUrl, fixture.IssueNonce(), new { onlyReturnExisting = true }));

            Assert.IsInstanceOfType<OkObjectResult>(result);
            Assert.AreEqual(accountKid, controller.Response.Headers.Location.ToString());
        }

        [TestMethod]
        [Description("onlyReturnExisting for an unregistered key is accountDoesNotExist")]
        public async Task NewAccount_OnlyReturnExisting_UnknownKey_IsAccountDoesNotExist()
        {
            var fixture = new Fixture();
            using var jws = new AcmeTestJws();
            using var otherJws = new AcmeTestJws();
            await fixture.StoreAccount(otherJws.PublicKey);

            var result = await fixture.CreateController().NewAccount(jws.SignWithJwk(NewAccountUrl, fixture.IssueNonce(), new { onlyReturnExisting = true }));

            AssertAcmeError(result, 400, AcmeErrorResponseService.AcmeErrorTypes.AccountDoesNotExist);
        }

        [TestMethod]
        [Description("A new key without an external account binding is still refused")]
        public async Task NewAccount_UnknownKey_WithoutEab_IsExternalAccountRequired()
        {
            var fixture = new Fixture();
            using var jws = new AcmeTestJws();

            var result = await fixture.CreateController().NewAccount(jws.SignWithJwk(NewAccountUrl, fixture.IssueNonce(), new { termsOfServiceAgreed = true }));

            AssertAcmeError(result, 400, AcmeErrorResponseService.AcmeErrorTypes.ExternalAccountRequired);
        }

        [TestMethod]
        [Description("An account is found by its key whatever the member encoding of the key")]
        public async Task FindAccountByKey_MatchesByThumbprint()
        {
            var fixture = new Fixture();
            using var jws = new AcmeTestJws();
            var accountKid = await fixture.StoreAccount(jws.PublicKey);

            var paddedKey = new JsonWebKey { Kty = "EC", Crv = "P-256", X = jws.PublicKey.X + "=", Y = jws.PublicKey.Y };

            Assert.AreEqual(accountKid, (await fixture.Config.FindAccountByKey(paddedKey))?.AccountKid);

            using var otherJws = new AcmeTestJws();
            Assert.IsNull(await fixture.Config.FindAccountByKey(otherJws.PublicKey));
        }

        [TestMethod]
        [Description("Of concurrent registrations with one EAB key, only one claims it")]
        public async Task TryClaimEabKey_Concurrent_OnlyOneSucceeds()
        {
            var fixture = new Fixture();
            var keyId = Guid.NewGuid().ToString("N");

            var claims = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => fixture.Config.TryClaimEabKey(keyId))));

            Assert.AreEqual(1, claims.Count(c => c));
            Assert.IsTrue(await fixture.Config.IsEabKeyConsumed(keyId));
            Assert.IsFalse(await fixture.Config.TryClaimEabKey(keyId), "a consumed key cannot be claimed again");
        }

        private static void AssertAcmeError(IActionResult result, int statusCode, string errorType)
        {
            Assert.IsInstanceOfType<ObjectResult>(result, result.GetType().Name);

            var objectResult = (ObjectResult)result;

            Assert.AreEqual(statusCode, objectResult.StatusCode);
            Assert.AreEqual(errorType, ((AcmeError)objectResult.Value).Type, JsonConvert.SerializeObject(objectResult.Value));
            CollectionAssert.Contains(objectResult.ContentTypes.ToList(), AcmeErrorResponseService.ProblemContentType);
        }

        private sealed class Fixture
        {
            public AcmeServerConfig Config { get; } = new(new FakeConfigurationStore(), "acme-tests");

            public string IssueNonce()
            {
                var nonce = Guid.NewGuid().ToString("N");
                Config.StoreAcmeNonce(nonce, DateTime.UtcNow).GetAwaiter().GetResult();
                return nonce;
            }

            public async Task<string> StoreAccount(JsonWebKey key)
            {
                var accountKid = $"https://{Host}/acme/account/{Guid.NewGuid():N}";

                await Config.StoreAcmeAccount(accountKid, new AcmeAccount
                {
                    Status = AccountStatus.Valid,
                    SecurityPrincipalId = "sp-build-agent",
                    TermsOfServiceAgreed = true,
                    Orders = $"{accountKid}/orders",
                    DateCreated = DateTimeOffset.UtcNow
                });

                await Config.StoreAcmeAccountKey(accountKid, key);

                return accountKid;
            }

            public AcmeController CreateController()
            {
                var stateProvider = new Mock<IInstanceManagementStateProvider>();
                stateProvider.Setup(s => s.GetManagementHubInstanceId()).Returns("hub-instance");

                var client = new Mock<ICertifyInternalApiClient>();
                var jwsValidator = new AcmeJwsValidator(NullLogger<AcmeJwsValidator>.Instance, Config);
                var eabValidator = new AcmeExternalAccountBindingValidator(NullLogger<AcmeExternalAccountBindingValidator>.Instance, client.Object, Config);

                var httpContext = new DefaultHttpContext();
                httpContext.Request.Scheme = "https";
                httpContext.Request.Host = new HostString(Host);
                httpContext.Request.Path = "/acme/new-account";

                return new AcmeController(
                    NullLogger<AcmeController>.Instance,
                    new ManagementAPI(stateProvider.Object, null!, NullLogger<ManagementAPI>.Instance),
                    stateProvider.Object,
                    client.Object,
                    Config,
                    new AcmeBackgroundTaskService(NullLogger<AcmeBackgroundTaskService>.Instance, new Mock<IServiceScopeFactory>().Object),
                    jwsValidator,
                    eabValidator,
                    new AcmeHelper(Config))
                {
                    ControllerContext = new ControllerContext { HttpContext = httpContext }
                };
            }
        }
    }
}
