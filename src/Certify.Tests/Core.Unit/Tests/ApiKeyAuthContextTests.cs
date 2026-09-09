using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Controllers;
using Certify.Server.Hub.Api.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;
using ActionResultConfig = Certify.Models.Config.ActionResult;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class ApiKeyAuthContextTests
    {
        [TestMethod]
        public async Task ApiKeyAuthenticationHandler_PopulatesPrincipalFromResolvedTokenContext()
        {
            // the handler authenticates the token, it does not authorize an action: a strict mock which only
            // answers ResolveApiToken fails the test if it starts asking whether the principal may do something
            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);
            client.Setup(c => c.ResolveApiToken(
                    It.IsAny<AccessToken>(),
                    It.IsAny<AuthContext>()))
                .ReturnsAsync(new Certify.Models.Config.ActionResult<AccessTokenAuthorizationContext>("OK", true)
                {
                    Result = new AccessTokenAuthorizationContext
                    {
                        SecurityPrincipalId = "sp-123",
                        ScopedAssignedRoles = ["assigned-role-1", "assigned-role-2"]
                    }
                });

            var options = new Mock<IOptionsMonitor<ApiKeyAuthenticationOptions>>(MockBehavior.Strict);
            options.Setup(o => o.Get(It.IsAny<string>())).Returns(new ApiKeyAuthenticationOptions());

            var context = new DefaultHttpContext();
            context.Request.Headers["X-Client-ID"] = "client-id";
            context.Request.Headers["X-Client-Secret"] = "client-secret";

            var handler = new ApiKeyAuthenticationHandler(
                options.Object,
                NullLoggerFactory.Instance,
                UrlEncoder.Default,
                client.Object);

            await handler.InitializeAsync(
                new AuthenticationScheme(ApiKeyAuthenticationDefaults.AuthenticationScheme, ApiKeyAuthenticationDefaults.AuthenticationScheme, typeof(ApiKeyAuthenticationHandler)),
                context);

            var result = await handler.AuthenticateAsync();

            Assert.IsTrue(result.Succeeded);
            Assert.IsNotNull(result.Principal);
            Assert.AreEqual("sp-123", result.Principal!.FindFirstValue(ClaimTypes.Sid));

            var scopedAssignedRoles = result.Principal.FindAll(ApiKeyAuthenticationDefaults.ScopedAssignedRoleClaimType).Select(c => c.Value).ToList();
            CollectionAssert.AreEquivalent(new[] { "assigned-role-1", "assigned-role-2" }, scopedAssignedRoles);
        }

        /// <summary>
        /// The managed challenge API has always accepted the client id and secret as AuthKey/AuthSecret fields in
        /// the request body, and the Certify managed DNS provider in released agents sends them only that way. The
        /// handler resolves that transport too, which is what lets those endpoints authenticate through the
        /// middleware instead of reading credentials off the request themselves.
        /// </summary>
        [TestMethod]
        public async Task ApiKeyAuthenticationHandler_AuthenticatesCredentialsCarriedInTheRequestBody()
        {
            var client = CreateResolvingClient("sp-inline", ["assigned-role-inline"]);

            var context = CreateJsonBodyContext(@"{""ChallengeType"":""dns-01"",""AuthKey"":""client-id"",""AuthSecret"":""client-secret""}");

            var result = await AuthenticateAsync(context, client);

            Assert.IsTrue(result.Succeeded, "credentials in the request body should authenticate");
            Assert.AreEqual("sp-inline", result.Principal!.FindFirstValue(ClaimTypes.Sid));

            var scopedAssignedRoles = result.Principal.FindAll(ApiKeyAuthenticationDefaults.ScopedAssignedRoleClaimType).Select(c => c.Value).ToList();
            CollectionAssert.AreEquivalent(new[] { "assigned-role-inline" }, scopedAssignedRoles);
        }

        /// <summary>
        /// Field names travel over the wire and are serialized by clients we do not control, so a casing difference
        /// must not decide whether a caller authenticates.
        /// </summary>
        [TestMethod]
        public async Task ApiKeyAuthenticationHandler_MatchesInlineCredentialFieldNamesIgnoringCase()
        {
            var client = CreateResolvingClient("sp-inline", []);

            var context = CreateJsonBodyContext(@"{""authKey"":""client-id"",""authSecret"":""client-secret""}");

            var result = await AuthenticateAsync(context, client);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual("sp-inline", result.Principal!.FindFirstValue(ClaimTypes.Sid));
        }

        /// <summary>
        /// Reading the body must leave it readable, or model binding sees an empty request after authentication.
        /// </summary>
        [TestMethod]
        public async Task ApiKeyAuthenticationHandler_RewindsTheRequestBodyAfterReadingCredentials()
        {
            var client = CreateResolvingClient("sp-inline", []);

            var body = @"{""AuthKey"":""client-id"",""AuthSecret"":""client-secret"",""Identifier"":""www.example.com""}";
            var context = CreateJsonBodyContext(body);

            await AuthenticateAsync(context, client);

            using var reader = new System.IO.StreamReader(context.Request.Body);
            Assert.AreEqual(body, await reader.ReadToEndAsync());
        }

        /// <summary>
        /// A request carrying no credential in either transport is not a failed authentication, it is no result, so
        /// the request can still reach an endpoint which does not require one.
        /// </summary>
        [TestMethod]
        public async Task ApiKeyAuthenticationHandler_ReturnsNoResultWhenTheBodyCarriesNoCredentials()
        {
            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);

            var context = CreateJsonBodyContext(@"{""ChallengeType"":""dns-01"",""Identifier"":""www.example.com""}");

            var result = await AuthenticateAsync(context, client.Object);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Failure, "a request with no credentials has not failed authentication");
        }

        /// <summary>
        /// This path runs for callers who have not authenticated, so an anonymous request must not be able to make
        /// the hub buffer and parse an arbitrarily large body before anything knows who sent it.
        /// </summary>
        [TestMethod]
        public async Task ApiKeyAuthenticationHandler_IgnoresAnOversizedBody()
        {
            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);

            var padding = new string('x', 128 * 1024);
            var context = CreateJsonBodyContext($@"{{""Padding"":""{padding}"",""AuthKey"":""client-id"",""AuthSecret"":""client-secret""}}");

            var result = await AuthenticateAsync(context, client.Object);

            Assert.IsFalse(result.Succeeded, "a body over the size limit should not be parsed for credentials");
        }

        /// <summary>
        /// A body which is not JSON, or is malformed, carries no credential and is not this layer's error to report.
        /// </summary>
        [TestMethod]
        public async Task ApiKeyAuthenticationHandler_IgnoresANonJsonBody()
        {
            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);

            var context = CreateJsonBodyContext("AuthKey=client-id&AuthSecret=client-secret", contentType: "application/x-www-form-urlencoded");

            var result = await AuthenticateAsync(context, client.Object);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Failure);
        }

        /// <summary>
        /// Headers remain the primary transport: a request presenting both is authenticated from the headers.
        /// </summary>
        [TestMethod]
        public async Task ApiKeyAuthenticationHandler_PrefersHeaderCredentialsOverInlineOnes()
        {
            AccessToken? resolved = null;

            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);
            client.Setup(c => c.ResolveApiToken(It.IsAny<AccessToken>(), It.IsAny<AuthContext>()))
                .Callback<AccessToken, AuthContext>((token, _) => resolved = token)
                .ReturnsAsync(new Certify.Models.Config.ActionResult<AccessTokenAuthorizationContext>("OK", true)
                {
                    Result = new AccessTokenAuthorizationContext { SecurityPrincipalId = "sp-header" }
                });

            var context = CreateJsonBodyContext(@"{""AuthKey"":""body-client-id"",""AuthSecret"":""body-secret""}");
            context.Request.Headers[ApiKeyAuthenticationDefaults.ClientIdHeaderName] = "header-client-id";
            context.Request.Headers[ApiKeyAuthenticationDefaults.ClientSecretHeaderName] = "header-secret";

            var result = await AuthenticateAsync(context, client.Object);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual("header-client-id", resolved!.ClientId);
        }

        private static ICertifyInternalApiClient CreateResolvingClient(string securityPrincipalId, string[] scopedAssignedRoles)
        {
            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);
            client.Setup(c => c.ResolveApiToken(It.IsAny<AccessToken>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(new Certify.Models.Config.ActionResult<AccessTokenAuthorizationContext>("OK", true)
                {
                    Result = new AccessTokenAuthorizationContext
                    {
                        SecurityPrincipalId = securityPrincipalId,
                        ScopedAssignedRoles = [.. scopedAssignedRoles]
                    }
                });

            return client.Object;
        }

        private static DefaultHttpContext CreateJsonBodyContext(string body, string contentType = "application/json")
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(body);

            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Post;
            context.Request.ContentType = contentType;
            context.Request.ContentLength = bytes.Length;
            context.Request.Body = new System.IO.MemoryStream(bytes);

            return context;
        }

        private static async Task<AuthenticateResult> AuthenticateAsync(HttpContext context, ICertifyInternalApiClient client)
        {
            var options = new Mock<IOptionsMonitor<ApiKeyAuthenticationOptions>>(MockBehavior.Strict);
            options.Setup(o => o.Get(It.IsAny<string>())).Returns(new ApiKeyAuthenticationOptions());

            var handler = new ApiKeyAuthenticationHandler(
                options.Object,
                NullLoggerFactory.Instance,
                UrlEncoder.Default,
                client);

            await handler.InitializeAsync(
                new AuthenticationScheme(ApiKeyAuthenticationDefaults.AuthenticationScheme, ApiKeyAuthenticationDefaults.AuthenticationScheme, typeof(ApiKeyAuthenticationHandler)),
                context);

            return await handler.AuthenticateAsync();
        }

        [TestMethod]
        public void CurrentAuthContext_UsesAuthenticatedClaimsWithoutBearerHeader()
        {
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [
                            new Claim(ClaimTypes.Sid, "sp-456"),
                            new Claim(ApiKeyAuthenticationDefaults.ScopedAssignedRoleClaimType, "assigned-role-a")
                        ],
                        ApiKeyAuthenticationDefaults.AuthenticationScheme))
            };

            var controller = new ApiControllerBase
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = context
                }
            };

            var authContextProperty = typeof(ApiControllerBase).GetProperty("CurrentAuthContext", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(authContextProperty);

            var authContext = authContextProperty!.GetValue(controller) as AuthContext;
            Assert.IsNotNull(authContext);
            Assert.AreEqual("sp-456", authContext!.UserId);
            CollectionAssert.AreEquivalent(new[] { "assigned-role-a" }, authContext.ScopedAssignedRoles);
        }

        /// <summary>
        /// A request authorized by an API access token is subject to the domain restrictions on the roles that
        /// token's scope selects. The principal arrives as claims from the ApiToken scheme, so the check has to
        /// read it from there rather than failing closed and rejecting every API token call.
        /// </summary>
        [TestMethod]
        public async Task CheckIdentifiersAuthorized_EnforcesDomainRestrictionsForAnApiTokenPrincipal()
        {
            var (controller, client) = CreateAccessTokenAuthorizedController(domainRestriction: "*.example.com");

            var requestAuthorized = await InvokeCheckRequestAuthorized(controller, client);
            Assert.IsTrue(requestAuthorized.IsSuccess, requestAuthorized.Message);

            var permitted = await InvokeCheckIdentifiersAuthorized(controller, client, "www.example.com");
            Assert.IsTrue(permitted.IsSuccess, permitted.Message);

            var denied = await InvokeCheckIdentifiersAuthorized(controller, client, "www.notpermitted.com");
            Assert.IsFalse(denied.IsSuccess);
            StringAssert.Contains(denied.Message, "not permitted by the domain restrictions");
        }

        /// <summary>
        /// A principal whose authorizing roles carry no domain resources is unrestricted, so the check passes without
        /// needing to know the identifiers.
        /// </summary>
        [TestMethod]
        public async Task CheckIdentifiersAuthorized_ApiTokenPrincipalWithoutDomainRestrictionsIsUnrestricted()
        {
            var (controller, client) = CreateAccessTokenAuthorizedController(domainRestriction: null);

            var requestAuthorized = await InvokeCheckRequestAuthorized(controller, client);
            Assert.IsTrue(requestAuthorized.IsSuccess, requestAuthorized.Message);

            var result = await InvokeCheckIdentifiersAuthorized(controller, client, "anything.example.org");
            Assert.IsTrue(result.IsSuccess, result.Message);
        }

        /// <summary>
        /// An API access token is issued scoped to specific role assignments. Being authenticated as a principal is
        /// not authority to act as every role that principal holds, and only the access check itself crosses to the
        /// access control store, so the scope has to be carried on it or the token is evaluated unscoped.
        /// </summary>
        [TestMethod]
        public async Task CheckRequestAuthorized_ForwardsTheTokensRoleScopeToTheAccessCheck()
        {
            AccessCheck? evaluated = null;

            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);
            client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync((AccessCheck check, AuthContext _) =>
                {
                    evaluated = check;
                    return true;
                });

            var controller = CreateApiTokenAuthenticatedController("sp-scoped", "ar-scoped");

            var result = await InvokeCheckRequestAuthorized(controller, client.Object);

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.IsNotNull(evaluated);
            Assert.AreEqual("sp-scoped", evaluated!.SecurityPrincipalId);
            CollectionAssert.AreEquivalent(
                new[] { "ar-scoped" },
                evaluated.ScopedAssignedRoles,
                "an unscoped check evaluates every role the principal holds, so a token scoped to one assignment would authorize as all of them");
        }

        /// <summary>
        /// A check which names another security principal asks whether that principal has access, which the caller's
        /// own token scope does not narrow. Applying it there would answer a different question than the one asked.
        /// </summary>
        [TestMethod]
        public async Task CheckRequestAuthorized_DoesNotApplyTheCallersScopeToAnotherPrincipal()
        {
            AccessCheck? evaluated = null;

            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);
            client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync((AccessCheck check, AuthContext _) =>
                {
                    evaluated = check;
                    return true;
                });

            var controller = CreateApiTokenAuthenticatedController("sp-scoped", "ar-scoped");

            var result = await InvokeCheckRequestAuthorized(
                controller,
                client.Object,
                new AccessCheck("sp-other", ResourceTypes.Certificate, StandardResourceActions.CertificateDownload));

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.IsNotNull(evaluated);
            Assert.AreEqual("sp-other", evaluated!.SecurityPrincipalId);
            Assert.IsEmpty(evaluated.ScopedAssignedRoles);
        }

        private static ApiControllerBase CreateApiTokenAuthenticatedController(string securityPrincipalId, params string[] scopedAssignedRoleIds)
        {
            var claims = new List<Claim> { new(ClaimTypes.Sid, securityPrincipalId) };

            claims.AddRange(scopedAssignedRoleIds.Select(id => new Claim(ApiKeyAuthenticationDefaults.ScopedAssignedRoleClaimType, id)));

            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.AuthenticationScheme))
            };

            return new ApiControllerBase
            {
                ControllerContext = new ControllerContext { HttpContext = context }
            };
        }

        private static (ApiControllerBase Controller, ICertifyInternalApiClient Client) CreateAccessTokenAuthorizedController(string? domainRestriction)
        {
            var authorizingRole = new AssignedRole
            {
                Id = "ar-1",
                RoleId = "cert_consumer_role",
                SecurityPrincipalId = "sp-token",
                IncludedResources = domainRestriction == null
                    ? []
                    : [new Resource { ResourceType = ResourceTypes.Domain, Identifier = domainRestriction }]
            };

            // strict: the token was resolved by the authentication middleware, so a controller which asks the
            // backend to resolve it a second time fails the test rather than quietly costing a store lookup
            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);

            client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync((AccessCheck check, AuthContext _) =>
                {
                    Assert.AreEqual("sp-token", check.SecurityPrincipalId);
                    CollectionAssert.AreEquivalent(new[] { "ar-1" }, check.ScopedAssignedRoles);

                    return true;
                });

            client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync((AccessCheck check, AuthContext _) =>
                {
                    // the scope must be evaluated for the token's principal and role scope, not for an anonymous caller
                    Assert.AreEqual("sp-token", check.SecurityPrincipalId);
                    CollectionAssert.AreEquivalent(new[] { "ar-1" }, check.ScopedAssignedRoles);

                    return new ResourceAccessScope
                    {
                        HasAccess = true,
                        IsUnrestricted = true,
                        AuthorizingRoles = [authorizingRole]
                    };
                });

            // the ApiToken scheme resolved the credential before the endpoint ran, so the principal and its role
            // scope arrive as claims - these endpoints used to be [AllowAnonymous] and read the credential themselves
            var controller = CreateApiTokenAuthenticatedController("sp-token", "ar-1");

            return (controller, client.Object);
        }

        private static async Task<ActionResultConfig> InvokeCheckRequestAuthorized(ApiControllerBase controller, ICertifyInternalApiClient client, AccessCheck? check = null)
        {
            var method = typeof(ApiControllerBase).GetMethod("CheckRequestAuthorized", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            check ??= new AccessCheck(default!, ResourceTypes.Certificate, StandardResourceActions.CertificateDownload);

            return await (Task<ActionResultConfig>)method!.Invoke(controller, [client, check])!;
        }

        private static async Task<ActionResultConfig> InvokeCheckIdentifiersAuthorized(ApiControllerBase controller, ICertifyInternalApiClient client, params string?[] identifiers)
        {
            var method = typeof(ApiControllerBase).GetMethod("CheckIdentifiersAuthorized", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            return await (Task<ActionResultConfig>)method!.Invoke(
                controller,
                [client, StandardResourceActions.CertificateDownload, identifiers])!;
        }
    }
}
