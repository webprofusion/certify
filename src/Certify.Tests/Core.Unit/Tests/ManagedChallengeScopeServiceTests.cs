using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// Managed ACME tag scoped access resolution via the hub scope service.
    /// </summary>
    [TestClass]
    public class ManagedChallengeScopeServiceTests
    {
        private const string DepartmentCategory = "department";
        private const string FinanceDept = "finance";

        private static Mock<ICertifyInternalApiClient> CreateClient(bool allowUnscoped, ICollection<ItemTag> tags)
        {
            var client = new Mock<ICertifyInternalApiClient>();

            client.Setup(c => c.GetHubSettings(It.IsAny<AuthContext>()))
                .ReturnsAsync(new HubSettings
                {
                    ManagedChallenge = new ManagedChallengeSettings { AllowUnscopedForScopedPrincipals = allowUnscoped }
                });

            client.Setup(c => c.GetManagedChallenges(It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<ManagedChallenge>
                {
                    new ManagedChallenge
                    {
                        Id = "challenge-finance",
                        ChallengeConfig = new CertRequestChallengeConfig { DomainMatch = "*.finance.example.com", ChallengeType = "dns-01" }
                    }
                });

            client.Setup(c => c.GetAllHubItemTags(null, null, TaggedItemTypes.ManagedChallenge, null, It.IsAny<AuthContext>()))
                .ReturnsAsync(tags);

            client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync((AccessCheck check, AuthContext _) => new ResourceAccessScope
                {
                    HasAccess = true,
                    IsUnrestricted = false,
                    AllowUnscopedResources = check.AllowUnscopedResources,
                    AuthorizingRoles =
                    [
                        new AssignedRole
                        {
                            Id = "ar-1",
                            RoleId = StandardRoles.ManagedAcmeConsumer.Id,
                            SecurityPrincipalId = "sp-1",
                            ScopedTags = [new TagScope { CategoryKey = DepartmentCategory, Value = FinanceDept }]
                        }
                    ]
                });

            return client;
        }

        [TestMethod]
        [Description("Tag scoped managed ACME principal can satisfy identifiers when the managed challenge carries a matching tag")]
        public async Task TagScopedPrincipal_WithMatchingChallengeTag_CanSatisfyIdentifier()
        {
            var tags = new List<ItemTag>
            {
                new ItemTag("challenge-finance", TaggedItemTypes.ManagedChallenge, DepartmentCategory, FinanceDept)
            };

            var client = CreateClient(allowUnscoped: false, tags);
            var service = new ManagedChallengeScopeService(client.Object, NullLogger<ManagedChallengeScopeService>.Instance);

            var result = await service.ValidatePrincipalCanSatisfyIdentifiers(
                "sp-1",
                ["app.finance.example.com"],
                null,
                StandardResourceActions.ManagedAcmePerformOrder);

            Assert.IsTrue(result.CanSatisfy, result.FailureReason);
            Assert.HasCount(1, result.AccessibleChallenges);
        }

        [TestMethod]
        [Description("Tag scoped managed ACME principal is denied when challenge tags cannot be resolved and unscoped is not allowed")]
        public async Task TagScopedPrincipal_WhenChallengeTagsUnavailable_IsDenied()
        {
            var client = CreateClient(allowUnscoped: false, new List<ItemTag>());
            var service = new ManagedChallengeScopeService(client.Object, NullLogger<ManagedChallengeScopeService>.Instance);

            var result = await service.ValidatePrincipalCanSatisfyIdentifiers(
                "sp-1",
                ["app.finance.example.com"],
                null,
                StandardResourceActions.ManagedAcmePerformOrder);

            Assert.IsFalse(result.CanSatisfy);
            Assert.IsEmpty(result.AccessibleChallenges);
        }

        [TestMethod]
        [Description("Allow unscoped hub setting permits untagged managed challenges for tag scoped principals")]
        public async Task TagScopedPrincipal_WithAllowUnscoped_CanSatisfyUntaggedChallenge()
        {
            var client = CreateClient(allowUnscoped: true, new List<ItemTag>());
            var service = new ManagedChallengeScopeService(client.Object, NullLogger<ManagedChallengeScopeService>.Instance);

            var result = await service.ValidatePrincipalCanSatisfyIdentifiers(
                "sp-1",
                ["app.finance.example.com"],
                null,
                StandardResourceActions.ManagedAcmePerformOrder);

            Assert.IsTrue(result.CanSatisfy, result.FailureReason);
            Assert.HasCount(1, result.AccessibleChallenges);
        }

        [TestMethod]
        [Description("A principal whose roles do not grant the action is denied, and the denial reads back its stored role assignments")]
        public async Task PrincipalWithoutAuthorizingRole_IsDeniedAndRoleAssignmentsAreReported()
        {
            var client = CreateClient(allowUnscoped: false, new List<ItemTag>());

            client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(new ResourceAccessScope { HasAccess = false });

            client.Setup(c => c.GetSecurityPrincipalRoleStatus("sp-1", It.IsAny<AuthContext>()))
                .ReturnsAsync(new RoleStatus
                {
                    AssignedRoles = [new AssignedRole { Id = "ar-1", RoleId = StandardRoles.HubViewer.Id, SecurityPrincipalId = "sp-1" }],
                    Roles = [new Role(StandardRoles.HubViewer.Id, "Hub Viewer", "", policies: [StandardPolicies.ManagementHubReader])],
                    Policies = [new ResourcePolicy { Id = StandardPolicies.ManagementHubReader, ResourceActions = [StandardResourceActions.ManagedChallengeList] }]
                });

            var service = new ManagedChallengeScopeService(client.Object, NullLogger<ManagedChallengeScopeService>.Instance);

            var result = await service.ValidatePrincipalCanSatisfyIdentifiers(
                "sp-1",
                ["app.finance.example.com"],
                null,
                StandardResourceActions.ManagedChallengeCleanup);

            Assert.IsFalse(result.CanSatisfy);
            Assert.IsEmpty(result.AccessibleChallenges);
            StringAssert.Contains(result.FailureReason, "not authorised to use managed challenges");

            // the denial has to be able to say which roles the principal actually holds
            client.Verify(c => c.GetSecurityPrincipalRoleStatus("sp-1", It.IsAny<AuthContext>()), Times.Once);
        }

        [TestMethod]
        [Description("A denial still reports when the principal's role assignments cannot be read")]
        public async Task PrincipalWithoutAuthorizingRole_IsDeniedWhenRoleAssignmentsCannotBeRead()
        {
            var client = CreateClient(allowUnscoped: false, new List<ItemTag>());

            client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(new ResourceAccessScope { HasAccess = false });

            client.Setup(c => c.GetSecurityPrincipalRoleStatus(It.IsAny<string>(), It.IsAny<AuthContext>()))
                .ThrowsAsync(new System.Net.Http.HttpRequestException("backend unavailable"));

            var service = new ManagedChallengeScopeService(client.Object, NullLogger<ManagedChallengeScopeService>.Instance);

            var result = await service.AuthorizeIdentifiersForPrincipal(
                "sp-1",
                ["app.finance.example.com"],
                null,
                StandardResourceActions.ManagedChallengeCleanup);

            Assert.IsFalse(result.IsAuthorized);
            StringAssert.Contains(result.FailureReason, "not authorised to use managed challenges");
        }
    }
}
