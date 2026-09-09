using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Controllers;
using Certify.Server.Hub.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// The Preview Access dialog asks what a chosen identity can reach, not what its security principal can reach
    /// overall. A principal presents itself with every role it holds, but an API access token is issued scoped to
    /// particular role assignments, so previewing a token has to narrow the access check the same way a live request
    /// with that token would - otherwise the preview reports access the token does not actually have.
    ///
    /// These drive the endpoint the dialog calls for managed challenges. The certificate endpoint threads the same
    /// scope through <c>CheckSubscribableManagedCertsForInstance</c>.
    /// </summary>
    [TestClass]
    public class PreviewAccessIdentityScopeTests
    {
        private const string PrincipalId = "sp-managed-instance";
        private const string AdminId = "sp-admin";
        private const string ScopedAssignedRoleId = "ar-challenge-consumer";

        [TestMethod]
        [Description("Previewing the principal itself checks access against its whole role set")]
        public async Task NoAccessToken_ChecksWithNoRoleScope()
        {
            var fixture = new Fixture();
            var controller = fixture.CreateController();

            var result = await controller.GetSubscribableManagedChallengesBySecurityPrincipal(PrincipalId);

            Assert.IsInstanceOfType<OkObjectResult>(result, fixture.Describe(result));
            Assert.IsEmpty(
                fixture.Checks.Single().ScopedAssignedRoles,
                "the principal holds every assignment, so nothing narrows the check");
        }

        [TestMethod]
        [Description("Previewing an API token narrows the check to the role assignments that token is scoped to")]
        public async Task ScopedAccessToken_ChecksWithTheTokenScope()
        {
            var fixture = new Fixture();
            var controller = fixture.CreateController();

            var result = await controller.GetSubscribableManagedChallengesBySecurityPrincipal(PrincipalId, Fixture.ScopedTokenId);

            Assert.IsInstanceOfType<OkObjectResult>(result, fixture.Describe(result));
            CollectionAssert.AreEquivalent(
                new[] { ScopedAssignedRoleId },
                fixture.Checks.Single().ScopedAssignedRoles,
                "a scoped token reaches only what its own assignments allow");
        }

        [TestMethod]
        [Description("Previewing an unscoped API token checks the principal's whole role set, as that token would")]
        public async Task UnscopedAccessToken_ChecksWithNoRoleScope()
        {
            var fixture = new Fixture();
            var controller = fixture.CreateController();

            var result = await controller.GetSubscribableManagedChallengesBySecurityPrincipal(PrincipalId, Fixture.UnscopedTokenId);

            Assert.IsInstanceOfType<OkObjectResult>(result, fixture.Describe(result));
            Assert.IsEmpty(
                fixture.Checks.Single().ScopedAssignedRoles,
                "an unscoped token can do anything its principal can");
        }

        /// <summary>
        /// Reporting an unresolvable token as unscoped would preview the principal's whole role set under a token
        /// which grants far less, so this fails rather than falling back to the wider identity.
        /// </summary>
        [TestMethod]
        [Description("An access token belonging to another principal is refused rather than previewed unscoped")]
        public async Task AccessTokenForAnotherPrincipal_IsRefused()
        {
            var fixture = new Fixture();
            var controller = fixture.CreateController();

            var result = await controller.GetSubscribableManagedChallengesBySecurityPrincipal(PrincipalId, Fixture.OtherPrincipalTokenId);

            Assert.IsInstanceOfType<ObjectResult>(result, fixture.Describe(result));
            Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result).StatusCode);
            Assert.IsEmpty(fixture.Checks, "nothing should be evaluated once the identity cannot be resolved");
        }

        #region Fixture

        private sealed class Fixture
        {
            public const string ScopedTokenId = "token-scoped";
            public const string UnscopedTokenId = "token-unscoped";
            public const string OtherPrincipalTokenId = "token-other-principal";

            /// <summary>The access checks the endpoint made, in order.</summary>
            public List<AccessCheck> Checks { get; } = [];

            public string Describe(IActionResult result)
            {
                return result is ObjectResult o && o.Value is ProblemDetails p
                    ? $"{result.GetType().Name} ({o.StatusCode}): {p.Detail}"
                    : result.GetType().Name;
            }

            public InternalManagedChallengeController CreateController()
            {
                var client = new Mock<ICertifyInternalApiClient>();

                client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((AccessCheck check, AuthContext _) =>
                    {
                        // the admin caller's own authorization check for the endpoint is not about the previewed principal
                        if (check.SecurityPrincipalId == PrincipalId)
                        {
                            Checks.Add(check);
                        }

                        return true;
                    });

                client.Setup(c => c.GetAssignedAccessTokens(It.IsAny<AuthContext>()))
                    .ReturnsAsync(new List<AssignedAccessToken>
                    {
                        new()
                        {
                            Id = ScopedTokenId,
                            SecurityPrincipalId = PrincipalId,
                            ScopedAssignedRoles = [ScopedAssignedRoleId]
                        },
                        new()
                        {
                            Id = UnscopedTokenId,
                            SecurityPrincipalId = PrincipalId
                        },
                        new()
                        {
                            Id = OtherPrincipalTokenId,
                            SecurityPrincipalId = "sp-someone-else",
                            ScopedAssignedRoles = ["ar-someone-else"]
                        }
                    });

                client.Setup(c => c.GetManagedChallenges(It.IsAny<AuthContext>()))
                    .ReturnsAsync(new List<ManagedChallenge>
                    {
                        new()
                        {
                            Id = "challenge-1",
                            Title = "Example DNS challenge",
                            ChallengeConfig = new Certify.Models.CertRequestChallengeConfig { DomainMatch = "*.example.com" }
                        }
                    });

                client.Setup(c => c.GetAllHubItemTags(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync(new List<ItemTag>());

                client.Setup(c => c.GetTagCategories(It.IsAny<AuthContext>()))
                    .ReturnsAsync(new List<TagCategory>());

                return new InternalManagedChallengeController(NullLogger<InternalManagedChallengeController>.Instance, client.Object)
                {
                    ControllerContext = new ControllerContext { HttpContext = CreateContext() }
                };
            }

            /// <summary>
            /// The request as an authenticated admin using the Preview Access dialog, which is a different principal
            /// from the one being previewed.
            /// </summary>
            private static DefaultHttpContext CreateContext()
            {
                var claims = new List<Claim> { new(ClaimTypes.Sid, AdminId) };

                var context = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.AuthenticationScheme))
                };

                context.Request.Method = HttpMethods.Get;
                context.Request.Path = $"/api/internal/v1/managedchallenges/available/securityprincipal/{PrincipalId}";

                var services = new ServiceCollection();
                services.AddProblemDetailsFactory();
                context.RequestServices = services.BuildServiceProvider();

                return context;
            }
        }

        #endregion
    }
}
