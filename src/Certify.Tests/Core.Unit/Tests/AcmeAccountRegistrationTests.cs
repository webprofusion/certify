using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
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
    /// (RFC 8555 Section 7.3.1), an external account binding key registers one account only, and a directory key
    /// in the url registers accounts for clients without EAB support.
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

        [TestMethod]
        [Description("A directory fetched with a valid key gives a keyed new-account url, and no EAB requirement")]
        public async Task GetDirectory_ValidKey_KeysOnlyNewAccount()
        {
            var fixture = new Fixture();
            var key = fixture.AddAccessToken();

            var result = await fixture.CreateController($"/acme/{key}/directory").GetDirectory(key);

            var directory = (AcmeDirectory)((OkObjectResult)result).Value;
            Assert.AreEqual($"https://{Host}/acme/{key}/new-account", directory.NewAccount);
            Assert.AreEqual($"https://{Host}/acme/new-order", directory.NewOrder);
            Assert.AreEqual($"https://{Host}/acme/new-nonce", directory.NewNonce);
            Assert.IsFalse(directory.Meta.ExternalAccountRequired);
        }

        [TestMethod]
        [Description("A directory fetched with a wrong key is refused")]
        public async Task GetDirectory_WrongKey_IsUnauthorized()
        {
            var fixture = new Fixture();
            fixture.AddAccessToken();

            var wrongKey = ManagedAcmeDirectoryKey.Build(Fixture.TokenId, DirectoryKeyHash("not-the-secret"));
            var result = await fixture.CreateController($"/acme/{wrongKey}/directory").GetDirectory(wrongKey);

            AssertAcmeError(result, 403, AcmeErrorResponseService.AcmeErrorTypes.Unauthorized);
        }

        [TestMethod]
        [Description("A valid directory key registers an account without EAB, bound to the token's principal, and can register again")]
        public async Task NewAccount_ValidKey_WithoutEab_RegistersAndIsReusable()
        {
            var fixture = new Fixture();
            var key = fixture.AddAccessToken();
            var url = $"https://{Host}/acme/{key}/new-account";

            using var jws = new AcmeTestJws();
            var result = await fixture.CreateController($"/acme/{key}/new-account").NewAccount(jws.SignWithJwk(url, fixture.IssueNonce(), new { termsOfServiceAgreed = true }), key);

            Assert.IsInstanceOfType<CreatedResult>(result, result.GetType().Name);
            var (accountKid, account) = (await fixture.Config.GetAccounts()).Single();
            StringAssert.StartsWith(accountKid, $"https://{Host}/acme/account/", "the account url does not carry the key");
            Assert.AreEqual(Fixture.TokenId, account.internalId);
            Assert.AreEqual(Fixture.PrincipalId, account.SecurityPrincipalId);

            // the url is configured once on the client, which may register again with a new account key
            using var otherJws = new AcmeTestJws();
            result = await fixture.CreateController($"/acme/{key}/new-account").NewAccount(otherJws.SignWithJwk(url, fixture.IssueNonce(), new { termsOfServiceAgreed = true }), key);

            Assert.IsInstanceOfType<CreatedResult>(result, result.GetType().Name);
            Assert.AreEqual(2, (await fixture.Config.GetAccounts()).Count);
        }

        [TestMethod]
        [Description("The EAB key of a token is not accepted as its directory key")]
        public async Task NewAccount_EabKeyAsDirectoryKey_IsUnauthorized()
        {
            var fixture = new Fixture();
            fixture.AddAccessToken();

            var eabKey = Certify.Management.Util.ToUrlSafeBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Fixture.Secret)));
            var key = ManagedAcmeDirectoryKey.Build(Fixture.TokenId, eabKey);

            await AssertNewAccountRefused(fixture, key);
        }

        [TestMethod]
        [Description("A directory key is refused once its token is revoked")]
        public async Task NewAccount_RevokedToken_IsUnauthorized()
        {
            var fixture = new Fixture();
            var key = fixture.AddAccessToken(revoked: true);

            await AssertNewAccountRefused(fixture, key);
        }

        [TestMethod]
        [Description("A directory key is refused when its principal may not perform managed ACME orders")]
        public async Task NewAccount_PrincipalWithoutManagedAcmeAccess_IsUnauthorized()
        {
            var fixture = new Fixture();
            var key = fixture.AddAccessToken(hasManagedAcmeAccess: false);

            await AssertNewAccountRefused(fixture, key);
        }

        [TestMethod]
        [Description("A valid directory key is refused while keyed directory urls are disabled in hub settings")]
        public async Task KeyedDirectoryUrlsDisabled_ValidKey_IsUnauthorized()
        {
            var fixture = new Fixture();
            var key = fixture.AddAccessToken(allowKeyedDirectoryUrls: false);

            var result = await fixture.CreateController($"/acme/{key}/directory").GetDirectory(key);
            AssertAcmeError(result, 403, AcmeErrorResponseService.AcmeErrorTypes.Unauthorized);

            await AssertNewAccountRefused(fixture, key);
        }

        [TestMethod]
        [Description("A directory key splits into token id and hash at the last separator, malformed keys do not parse")]
        public void DirectoryKey_TryParse()
        {
            Assert.IsTrue(ManagedAcmeDirectoryKey.TryParse("a.b.hash", out var tokenId, out var hash));
            Assert.AreEqual("a.b", tokenId);
            Assert.AreEqual("hash", hash);

            foreach (var malformed in new[] { null, "", "nohash", ".hash", "token." })
            {
                Assert.IsFalse(ManagedAcmeDirectoryKey.TryParse(malformed, out _, out _), malformed ?? "null");
            }
        }

        private static async Task AssertNewAccountRefused(Fixture fixture, string key)
        {
            using var jws = new AcmeTestJws();
            var url = $"https://{Host}/acme/{key}/new-account";

            var result = await fixture.CreateController($"/acme/{key}/new-account").NewAccount(jws.SignWithJwk(url, fixture.IssueNonce(), new { termsOfServiceAgreed = true }), key);

            AssertAcmeError(result, 403, AcmeErrorResponseService.AcmeErrorTypes.Unauthorized);
            Assert.AreEqual(0, (await fixture.Config.GetAccounts()).Count);
        }

        private static string DirectoryKeyHash(string secret)
            => Certify.Management.Util.ToUrlSafeBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(ManagedAcmeDirectoryKey.GetHashInput(secret))));

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
            public const string TokenId = "token-firewall-01";
            public const string Secret = "token-secret-value";
            public const string PrincipalId = "sp-firewall-01";

            public AcmeServerConfig Config { get; } = new(new FakeConfigurationStore(), "acme-tests");

            public Mock<ICertifyInternalApiClient> Client { get; } = new();

            /// <summary>
            /// Adds an access token for a Managed ACME consumer principal, returning its directory key
            /// </summary>
            public string AddAccessToken(bool revoked = false, bool hasManagedAcmeAccess = true, bool allowKeyedDirectoryUrls = true)
            {
                Client.Setup(c => c.GetHubSettings(It.IsAny<AuthContext>()))
                    .ReturnsAsync(new HubSettings { ManagedAcme = new ManagedAcmeSettings { AllowKeyedDirectoryUrls = allowKeyedDirectoryUrls } });

                var token = new AccessToken { Id = TokenId, Secret = Secret, DateRevoked = revoked ? DateTimeOffset.UtcNow : null };

                Client.Setup(c => c.GetAssignedAccessTokens(It.IsAny<AuthContext>()))
                    .ReturnsAsync([new AssignedAccessToken { SecurityPrincipalId = PrincipalId, AccessTokens = [new AccessToken { Id = TokenId, DateRevoked = token.DateRevoked }] }]);

                Client.Setup(c => c.UnlockAccessToken(TokenId, It.IsAny<AuthContext>()))
                    .ReturnsAsync(new AccessTokenUnlockResult { IsSuccess = true, Result = token });

                Client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.Is<AccessCheck>(a => a.SecurityPrincipalId == PrincipalId), It.IsAny<AuthContext>()))
                    .ReturnsAsync(hasManagedAcmeAccess);

                return ManagedAcmeDirectoryKey.Build(TokenId, DirectoryKeyHash(Secret));
            }

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

            public AcmeController CreateController(string path = "/acme/new-account")
            {
                var stateProvider = new Mock<IInstanceManagementStateProvider>();
                stateProvider.Setup(s => s.GetManagementHubInstanceId()).Returns("hub-instance");

                var client = Client;
                var jwsValidator = new AcmeJwsValidator(NullLogger<AcmeJwsValidator>.Instance, Config);
                var eabValidator = new AcmeExternalAccountBindingValidator(NullLogger<AcmeExternalAccountBindingValidator>.Instance, client.Object, Config);

                var httpContext = new DefaultHttpContext();
                httpContext.Request.Scheme = "https";
                httpContext.Request.Host = new HostString(Host);
                httpContext.Request.Path = path;

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
