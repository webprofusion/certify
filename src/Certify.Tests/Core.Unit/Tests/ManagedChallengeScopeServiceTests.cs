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
    }
}
