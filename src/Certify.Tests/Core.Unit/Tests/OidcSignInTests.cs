using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Controllers;
using Certify.Server.Hub.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// OIDC sign in must validate the ID token issuer, use PKCE, and take its endpoints from discovery, before group claims
    /// in the token can be trusted to grant hub roles.
    /// </summary>
    [TestClass]
    public class OidcSignInTests
    {
        private const string ClientId = "client-1";
        private const string Nonce = "nonce-1";
        private const string TenantId = "40e2f3e1-768f-4006-8891-0c85aecd9fd9";
        private const string OtherTenantId = "9188040d-6c67-4c5b-b112-36a304b66dad";

        private static readonly RsaSecurityKey SigningKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "key-1" };

        private static OidcProviderConfig CreateProvider() => new OidcProviderConfig
        {
            Id = "default",
            Authority = "https://idp.example.com/realms/test",
            ClientId = ClientId,
            ClientSecret = "secret-1",
            RedirectUri = "https://hub.example.com/authentication/login-callback",
            Scope = "openid profile email"
        };

        private static OpenIdConnectConfiguration CreateDiscovery(string issuer)
        {
            var discovery = new OpenIdConnectConfiguration
            {
                Issuer = issuer,
                AuthorizationEndpoint = "https://idp.example.com/discovered/authorize",
                TokenEndpoint = "https://idp.example.com/discovered/token"
            };
            discovery.SigningKeys.Add(SigningKey);
            return discovery;
        }

        private static string CreateIdToken(string issuer, string nonce = Nonce, string? tenantId = null, SecurityKey? key = null)
        {
            var claims = new List<Claim> { new Claim("sub", "user-1"), new Claim("nonce", nonce) };
            if (tenantId != null)
            {
                claims.Add(new Claim("tid", tenantId));
            }

            var token = new JwtSecurityToken(
                issuer,
                ClientId,
                claims,
                DateTime.UtcNow.AddMinutes(-1),
                DateTime.UtcNow.AddMinutes(10),
                new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        [TestMethod]
        [DataRow("https://login.microsoftonline.com/" + TenantId, "https://login.microsoftonline.com/" + TenantId + "/v2.0/.well-known/openid-configuration", DisplayName = "Entra tenant authority uses v2 metadata")]
        [DataRow("https://login.microsoftonline.com/" + TenantId + "/v2.0/", "https://login.microsoftonline.com/" + TenantId + "/v2.0/.well-known/openid-configuration", DisplayName = "Entra v2 authority unchanged")]
        [DataRow("https://idp.example.com/realms/test/", "https://idp.example.com/realms/test/.well-known/openid-configuration", DisplayName = "Other providers use the authority as given")]
        public void DiscoveryEndpoint_IsDerivedFromTheAuthority(string authority, string expected)
        {
            var provider = CreateProvider();
            provider.Authority = authority;

            Assert.AreEqual(expected, OidcSignIn.GetDiscoveryEndpoint(provider));
        }

        [TestMethod]
        public void DiscoveryEndpoint_UsesConfiguredValue()
        {
            var provider = CreateProvider();
            provider.DiscoveryEndpoint = "https://idp.example.com/custom/metadata";

            Assert.AreEqual("https://idp.example.com/custom/metadata", OidcSignIn.GetDiscoveryEndpoint(provider));
        }

        [TestMethod]
        public void Endpoints_ComeFromDiscovery_WhenNotConfigured()
        {
            var provider = CreateProvider();
            provider.AuthorizeEndpoint = "";
            var discovery = CreateDiscovery("https://idp.example.com/realms/test");

            Assert.AreEqual("https://idp.example.com/discovered/authorize", OidcSignIn.GetAuthorizationEndpoint(provider, discovery));
            Assert.AreEqual("https://idp.example.com/discovered/token", OidcSignIn.GetTokenEndpoint(provider, discovery));
        }

        [TestMethod]
        public void Endpoints_UseConfiguredValues()
        {
            var provider = CreateProvider();
            provider.AuthorizeEndpoint = "https://idp.example.com/configured/authorize";
            provider.TokenEndpoint = "https://idp.example.com/configured/token";
            var discovery = CreateDiscovery("https://idp.example.com/realms/test");

            Assert.AreEqual("https://idp.example.com/configured/authorize", OidcSignIn.GetAuthorizationEndpoint(provider, discovery));
            Assert.AreEqual("https://idp.example.com/configured/token", OidcSignIn.GetTokenEndpoint(provider, discovery));
        }

        [TestMethod]
        public void Endpoints_AreRequired()
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => OidcSignIn.GetTokenEndpoint(CreateProvider(), new OpenIdConnectConfiguration()));
        }

        [TestMethod]
        public void CodeChallenge_MatchesRfc7636Example()
        {
            // RFC 7636 appendix B
            Assert.AreEqual("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", OidcSignIn.CreateCodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        }

        [TestMethod]
        public void CodeVerifier_IsAValidLengthAndUnique()
        {
            var verifier = OidcSignIn.CreateCodeVerifier();

            Assert.IsTrue(verifier.Length >= 43 && verifier.Length <= 128, "RFC 7636 requires 43 to 128 characters.");
            Assert.AreNotEqual(verifier, OidcSignIn.CreateCodeVerifier());
        }

        [TestMethod]
        public void AuthorizationUrl_RequestsTheCodeFlowWithPkce()
        {
            var provider = CreateProvider();
            provider.ResponseType = "id_token";
            provider.ResponseMode = "form_post";

            var url = OidcSignIn.BuildAuthorizationUrl(provider, "https://idp.example.com/authorize", "state-1", Nonce, "challenge-1");
            var query = QueryHelpers.ParseQuery(new Uri(url).Query);

            Assert.IsTrue(url.StartsWith("https://idp.example.com/authorize?"));
            Assert.AreEqual("code", query["response_type"].ToString(), "The callback only handles an authorization code.");
            Assert.IsFalse(query.ContainsKey("response_mode"), "The UI reads the response from the query string.");
            Assert.AreEqual("challenge-1", query["code_challenge"].ToString());
            Assert.AreEqual("S256", query["code_challenge_method"].ToString());
            Assert.AreEqual("state-1", query["state"].ToString());
            Assert.AreEqual(Nonce, query["nonce"].ToString());
        }

        [TestMethod]
        public void TokenRequest_SendsTheCodeVerifier()
        {
            var request = OidcSignIn.CreateTokenRequest(CreateProvider(), "code-1", "verifier-1");

            Assert.AreEqual("verifier-1", request["code_verifier"]);
            Assert.AreEqual("secret-1", request["client_secret"]);
        }

        [TestMethod]
        public void TokenRequest_OmitsAnEmptyClientSecret()
        {
            var provider = CreateProvider();
            provider.ClientSecret = "";

            Assert.IsFalse(OidcSignIn.CreateTokenRequest(provider, "code-1", "verifier-1").ContainsKey("client_secret"));
        }

        [TestMethod]
        public void IdToken_IsAccepted_FromTheDiscoveredIssuer()
        {
            var issuer = "https://idp.example.com/realms/test";

            var principal = OidcSignIn.ValidateIdToken(CreateProvider(), CreateDiscovery(issuer), CreateIdToken(issuer), Nonce);

            Assert.AreEqual(Nonce, principal.FindFirst("nonce")?.Value);
        }

        [TestMethod]
        public void IdToken_IsRejected_FromAnotherIssuer()
        {
            var token = CreateIdToken("https://attacker.example.com/");

            Assert.ThrowsExactly<SecurityTokenInvalidIssuerException>(() =>
                OidcSignIn.ValidateIdToken(CreateProvider(), CreateDiscovery("https://idp.example.com/realms/test"), token, Nonce));
        }

        [TestMethod]
        public void IdToken_IsRejected_WithAnotherNonce()
        {
            var issuer = "https://idp.example.com/realms/test";

            Assert.ThrowsExactly<SecurityTokenValidationException>(() =>
                OidcSignIn.ValidateIdToken(CreateProvider(), CreateDiscovery(issuer), CreateIdToken(issuer, nonce: "other"), Nonce));
        }

        [TestMethod]
        public void IdToken_IsRejected_WhenSignedByAnUnknownKey()
        {
            var issuer = "https://idp.example.com/realms/test";
            var otherKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "key-2" };

            Assert.ThrowsExactly<SecurityTokenSignatureKeyNotFoundException>(() =>
                OidcSignIn.ValidateIdToken(CreateProvider(), CreateDiscovery(issuer), CreateIdToken(issuer, key: otherKey), Nonce));
        }

        [TestMethod]
        public void MultiTenantIssuer_AcceptsTheIssuerOfTheTokensTenant()
        {
            var discovery = CreateDiscovery("https://login.microsoftonline.com/{tenantid}/v2.0");
            var token = CreateIdToken($"https://login.microsoftonline.com/{TenantId}/v2.0", tenantId: TenantId);

            var principal = OidcSignIn.ValidateIdToken(CreateProvider(), discovery, token, Nonce);

            Assert.IsTrue(principal.Identity?.IsAuthenticated);
        }

        [TestMethod]
        public void MultiTenantIssuer_RejectsAnIssuerForAnotherTenant()
        {
            var discovery = CreateDiscovery("https://login.microsoftonline.com/{tenantid}/v2.0");
            var token = CreateIdToken($"https://login.microsoftonline.com/{OtherTenantId}/v2.0", tenantId: TenantId);

            Assert.ThrowsExactly<SecurityTokenInvalidIssuerException>(() => OidcSignIn.ValidateIdToken(CreateProvider(), discovery, token, Nonce));
        }

        [TestMethod]
        public void MultiTenantIssuer_RejectsATokenWithoutATenant()
        {
            var discovery = CreateDiscovery("https://login.microsoftonline.com/{tenantid}/v2.0");
            var token = CreateIdToken("https://login.microsoftonline.com/{tenantid}/v2.0");

            Assert.ThrowsExactly<SecurityTokenInvalidIssuerException>(() => OidcSignIn.ValidateIdToken(CreateProvider(), discovery, token, Nonce));
        }

        private static (AuthController Controller, MemoryCache Cache) CreateController(OidcProviderConfig provider)
        {
            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Loose);
            client.Setup(c => c.GetOidcProviderWithSecret(It.IsAny<string>(), It.IsAny<AuthContext>())).ReturnsAsync(provider);

            var cache = new MemoryCache(new MemoryCacheOptions());
            var controller = new AuthController(
                NullLogger<AuthController>.Instance,
                client.Object,
                new ConfigurationBuilder().Build(),
                cache)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };

            return (controller, cache);
        }

        [TestMethod]
        public async Task BeginOidcLogin_StoresTheVerifierForTheChallengeItSends()
        {
            var provider = CreateProvider();
            provider.AuthorizeEndpoint = "https://idp.example.com/configured/authorize";
            var (controller, cache) = CreateController(provider);

            var result = await controller.BeginOidcLogin("default");

            var response = (OidcLoginResponse)((OkObjectResult)result).Value!;
            var query = QueryHelpers.ParseQuery(new Uri(response.AuthUrl).Query);
            var state = JsonSerializer.Deserialize<OidcState>((string)cache.Get($"oidc_state_{response.State}")!)!;

            Assert.IsFalse(string.IsNullOrEmpty(state.CodeVerifier));
            Assert.AreEqual(OidcSignIn.CreateCodeChallenge(state.CodeVerifier), query["code_challenge"].ToString());
            Assert.AreEqual(state.Nonce, query["nonce"].ToString());
        }

        [TestMethod]
        public async Task CompleteOidcLogin_RequiresAnAuthorizationCode()
        {
            var (controller, cache) = CreateController(CreateProvider());
            cache.Set("oidc_state_state-1", JsonSerializer.Serialize(new OidcState
            {
                Provider = "default",
                Nonce = Nonce,
                CodeVerifier = "verifier-1",
                Timestamp = DateTimeOffset.UtcNow
            }));

            var result = await controller.CompleteOidcLogin(new OidcCallbackBody { state = "state-1" });

            var response = (AuthResponse)((BadRequestObjectResult)result).Value!;
            Assert.AreEqual("missing_code", response.Detail);
        }
    }
}
