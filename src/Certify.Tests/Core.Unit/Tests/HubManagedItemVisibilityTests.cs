using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Reporting;
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
    /// The managed item listing applies the same visibility as the UI status feed, so a caller whose role is
    /// restricted to domains lists only the items they are sent updates for.
    /// </summary>
    [TestClass]
    public class HubManagedItemVisibilityTests
    {
        private const string CallerId = "sp-domain";
        private const string InstanceId = "instance-1";

        [TestMethod]
        public async Task GetHubManagedItems_DomainRestrictedCallerListsOnlyItemsWithinTheirDomains()
        {
            var controller = CreateController(domainRules: ["*.example.com"]);

            var result = await controller.GetHubManagedItems(null, null) as OkObjectResult;

            var items = ((ManagedCertificateSummaryResult)result!.Value!).Results.Select(r => r.Id).ToList();
            CollectionAssert.AreEquivalent(new[] { "item-within" }, items);
        }

        [TestMethod]
        public async Task GetHubManagedItems_UnrestrictedCallerListsEveryItem()
        {
            var controller = CreateController(domainRules: []);

            var result = await controller.GetHubManagedItems(null, null) as OkObjectResult;

            var items = ((ManagedCertificateSummaryResult)result!.Value!).Results.Select(r => r.Id).ToList();
            CollectionAssert.AreEquivalent(new[] { "item-within", "item-outside" }, items);
        }

        /// <summary>
        /// The pre-aggregated instance summaries count every item, so they are only usable for an unrestricted caller.
        /// </summary>
        [TestMethod]
        public async Task GetHubManagedItemsSummary_DomainRestrictedCallerIsNotGivenTheUnfilteredAggregate()
        {
            var controller = CreateController(domainRules: ["*.example.com"]);

            var result = await controller.GetHubManagedItemsSummary(null, null) as OkObjectResult;

            Assert.AreEqual(1, ((StatusSummary)result!.Value!).Total);
        }

        private static HubController CreateController(List<string> domainRules)
        {
            var role = new AssignedRole
            {
                Id = "ar-viewer",
                RoleId = StandardRoles.HubViewer.Id,
                SecurityPrincipalId = CallerId,
                IncludedResources = domainRules.Select(d => new Resource { ResourceType = ResourceTypes.Domain, Identifier = d }).ToList()
            };

            var client = new Mock<ICertifyInternalApiClient>();

            client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync((AccessCheck check, AuthContext _) => check.ResourceActionId == StandardResourceActions.ManagedItemList);

            client.Setup(c => c.GetSecurityPrincipalAssignedRoles(CallerId, It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<AssignedRole> { role });

            client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(new ResourceAccessScope { HasAccess = true, IsUnrestricted = true, AuthorizingRoles = [role] });

            client.Setup(c => c.GetHubManagedInstances(It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<ManagedInstanceInfo> { new() { InstanceId = InstanceId, Title = "Instance" } });

            client.Setup(c => c.GetTagCategories(It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<TagCategory>());

            client.Setup(c => c.GetAllHubItemTags(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<ItemTag>());

            var items = new ConcurrentDictionary<string, ManagedInstanceItems>();
            items[InstanceId] = new ManagedInstanceItems
            {
                InstanceId = InstanceId,
                Items =
                [
                    Item("item-within", "www.example.com"),
                    Item("item-outside", "www.example.com", "www.example.org")
                ]
            };

            var summaries = new ConcurrentDictionary<string, StatusSummary>();
            summaries[InstanceId] = new StatusSummary { InstanceId = InstanceId, Total = 2 };

            var stateProvider = new Mock<IInstanceManagementStateProvider>();
            stateProvider.Setup(s => s.GetManagedInstanceItems(It.IsAny<string?>())).Returns(items);
            stateProvider.Setup(s => s.GetConnectedInstances()).Returns([]);
            stateProvider.Setup(s => s.GetManagedInstanceStatusSummaries()).Returns(summaries);

            var mgmtApi = new ManagementAPI(
                stateProvider.Object,
                new Mock<IHubContext<InstanceManagementHub, IInstanceManagementHub>>().Object,
                new Mock<Certify.Management.ICertifyManager>().Object,
                NullLogger<ManagementAPI>.Instance);

            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Sid, CallerId)],
                    ApiKeyAuthenticationDefaults.AuthenticationScheme))
            };

            var services = new ServiceCollection();
            services.AddProblemDetailsFactory();
            context.RequestServices = services.BuildServiceProvider();

            return new HubController(NullLogger<CertificateController>.Instance, client.Object, stateProvider.Object, mgmtApi)
            {
                ControllerContext = new ControllerContext { HttpContext = context }
            };
        }

        private static ManagedCertificate Item(string id, params string[] domains)
        {
            return new ManagedCertificate
            {
                Id = id,
                InstanceId = InstanceId,
                Name = id,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = domains.First(),
                    SubjectAlternativeNames = domains
                }
            };
        }
    }
}
