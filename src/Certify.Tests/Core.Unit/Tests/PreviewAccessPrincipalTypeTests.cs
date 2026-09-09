using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Providers;
using Certify.Server.Hub.Api.Controllers;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// Preview Access answers what a security principal can reach, for every principal type.
    ///
    /// Certificate access is granted by the certificate download action, which a user or application principal can
    /// hold just as a managed instance can. The preview used to resolve the principal to a managed instance first and
    /// return nothing when there was none, so every user and application principal previewed as having no certificate
    /// access at all - regardless of the roles they actually held.
    ///
    /// The one thing that is specific to a managed instance is that its own items are left out, because it already
    /// holds them locally and would not subscribe to them.
    /// </summary>
    [TestClass]
    public class PreviewAccessPrincipalTypeTests
    {
        private const string AdminId = "sp-admin";
        private const string UserPrincipalId = "sp-user";
        private const string InstancePrincipalId = "sp-managed-instance";
        private const string OwnInstanceId = "instance-own";
        private const string OtherInstanceId = "instance-other";

        [TestMethod]
        [Description("A user principal previews the certificates its roles permit, though it has no managed instance")]
        public async Task UserPrincipal_ListsThePermittedCertificates()
        {
            var fixture = new Fixture();
            var controller = fixture.CreateController();

            var result = await controller.GetSubscribableManagedCertificatesBySecurityPrincipal(UserPrincipalId);

            var items = fixture.Items(result);
            CollectionAssert.AreEquivalent(
                new[] { "cert-own-instance", "cert-other-instance" },
                items.Select(i => i.Id).ToList(),
                "a principal with no instance of its own has no items to leave out");
        }

        [TestMethod]
        [Description("A managed instance principal is not offered the certificates from its own instance")]
        public async Task ManagedInstancePrincipal_ExcludesItsOwnItems()
        {
            var fixture = new Fixture();
            var controller = fixture.CreateController();

            var result = await controller.GetSubscribableManagedCertificatesBySecurityPrincipal(InstancePrincipalId);

            var items = fixture.Items(result);
            CollectionAssert.AreEquivalent(
                new[] { "cert-other-instance" },
                items.Select(i => i.Id).ToList(),
                "an instance already holds its own certificates, so it does not subscribe to them");
        }

        [TestMethod]
        [Description("A principal whose roles do not permit a certificate does not see it, whatever its type")]
        public async Task PrincipalWithoutDownloadAccess_ListsNothing()
        {
            var fixture = new Fixture { GrantCertificateAccess = false };
            var controller = fixture.CreateController();

            var result = await controller.GetSubscribableManagedCertificatesBySecurityPrincipal(UserPrincipalId);

            Assert.IsEmpty(fixture.Items(result));
        }

        [TestMethod]
        [Description("Previewing a user principal as one of its API tokens narrows the check to that token's scope")]
        public async Task UserPrincipal_AsScopedAccessToken_ChecksWithTheTokenScope()
        {
            var fixture = new Fixture();
            var controller = fixture.CreateController();

            var result = await controller.GetSubscribableManagedCertificatesBySecurityPrincipal(UserPrincipalId, Fixture.ScopedTokenId);

            Assert.IsInstanceOfType<OkObjectResult>(result, fixture.Describe(result));
            Assert.IsNotEmpty(fixture.CertificateChecks, "the certificates still have to be evaluated");
            Assert.IsTrue(
                fixture.CertificateChecks.TrueForAll(c => c.ScopedAssignedRoles.Contains(Fixture.ScopedAssignedRoleId)),
                "every certificate is evaluated against the role assignments the token is scoped to");
        }

        #region Fixture

        private sealed class Fixture
        {
            public const string ScopedTokenId = "token-scoped";
            public const string ScopedAssignedRoleId = "ar-certificate-consumer";

            /// <summary>Whether the previewed principal's roles permit downloading the certificates.</summary>
            public bool GrantCertificateAccess { get; init; } = true;

            /// <summary>The certificate download checks the endpoint made, in order.</summary>
            public List<AccessCheck> CertificateChecks { get; } = [];

            public string Describe(IActionResult result)
            {
                return result is ObjectResult o && o.Value is ProblemDetails p
                    ? $"{result.GetType().Name} ({o.StatusCode}): {p.Detail}"
                    : result.GetType().Name;
            }

            public List<ManagedCertificateSummary> Items(IActionResult result)
            {
                Assert.IsInstanceOfType<OkObjectResult>(result, Describe(result));

                return (List<ManagedCertificateSummary>)((OkObjectResult)result).Value!;
            }

            public HubController CreateController()
            {
                var client = new Mock<ICertifyInternalApiClient>();

                client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((AccessCheck check, AuthContext _) =>
                    {
                        if (check.ResourceType != ResourceTypes.Certificate)
                        {
                            // the admin caller's own authorization for the endpoint, not the preview itself
                            return true;
                        }

                        CertificateChecks.Add(check);

                        return GrantCertificateAccess;
                    });

                client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync(new ResourceAccessScope { HasAccess = true, IsUnrestricted = true });

                client.Setup(c => c.GetAssignedAccessTokens(It.IsAny<AuthContext>()))
                    .ReturnsAsync(new List<AssignedAccessToken>
                    {
                        new()
                        {
                            Id = ScopedTokenId,
                            SecurityPrincipalId = UserPrincipalId,
                            ScopedAssignedRoles = [ScopedAssignedRoleId]
                        }
                    });

                // only the managed instance principal has an instance of its own
                client.Setup(c => c.GetHubManagedInstances(It.IsAny<AuthContext>()))
                    .ReturnsAsync(new List<ManagedInstanceInfo>
                    {
                        new() { InstanceId = OwnInstanceId, Title = "Own instance", SecurityPrincipalId = InstancePrincipalId },
                        new() { InstanceId = OtherInstanceId, Title = "Other instance", SecurityPrincipalId = "sp-other-instance" }
                    });

                client.Setup(c => c.GetHubItemTags(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync(new List<TagSummary>());

                var stateProvider = new Mock<IInstanceManagementStateProvider>();
                stateProvider.Setup(s => s.GetManagedInstanceItems()).Returns(ManagedItems());

                var mgmtApi = new ManagementAPI(
                    stateProvider.Object,
                    new Mock<IHubContext<InstanceManagementHub, IInstanceManagementHub>>().Object,
                    new Mock<Certify.Management.ICertifyManager>().Object,
                    NullLogger<ManagementAPI>.Instance);

                return new HubController(NullLogger<CertificateController>.Instance, client.Object, stateProvider.Object, mgmtApi)
                {
                    ControllerContext = new ControllerContext { HttpContext = CreateContext() }
                };
            }

            /// <summary>
            /// One certificate held by the managed instance principal's own instance, and one held elsewhere.
            /// </summary>
            private static ConcurrentDictionary<string, ManagedInstanceItems> ManagedItems()
            {
                var items = new ConcurrentDictionary<string, ManagedInstanceItems>();

                items[OwnInstanceId] = new ManagedInstanceItems
                {
                    InstanceId = OwnInstanceId,
                    Items = [Certificate("cert-own-instance", OwnInstanceId, "own.example.com")]
                };

                items[OtherInstanceId] = new ManagedInstanceItems
                {
                    InstanceId = OtherInstanceId,
                    Items = [Certificate("cert-other-instance", OtherInstanceId, "other.example.com")]
                };

                return items;
            }

            private static ManagedCertificate Certificate(string id, string instanceId, string primaryDomain)
            {
                return new ManagedCertificate
                {
                    Id = id,
                    InstanceId = instanceId,
                    Name = primaryDomain,
                    RequestConfig = new CertRequestConfig { PrimaryDomain = primaryDomain }
                };
            }

            /// <summary>
            /// The request as an authenticated admin using the Preview Access dialog, which is a different principal
            /// from the one being previewed.
            /// </summary>
            private static DefaultHttpContext CreateContext()
            {
                var context = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Sid, AdminId)],
                        ApiKeyAuthenticationDefaults.AuthenticationScheme))
                };

                context.Request.Method = HttpMethods.Get;
                context.Request.Path = "/api/internal/v1/hub/subscription/available/securityprincipal";

                var services = new ServiceCollection();
                services.AddProblemDetailsFactory();
                context.RequestServices = services.BuildServiceProvider();

                return context;
            }
        }

        #endregion
    }
}
