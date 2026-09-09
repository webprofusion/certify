using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Controllers;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using ActionResultConfig = Certify.Models.Config.ActionResult;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// Authentication and authorization scenarios for the managed challenge endpoints, driven through the controller.
    ///
    /// The same API access token can arrive as request headers or as AuthKey/AuthSecret fields in the request body,
    /// and the identity fulfillment runs as has to be the one that was authorized either way. Which resource action
    /// is checked also depends on the endpoint rather than on how the credentials were presented - getting that
    /// wrong denies a managed challenge consumer against a role it was never meant to hold.
    ///
    /// The managed instance route is covered by ManagedInstanceRequestAuthTests.
    /// </summary>
    [TestClass]
    public class ManagedChallengeEndpointAuthTests
    {
        private const string ClientId = "client-id";
        private const string ClientSecret = "client-secret";
        private const string PrincipalId = "sp-consumer";
        private const string Identifier = "www.example.com";

        #region Credentials presented as headers

        [TestMethod]
        [Description("An API token presented as headers performs the challenge as its own principal")]
        public async Task Perform_ApiTokenInHeaders_FulfillsAsThatPrincipal()
        {
            var fixture = new Fixture();
            var controller = fixture.CreateController(CredentialTransport.Headers);

            var result = await controller.PerformManagedChallenge(fixture.Request(CredentialTransport.Headers));

            Assert.IsInstanceOfType<OkObjectResult>(result, fixture.Describe(result));
            Assert.AreEqual(PrincipalId, fixture.Forwarded!.Caller.SecurityPrincipalId);
            Assert.AreEqual(ManagedChallengeRequestOrigins.ManagedChallengeApi, fixture.Forwarded.Caller.Origin);
        }

        #endregion

        #region Credentials presented in the request body

        /// <summary>
        /// The transport the Certify managed DNS provider in released agents uses.
        /// </summary>
        [TestMethod]
        [Description("An API token presented in the request body performs the challenge as its own principal")]
        public async Task Perform_ApiTokenInBody_FulfillsAsThatPrincipal()
        {
            var fixture = new Fixture();
            var controller = fixture.CreateController(CredentialTransport.Body);

            var result = await controller.PerformManagedChallenge(fixture.Request(CredentialTransport.Body));

            Assert.IsInstanceOfType<OkObjectResult>(result, fixture.Describe(result));
            Assert.AreEqual(PrincipalId, fixture.Forwarded!.Caller.SecurityPrincipalId);
        }

        [TestMethod]
        [Description("Credentials which authenticated the request are not forwarded for fulfillment")]
        public async Task Perform_ApiTokenInBody_DoesNotForwardTheCredentials()
        {
            var fixture = new Fixture();
            var controller = fixture.CreateController(CredentialTransport.Body);

            await controller.PerformManagedChallenge(fixture.Request(CredentialTransport.Body));

            Assert.IsEmpty(fixture.Forwarded!.Request.AuthKey);
            Assert.IsEmpty(fixture.Forwarded.Request.AuthSecret);
        }

        /// <summary>
        /// Credentials in the body cannot be used to act as someone else. The identity comes from what the request
        /// authenticated as, so a body carrying another client's credentials is ignored rather than honoured - the
        /// caller gets their own role scope, never the other principal's.
        ///
        /// The middleware authenticates from headers first and the body only as a fallback, so a mismatch means the
        /// caller already authenticated as themselves by some other means.
        /// </summary>
        [TestMethod]
        [Description("Body credentials for another client do not change who the request runs as")]
        public async Task Perform_BodyCredentialsForAnotherClient_AreIgnored()
        {
            var fixture = new Fixture();
            var controller = fixture.CreateController(CredentialTransport.Headers);

            var request = fixture.Request(CredentialTransport.Body);
            request.AuthKey = "someone-elses-client-id";
            request.AuthSecret = "someone-elses-secret";

            var result = await controller.PerformManagedChallenge(request);

            Assert.IsInstanceOfType<OkObjectResult>(result, fixture.Describe(result));
            Assert.AreEqual(
                PrincipalId,
                fixture.Forwarded!.Caller.SecurityPrincipalId,
                "the request runs as the principal it authenticated as, not as whoever the body names");

            Assert.IsEmpty(fixture.Forwarded.Request.AuthKey, "the other client's credentials must not travel on either");
        }

        #endregion

        #region Role scope

        [TestMethod]
        [Description("A tag scoped token whose scope covers the identifier performs the challenge, carrying that scope")]
        public async Task Perform_TagScopedToken_WithinScope_FulfillsWithTheScopeApplied()
        {
            var fixture = new Fixture { ScopedAssignedRoles = ["ar-scoped"] };
            var controller = fixture.CreateController(CredentialTransport.Headers);

            var result = await controller.PerformManagedChallenge(fixture.Request(CredentialTransport.Headers));

            Assert.IsInstanceOfType<OkObjectResult>(result, fixture.Describe(result));
            CollectionAssert.AreEquivalent(
                new[] { "ar-scoped" },
                fixture.Forwarded!.Caller.ScopedAssignedRoles,
                "fulfillment selects challenges within the token's role scope, so that scope has to travel with it");
        }

        [TestMethod]
        [Description("A token whose scope does not cover the identifier is refused, and nothing is fulfilled")]
        public async Task Perform_TagScopedToken_OutsideScope_IsRefused()
        {
            var fixture = new Fixture { IdentifierAuthorized = false };
            var controller = fixture.CreateController(CredentialTransport.Headers);

            var result = await controller.PerformManagedChallenge(fixture.Request(CredentialTransport.Headers));

            AssertForbidden(result, fixture);
            Assert.IsNull(fixture.Forwarded, "a refused request must not reach fulfillment");
        }

        /// <summary>
        /// A signed in operator used to reach fulfillment with no principal at all, which reads as the unscoped
        /// system path - so a tag scoped operator got every managed challenge rather than the ones their roles select.
        /// </summary>
        [TestMethod]
        [Description("An interactive caller with no API token is still scoped by their own principal")]
        public async Task Perform_BearerAuthenticatedCaller_IsScopedByTheirOwnPrincipal()
        {
            var fixture = new Fixture();
            var controller = fixture.CreateController(CredentialTransport.None);

            var result = await controller.PerformManagedChallenge(fixture.Request(CredentialTransport.None));

            Assert.IsInstanceOfType<OkObjectResult>(result, fixture.Describe(result));
            Assert.AreEqual(PrincipalId, fixture.Forwarded!.Caller.SecurityPrincipalId);
        }

        #endregion

        #region Action selected per endpoint

        /// <summary>
        /// Which action is checked used to be inferred from whether the request carried AuthKey/AuthSecret, so a
        /// consumer authenticating by header was checked against the managed ACME order action instead.
        /// </summary>
        [TestMethod]
        [Description("Cleanup checks the cleanup action, whichever transport the credentials arrived on")]
        public async Task Cleanup_ChecksTheCleanupAction_ForEitherTransport()
        {
            foreach (var transport in new[] { CredentialTransport.Headers, CredentialTransport.Body })
            {
                var fixture = new Fixture();
                var controller = fixture.CreateController(transport);

                var result = await controller.CleanupManagedChallenge(fixture.Request(transport));

                Assert.IsInstanceOfType<OkObjectResult>(result, fixture.Describe(result));
                Assert.AreEqual(
                    StandardResourceActions.ManagedChallengeCleanup,
                    fixture.CheckedActions.LastOrDefault(),
                    $"cleanup presented as {transport} must be checked against the cleanup action");
            }
        }

        [TestMethod]
        [Description("Requesting a challenge checks the request action, whichever transport the credentials arrived on")]
        public async Task Perform_ChecksTheRequestAction_ForEitherTransport()
        {
            foreach (var transport in new[] { CredentialTransport.Headers, CredentialTransport.Body })
            {
                var fixture = new Fixture();
                var controller = fixture.CreateController(transport);

                await controller.PerformManagedChallenge(fixture.Request(transport));

                Assert.AreEqual(
                    StandardResourceActions.ManagedChallengeRequest,
                    fixture.CheckedActions.LastOrDefault(),
                    $"a request presented as {transport} must be checked against the request action");
            }
        }

        #endregion

        #region Fixture

        private enum CredentialTransport
        {
            /// <summary>Authenticated by bearer token, with no API access token presented.</summary>
            None,
            Headers,
            Body
        }

        private static void AssertForbidden(IActionResult result, Fixture fixture)
        {
            Assert.IsInstanceOfType<ObjectResult>(result, fixture.Describe(result));
            Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
        }

        private sealed class Fixture
        {
            public List<string> ScopedAssignedRoles { get; init; }

            /// <summary>Whether the identifier is within the caller's role scope.</summary>
            public bool IdentifierAuthorized { get; init; } = true;

            /// <summary>The resource actions the endpoint asked about, in order.</summary>
            public List<string> CheckedActions { get; } = [];

            /// <summary>What reached fulfillment, or null if the request was refused.</summary>
            public AuthorizedManagedChallengeRequest Forwarded { get; private set; }

            public ManagedChallengeRequest Request(CredentialTransport transport)
            {
                var request = new ManagedChallengeRequest
                {
                    ChallengeType = "dns-01",
                    Identifier = Identifier,
                    ResponseKey = "_acme-challenge." + Identifier,
                    ResponseValue = "response-value"
                };

                if (transport == CredentialTransport.Body)
                {
                    request.AuthKey = ClientId;
                    request.AuthSecret = ClientSecret;
                }

                return request;
            }

            public string Describe(IActionResult result)
            {
                return result is ObjectResult o && o.Value is ProblemDetails p
                    ? $"{result.GetType().Name} ({o.StatusCode}): {p.Detail}"
                    : result.GetType().Name;
            }

            public ManagedChallengeController CreateController(CredentialTransport transport)
            {
                var client = new Mock<ICertifyInternalApiClient>();

                // the caller's roles grant the action the endpoint asks about
                client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((AccessCheck check, AuthContext _) =>
                    {
                        CheckedActions.Add(check.ResourceActionId);
                        return true;
                    });

                client.Setup(c => c.AuthorizeManagedChallengeIdentifiers(It.IsAny<ManagedChallengeAuthorizationCheck>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((ManagedChallengeAuthorizationCheck check, AuthContext _) =>
                    {
                        CheckedActions.Add(check.RequiredActionId);

                        return IdentifierAuthorized
                            ? new ActionResultConfig("Authorized", true)
                            : new ActionResultConfig($"No accessible managed challenge matches identifier '{check.Identifiers.FirstOrDefault()}'", false);
                    });

                client.Setup(c => c.PerformManagedChallenge(It.IsAny<AuthorizedManagedChallengeRequest>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((AuthorizedManagedChallengeRequest authorized, AuthContext _) =>
                    {
                        Forwarded = authorized;
                        return new ActionResultConfig("Challenge completed", true);
                    });

                client.Setup(c => c.CleanupManagedChallenge(It.IsAny<AuthorizedManagedChallengeRequest>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((AuthorizedManagedChallengeRequest authorized, AuthContext _) =>
                    {
                        Forwarded = authorized;
                        return new ActionResultConfig("Cleanup completed", true);
                    });

                return new ManagedChallengeController(NullLogger<ManagedChallengeController>.Instance, client.Object)
                {
                    ControllerContext = new ControllerContext { HttpContext = CreateContext(transport, client.Object) }
                };
            }

            /// <summary>
            /// The request as the authentication middleware leaves it: the principal the credentials resolved to is
            /// on the context, and the api_client_id claim records which client id authenticated.
            /// </summary>
            private DefaultHttpContext CreateContext(CredentialTransport transport, ICertifyInternalApiClient client)
            {
                var claims = new List<Claim> { new(ClaimTypes.Sid, PrincipalId) };

                if (transport != CredentialTransport.None)
                {
                    claims.Add(new Claim(ApiKeyAuthenticationDefaults.ApiClientIdClaimType, ClientId));
                }

                foreach (var scopedRole in ScopedAssignedRoles ?? [])
                {
                    claims.Add(new Claim(ApiKeyAuthenticationDefaults.ScopedAssignedRoleClaimType, scopedRole));
                }

                var context = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.AuthenticationScheme))
                };

                context.Request.Method = HttpMethods.Post;
                context.Request.Path = "/api/v1/managedchallenge/request";

                if (transport == CredentialTransport.Headers)
                {
                    context.Request.Headers[ApiKeyAuthenticationDefaults.ClientIdHeaderName] = ClientId;
                    context.Request.Headers[ApiKeyAuthenticationDefaults.ClientSecretHeaderName] = ClientSecret;
                }

                var services = new ServiceCollection();
                services.AddSingleton(new ManagedInstanceRequestAuthValidator(client, NullLogger<ManagedInstanceRequestAuthValidator>.Instance));
                services.AddProblemDetailsFactory();
                context.RequestServices = services.BuildServiceProvider();

                return context;
            }
        }

        #endregion
    }
}
