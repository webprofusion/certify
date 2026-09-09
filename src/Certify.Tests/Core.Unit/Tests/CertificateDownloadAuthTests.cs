using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;
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
    /// Authentication and authorization scenarios for the certificate download endpoint, driven through the
    /// controller rather than against the access control primitives underneath it.
    ///
    /// This endpoint hands out private keys and has two quite different authorization routes: an ordinary caller
    /// authorized by their own roles, which is additionally subject to the domain restrictions on those roles, and a
    /// managed instance collecting a certificate it subscribes to, which authorizes by signed request and is
    /// deliberately not domain scoped. Whether each route applies the right one of those is a property of the
    /// endpoint's wiring, so it is only visible from here.
    /// </summary>
    [TestClass]
    public class CertificateDownloadAuthTests
    {
        private const string InstanceId = "instance-1";
        private const string ManagedCertId = "cert-1";
        private static readonly byte[] CertificateBytes = [1, 2, 3, 4];

        #region API token caller

        [TestMethod]
        [Description("An API token whose roles grant certificate download and carry no domain restrictions gets the certificate")]
        public async Task Download_UnrestrictedApiToken_ReturnsCertificate()
        {
            var client = CreateClient(domainRestriction: null, grantsDownload: true);
            var controller = CreateController(client.Object, ApiTokenPrincipal());

            var result = await controller.Download(InstanceId, ManagedCertId, "pfx");

            AssertCertificateReturned(result);
        }

        [TestMethod]
        [Description("An API token restricted to a domain gets a certificate whose identifiers are all within it")]
        public async Task Download_DomainRestrictedApiToken_ReturnsCertificateWithinScope()
        {
            var client = CreateClient(domainRestriction: "*.example.com", grantsDownload: true);
            var controller = CreateController(client.Object, ApiTokenPrincipal());

            var result = await controller.Download(InstanceId, ManagedCertId, "pfx");

            AssertCertificateReturned(result);
        }

        /// <summary>
        /// The reason the domain check cannot happen before the certificate is fetched: it is the identifiers on the
        /// certificate that are being checked, not anything on the request.
        /// </summary>
        [TestMethod]
        [Description("An API token restricted to a different domain is refused, even though its roles grant download")]
        public async Task Download_DomainRestrictedApiToken_IsRefusedOutsideItsScope()
        {
            var client = CreateClient(domainRestriction: "*.permitted.com", grantsDownload: true);
            var controller = CreateController(client.Object, ApiTokenPrincipal());

            var result = await controller.Download(InstanceId, ManagedCertId, "pfx");

            AssertUnauthorized(result, "not permitted by the domain restrictions");
        }

        [TestMethod]
        [Description("A caller whose roles grant neither certificate download nor hub joining is refused")]
        public async Task Download_CallerWithoutDownloadAccess_IsRefused()
        {
            var client = CreateClient(domainRestriction: null, grantsDownload: false);
            var controller = CreateController(client.Object, ApiTokenPrincipal());

            var result = await controller.Download(InstanceId, ManagedCertId, "pfx");

            // no certificate is fetched for a caller who cannot download one
            AssertUnauthorized(result);
        }

        #endregion

        #region Managed instance subscription caller

        /// <summary>
        /// A managed instance collecting a subscribed certificate holds the joining credentials, which do not grant
        /// certificate download. It authorizes by a separate route: the joining action, then a valid request
        /// signature, then its own principal's access to that specific certificate.
        /// </summary>
        [TestMethod]
        [Description("A managed instance with a valid signature and access to the certificate gets it")]
        public async Task Download_SignedManagedInstance_ReturnsSubscribedCertificate()
        {
            var client = CreateClient(domainRestriction: null, grantsDownload: false, grantsJoin: true, instancePrincipalMayDownload: true);
            var controller = CreateController(client.Object, JoiningPrincipal(), signRequest: true);

            var result = await controller.Download(InstanceId, ManagedCertId, "pfx");

            AssertCertificateReturned(result);
        }

        [TestMethod]
        [Description("A managed instance whose own principal has no access to the certificate is refused")]
        public async Task Download_SignedManagedInstance_WithoutCertificateAccess_IsRefused()
        {
            var client = CreateClient(domainRestriction: null, grantsDownload: false, grantsJoin: true, instancePrincipalMayDownload: false);
            var controller = CreateController(client.Object, JoiningPrincipal(), signRequest: true);

            var result = await controller.Download(InstanceId, ManagedCertId, "pfx");

            AssertUnauthorized(result, "not permitted to download this subscribed certificate");
        }

        /// <summary>
        /// Holding the joining credentials is not enough on its own: without a valid signature the hub assigned id
        /// header is just a claim about who is calling.
        /// </summary>
        [TestMethod]
        [Description("The joining credentials alone, with no request signature, are refused")]
        public async Task Download_UnsignedManagedInstance_IsRefused()
        {
            var client = CreateClient(domainRestriction: null, grantsDownload: false, grantsJoin: true, instancePrincipalMayDownload: true);
            var controller = CreateController(client.Object, JoiningPrincipal(), signRequest: false);

            var result = await controller.Download(InstanceId, ManagedCertId, "pfx");

            AssertUnauthorized(result);
        }

        /// <summary>
        /// The subscription route is deliberately not domain scoped: an instance collects the certificates it has
        /// been subscribed to, and which those are is decided by the tag scope on its role rather than by domain
        /// rules. A domain restriction on the joining principal must not silently start filtering them.
        /// </summary>
        [TestMethod]
        [Description("A managed instance is not subject to domain restrictions on the subscription route")]
        public async Task Download_SignedManagedInstance_IsNotDomainScoped()
        {
            var client = CreateClient(
                domainRestriction: "*.permitted.com",
                grantsDownload: false,
                grantsJoin: true,
                instancePrincipalMayDownload: true);

            var controller = CreateController(client.Object, JoiningPrincipal(), signRequest: true);

            var result = await controller.Download(InstanceId, ManagedCertId, "pfx");

            AssertCertificateReturned(result);
        }

        #endregion

        #region Fixtures

        private static void AssertCertificateReturned(IActionResult result)
        {
            Assert.IsInstanceOfType<FileContentResult>(result, $"expected the certificate, got {Describe(result)}");
            CollectionAssert.AreEqual(CertificateBytes, ((FileContentResult)result).FileContents);
        }

        private static void AssertUnauthorized(IActionResult result, string expectedDetail = null)
        {
            Assert.IsInstanceOfType<ObjectResult>(result, $"expected a refusal, got {Describe(result)}");

            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status401Unauthorized, objectResult.StatusCode);

            if (expectedDetail != null)
            {
                var problem = objectResult.Value as ProblemDetails;
                Assert.IsNotNull(problem);
                StringAssert.Contains(problem!.Detail, expectedDetail);
            }
        }

        private static string Describe(IActionResult result)
        {
            return result is ObjectResult o && o.Value is ProblemDetails p
                ? $"{result.GetType().Name} ({o.StatusCode}): {p.Detail}"
                : result.GetType().Name;
        }

        /// <summary>
        /// The certificate the instance holds, with identifiers the domain restriction cases turn on.
        /// </summary>
        private static ManagedCertificate TestCertificate()
        {
            return new ManagedCertificate
            {
                Id = ManagedCertId,
                InstanceId = InstanceId,
                DateRenewed = DateTimeOffset.UtcNow.AddDays(-1),
                CertificateThumbprintHash = "abc123",
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "www.example.com",
                    SubjectAlternativeNames = ["api.example.com"]
                }
            };
        }

        private static ClaimsPrincipal ApiTokenPrincipal()
        {
            return new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Sid, "sp-consumer"),
                    new Claim(ApiKeyAuthenticationDefaults.ApiClientIdClaimType, "client-id")
                ],
                ApiKeyAuthenticationDefaults.AuthenticationScheme));
        }

        /// <summary>
        /// The shared managed instance service principal, which the hub joining credentials belong to.
        /// </summary>
        private static ClaimsPrincipal JoiningPrincipal()
        {
            return new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Sid, AccessControlConfig.ManagedInstanceSecurityPrincipalId),
                    new Claim(ApiKeyAuthenticationDefaults.ApiClientIdClaimType, "joining-client-id")
                ],
                ApiKeyAuthenticationDefaults.AuthenticationScheme));
        }

        /// <summary>
        /// A backend which answers the access questions the endpoint asks, and serves the certificate.
        /// </summary>
        /// <param name="domainRestriction">Domain Match rule on the caller's authorizing role, or null for none.</param>
        /// <param name="grantsDownload">Whether the caller's own roles grant certificate download.</param>
        /// <param name="grantsJoin">Whether the caller's own roles grant hub joining.</param>
        /// <param name="instancePrincipalMayDownload">Whether the instance's own principal may download this certificate.</param>
        private static Mock<ICertifyInternalApiClient> CreateClient(
            string domainRestriction,
            bool grantsDownload,
            bool grantsJoin = false,
            bool instancePrincipalMayDownload = false)
        {
            var client = new Mock<ICertifyInternalApiClient>();

            client.Setup(c => c.CheckSecurityPrincipalHasAccess(
                    It.Is<AccessCheck>(a => a.ResourceActionId == StandardResourceActions.CertificateDownload
                        && a.SecurityPrincipalId != "instance-sp"),
                    It.IsAny<AuthContext>()))
                .ReturnsAsync(grantsDownload);

            client.Setup(c => c.CheckSecurityPrincipalHasAccess(
                    It.Is<AccessCheck>(a => a.ResourceActionId == StandardResourceActions.ManagementHubInstanceJoin),
                    It.IsAny<AuthContext>()))
                .ReturnsAsync(grantsJoin);

            // the instance's own principal, asked about this specific certificate on the subscription route
            client.Setup(c => c.CheckSecurityPrincipalHasAccess(
                    It.Is<AccessCheck>(a => a.SecurityPrincipalId == "instance-sp"
                        && a.ResourceActionId == StandardResourceActions.CertificateDownload),
                    It.IsAny<AuthContext>()))
                .ReturnsAsync(instancePrincipalMayDownload);

            var authorizingRole = new AssignedRole
            {
                Id = "ar-1",
                RoleId = StandardRoles.CertificateConsumer.Id,
                SecurityPrincipalId = "sp-consumer",
                IncludedResources = domainRestriction == null
                    ? []
                    : [new Resource { Id = "res-1", ResourceType = ResourceTypes.Domain, Identifier = domainRestriction }]
            };

            client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(new ResourceAccessScope
                {
                    HasAccess = true,
                    IsUnrestricted = true,
                    AuthorizingRoles = [authorizingRole]
                });

            client.Setup(c => c.GetHubManagedInstance(InstanceId, It.IsAny<AuthContext>()))
                .ReturnsAsync(new ManagedInstanceInfo
                {
                    Id = InstanceId,
                    InstanceId = InstanceId,
                    SecurityPrincipalId = "instance-sp",
                    RequestAuthSecretHash = ManagedInstanceRequestAuth.DeriveSecretHash(InstanceSecret)
                });

            client.Setup(c => c.GetHubItemTags(TaggedItemTypes.ManagedCertificate, ManagedCertId, It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<TagSummary>());

            return client;
        }

        private const string InstanceSecret = "instance-request-auth-secret";

        /// <summary>
        /// The endpoint reaches the instance holding the certificate through ManagementAPI. Pointing the state
        /// provider's hub instance id at the instance under test routes those commands to the in-process manager,
        /// which is where the certificate is served from below.
        /// </summary>
        private static CertificateController CreateController(
            ICertifyInternalApiClient client,
            ClaimsPrincipal caller,
            bool signRequest = false)
        {
            var certifyManager = new Mock<Certify.Management.ICertifyManager>();
            certifyManager.Setup(m => m.PerformHubCommandWithResult(It.IsAny<InstanceCommandRequest>()))
                .ReturnsAsync((InstanceCommandRequest cmd) => new InstanceCommandResult
                {
                    CommandId = cmd.CommandId,
                    CommandType = cmd.CommandType,
                    Value = cmd.CommandType switch
                    {
                        ManagementHubCommands.GetManagedItem => Serialize(TestCertificate()),
                        ManagementHubCommands.ExportCertificate => Serialize(
                            new Models.Config.ActionResult<byte[]>("OK", true) { Result = CertificateBytes }),
                        _ => null
                    }
                });

            var stateProvider = new Mock<IInstanceManagementStateProvider>();
            stateProvider.Setup(s => s.GetManagementHubInstanceId()).Returns(InstanceId);

            var mgmtApi = new ManagementAPI(
                stateProvider.Object,
                new Mock<IHubContext<InstanceManagementHub, IInstanceManagementHub>>().Object,
                certifyManager.Object,
                NullLogger<ManagementAPI>.Instance);

            var controller = new CertificateController(NullLogger<CertificateController>.Instance, client, mgmtApi)
            {
                ControllerContext = new ControllerContext { HttpContext = CreateRequestContext(caller, signRequest, client) }
            };

            return controller;
        }

        private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Certify.Shared.JsonOptions.DefaultJsonSerializerOptions);

        private static DefaultHttpContext CreateRequestContext(ClaimsPrincipal caller, bool signRequest, ICertifyInternalApiClient client)
        {
            var path = $"/api/v1/certificate/{InstanceId}/download/{ManagedCertId}/pfx";

            var context = new DefaultHttpContext { User = caller };
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = path;

            var services = new ServiceCollection();
            services.AddSingleton(new ManagedInstanceRequestAuthValidator(client, NullLogger<ManagedInstanceRequestAuthValidator>.Instance));
            services.AddProblemDetailsFactory();
            context.RequestServices = services.BuildServiceProvider();

            if (signRequest)
            {
                var timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                var bodyHash = ManagedInstanceRequestAuth.ComputeBodyHash([]);

                context.Request.Headers[ManagedInstanceRequestAuth.HubAssignedIdHeaderName] = InstanceId;
                context.Request.Headers[ManagedInstanceRequestAuth.TimestampHeaderName] = timestamp;
                context.Request.Headers[ManagedInstanceRequestAuth.SignatureHeaderName] =
                    ManagedInstanceRequestAuth.ComputeSignatureFromSecret(InstanceSecret, InstanceId, timestamp, HttpMethods.Get, path, bodyHash);
                context.Items[ManagedInstanceRequestAuth.CachedBodyHashItemKey] = bodyHash;
            }

            return context;
        }

        #endregion
    }
}
