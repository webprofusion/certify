using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Hub;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for tag-based access control on Managed Challenges.
    /// Verifies that security principals with tag-scoped ManagedChallengeConsumer roles
    /// can only access challenges with matching tags.
    /// </summary>
    [TestClass]
    public class ManagedChallengeTagAccessTests
    {
        private CertifyManager _manager;
        private MemoryObjectStore _store;

        // Test tag categories
        private const string DepartmentCategory = "department";
        private const string ProjectCategory = "project";
        private const string EnvironmentCategory = "environment";

        // Test tag values
        private const string FinanceDept = "finance";
        private const string EngineeringDept = "engineering";
        private const string WebAppProject = "webapp";
        private const string ApiProject = "api";
        private const string ProductionEnv = "production";

        [TestInitialize]
        public async Task Setup()
        {
            _store = new MemoryObjectStore();
            _manager = new CertifyManager();

            // Use reflection to set the private _configStore field
            var field = typeof(CertifyManager)
                .GetField("_configStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(_manager, _store);

            // access control reads the same store, so a principal seeded below resolves for authorization checks
            var accessField = typeof(CertifyManager)
                .GetField("_accessControl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            accessField?.SetValue(_manager, new Certify.Core.Management.Access.AccessControl(null, _store));

            // Create tag categories
            await _manager.AddOrUpdateTagCategory(new TagCategory
            {
                CategoryKey = DepartmentCategory,
                DisplayName = "Department",
                Description = "Organizational department"
            });

            await _manager.AddOrUpdateTagCategory(new TagCategory
            {
                CategoryKey = ProjectCategory,
                DisplayName = "Project",
                Description = "Project or application"
            });

            await _manager.AddOrUpdateTagCategory(new TagCategory
            {
                CategoryKey = EnvironmentCategory,
                DisplayName = "Environment",
                Description = "Deployment environment"
            });
        }

        #region Helper Methods

        /// <summary>
        /// Creates a managed challenge for testing by adding directly to the store
        /// </summary>
        private async Task<ManagedChallenge> CreateManagedChallenge(string id, string domainMatch)
        {
            var challenge = new ManagedChallenge
            {
                Id = id,
                Title = $"Challenge for {domainMatch}",
                ChallengeConfig = new CertRequestChallengeConfig
                {
                    ChallengeType = "dns-01",
                    DomainMatch = domainMatch,
                    ChallengeProvider = "DNS01.API.TestProvider"
                }
            };

            // Add directly to store (Update requires item to exist first)
            await _store.Add<ManagedChallenge>(nameof(ManagedChallenge), challenge);
            return challenge;
        }

        /// <summary>
        /// The challenges accessible to a caller whose authorizing role carries the given tag scopes.
        ///
        /// These tests used to call GetManagedChallengesWithTagFilter, which took raw tag scopes and was reachable
        /// from nowhere in the product - so the tag matching semantics below were verified against an
        /// implementation that never ran. This goes through the path that does: a resolved access scope, filtered
        /// by the tags stored against each challenge.
        /// </summary>
        private Task<ICollection<ManagedChallenge>> AccessibleForTagScopes(
            ICollection<TagScope> tagScopes,
            bool requireAllTags = false,
            bool includeUntagged = false)
        {
            var isTagScoped = tagScopes?.Count > 0;

            return _manager.GetAccessibleManagedChallenges(new ManagedChallengeAccessScope
            {
                HasAccess = true,

                // a role carrying no tag scopes is not restricted by tags, and reaches every challenge
                IsUnrestricted = !isTagScoped,
                AllowUnscopedResources = includeUntagged,
                AuthorizingRoles =
                [
                    new AssignedRole
                    {
                        Id = "ar-test",
                        RoleId = StandardRoles.ManagedChallengeConsumer.Id,
                        SecurityPrincipalId = "sp-test",
                        ScopedTags = tagScopes?.ToList(),
                        RequireAllScopedTags = requireAllTags
                    }
                ]
            });
        }

        /// <summary>
        /// Adds a tag to a managed challenge
        /// </summary>
        private async Task TagChallenge(string challengeId, string categoryKey, string value)
        {
            await _manager.AddHubItemTags(new List<ItemTag>
            {
                new ItemTag(challengeId, TaggedItemTypes.ManagedChallenge, categoryKey, value)
            });
        }

        #endregion

        #region Tag scoped challenge access

        [TestMethod]
        [Description("Challenges without tags are excluded when filtering with tag scopes (unless includeUntagged=true)")]
        public async Task AccessibleChallenges_ExcludesUntaggedChallenges()
        {
            // Arrange: Create challenges - one tagged, one untagged
            await CreateManagedChallenge("challenge-1", "*.example.com");
            await CreateManagedChallenge("challenge-2", "*.test.com");

            // Tag only the first challenge
            await TagChallenge("challenge-1", DepartmentCategory, FinanceDept);

            var tagScopes = new List<TagScope>
            {
                new TagScope { CategoryKey = DepartmentCategory, Value = FinanceDept }
            };

            // Act: Filter with tag scope, excluding untagged
            var filteredChallenges = await AccessibleForTagScopes(
                tagScopes,
                requireAllTags: false,
                includeUntagged: false);

            // Assert: Only tagged challenge returned
            Assert.HasCount(1, filteredChallenges);
            Assert.AreEqual("challenge-1", filteredChallenges.First().Id);
        }

        [TestMethod]
        [Description("Untagged challenges are included when includeUntagged=true")]
        public async Task AccessibleChallenges_IncludesUntaggedWhenRequested()
        {
            // Arrange: Create challenges - one tagged, one untagged
            await CreateManagedChallenge("challenge-1", "*.example.com");
            await CreateManagedChallenge("challenge-2", "*.test.com");

            await TagChallenge("challenge-1", DepartmentCategory, FinanceDept);

            var tagScopes = new List<TagScope>
            {
                new TagScope { CategoryKey = DepartmentCategory, Value = FinanceDept }
            };

            // Act: Filter with includeUntagged=true
            var filteredChallenges = await AccessibleForTagScopes(
                tagScopes,
                requireAllTags: false,
                includeUntagged: true);

            // Assert: Both challenges returned
            Assert.HasCount(2, filteredChallenges);
        }

        [TestMethod]
        [Description("Returns all challenges when no tag scopes provided")]
        public async Task AccessibleChallenges_ReturnsAllWhenNoScopes()
        {
            // Arrange: Create multiple challenges with different tags
            await CreateManagedChallenge("challenge-1", "*.example.com");
            await CreateManagedChallenge("challenge-2", "*.test.com");
            await CreateManagedChallenge("challenge-3", "*.other.com");

            await TagChallenge("challenge-1", DepartmentCategory, FinanceDept);
            await TagChallenge("challenge-2", DepartmentCategory, EngineeringDept);
            // challenge-3 untagged

            // Act: No tag scopes (admin access)
            var allChallenges = await AccessibleForTagScopes(
                tagScopes: null,
                requireAllTags: false,
                includeUntagged: true);

            // Assert: All challenges returned
            Assert.HasCount(3, allChallenges);
        }

        [TestMethod]
        [Description("Filter by specific category:value matches only challenges with that exact tag")]
        public async Task AccessibleChallenges_FiltersByExactValue()
        {
            // Arrange: Create challenges with different department tags
            await CreateManagedChallenge("challenge-finance", "*.finance.example.com");
            await CreateManagedChallenge("challenge-engineering", "*.eng.example.com");
            await CreateManagedChallenge("challenge-hr", "*.hr.example.com");

            await TagChallenge("challenge-finance", DepartmentCategory, FinanceDept);
            await TagChallenge("challenge-engineering", DepartmentCategory, EngineeringDept);
            await TagChallenge("challenge-hr", DepartmentCategory, "hr");

            var tagScopes = new List<TagScope>
            {
                new TagScope { CategoryKey = DepartmentCategory, Value = FinanceDept }
            };

            // Act
            var filteredChallenges = await AccessibleForTagScopes(
                tagScopes,
                requireAllTags: false,
                includeUntagged: false);

            // Assert: Only finance challenge
            Assert.HasCount(1, filteredChallenges);
            Assert.AreEqual("challenge-finance", filteredChallenges.First().Id);
        }

        [TestMethod]
        [Description("Filter by category with null value matches any value in that category")]
        public async Task AccessibleChallenges_FiltersByCategoryWildcard()
        {
            // Arrange: Create challenges with different department values
            await CreateManagedChallenge("challenge-finance", "*.finance.example.com");
            await CreateManagedChallenge("challenge-engineering", "*.eng.example.com");
            await CreateManagedChallenge("challenge-untagged", "*.other.example.com");

            await TagChallenge("challenge-finance", DepartmentCategory, FinanceDept);
            await TagChallenge("challenge-engineering", DepartmentCategory, EngineeringDept);
            // challenge-untagged has no department tag

            var tagScopes = new List<TagScope>
            {
                new TagScope { CategoryKey = DepartmentCategory, Value = null } // Any department
            };

            // Act
            var filteredChallenges = await AccessibleForTagScopes(
                tagScopes,
                requireAllTags: false,
                includeUntagged: false);

            // Assert: Both tagged challenges (any department value)
            Assert.HasCount(2, filteredChallenges);
            var ids = filteredChallenges.Select(c => c.Id).ToList();
            Assert.IsTrue(ids.Contains("challenge-finance"));
            Assert.IsTrue(ids.Contains("challenge-engineering"));
        }

        [TestMethod]
        [Description("Multiple tag scopes with OR logic returns challenges matching ANY scope")]
        public async Task AccessibleChallenges_OrLogicMatchesAny()
        {
            // Arrange: Create challenges with different tags
            await CreateManagedChallenge("challenge-finance-webapp", "*.finance.example.com");
            await CreateManagedChallenge("challenge-eng-api", "*.api.example.com");
            await CreateManagedChallenge("challenge-hr", "*.hr.example.com");

            await TagChallenge("challenge-finance-webapp", DepartmentCategory, FinanceDept);
            await TagChallenge("challenge-finance-webapp", ProjectCategory, WebAppProject);
            await TagChallenge("challenge-eng-api", DepartmentCategory, EngineeringDept);
            await TagChallenge("challenge-eng-api", ProjectCategory, ApiProject);
            await TagChallenge("challenge-hr", DepartmentCategory, "hr");

            var tagScopes = new List<TagScope>
            {
                new TagScope { CategoryKey = DepartmentCategory, Value = FinanceDept },
                new TagScope { CategoryKey = ProjectCategory, Value = ApiProject }
            };

            // Act: OR logic (default)
            var filteredChallenges = await AccessibleForTagScopes(
                tagScopes,
                requireAllTags: false,
                includeUntagged: false);

            // Assert: Matches finance OR api project
            Assert.HasCount(2, filteredChallenges);
            var ids = filteredChallenges.Select(c => c.Id).ToList();
            Assert.IsTrue(ids.Contains("challenge-finance-webapp")); // Has dept:finance
            Assert.IsTrue(ids.Contains("challenge-eng-api")); // Has project:api
        }

        [TestMethod]
        [Description("Multiple tag scopes with AND logic requires challenges to match ALL scopes")]
        public async Task AccessibleChallenges_AndLogicMatchesAll()
        {
            // Arrange: Create challenges with various tag combinations
            await CreateManagedChallenge("challenge-both", "*.both.example.com");
            await CreateManagedChallenge("challenge-finance-only", "*.finance.example.com");
            await CreateManagedChallenge("challenge-prod-only", "*.prod.example.com");

            // challenge-both has both required tags
            await TagChallenge("challenge-both", DepartmentCategory, FinanceDept);
            await TagChallenge("challenge-both", EnvironmentCategory, ProductionEnv);

            // challenge-finance-only has only department tag
            await TagChallenge("challenge-finance-only", DepartmentCategory, FinanceDept);

            // challenge-prod-only has only environment tag
            await TagChallenge("challenge-prod-only", EnvironmentCategory, ProductionEnv);

            var tagScopes = new List<TagScope>
            {
                new TagScope { CategoryKey = DepartmentCategory, Value = FinanceDept },
                new TagScope { CategoryKey = EnvironmentCategory, Value = ProductionEnv }
            };

            // Act: AND logic
            var filteredChallenges = await AccessibleForTagScopes(
                tagScopes,
                requireAllTags: true,
                includeUntagged: false);

            // Assert: Only challenge with BOTH tags
            Assert.HasCount(1, filteredChallenges);
            Assert.AreEqual("challenge-both", filteredChallenges.First().Id);
        }

        #endregion

        #region Stored challenge tags

        // GetManagedChallengeSummaries used to be tested here. It built summaries in the core and nothing called
        // it: the hub API assembles its own from the challenges and the stored item tags. Its tag scope test
        // duplicated the filtering covered above; what remains unique to it is that a challenge's tags read back,
        // which is the part the hub API relies on.

        [TestMethod]
        [Description("Every tag stored against a challenge reads back, which is what the hub API builds summaries from")]
        public async Task StoredChallengeTags_ReadBackForEachChallenge()
        {
            await CreateManagedChallenge("challenge-1", "*.example.com");
            await TagChallenge("challenge-1", DepartmentCategory, FinanceDept);
            await TagChallenge("challenge-1", ProjectCategory, WebAppProject);

            var tags = await _manager.GetHubItemTags(TaggedItemTypes.ManagedChallenge, "challenge-1");

            Assert.HasCount(2, tags);

            var tagKeys = tags.Select(t => t.CategoryKey).ToList();
            Assert.IsTrue(tagKeys.Contains(DepartmentCategory));
            Assert.IsTrue(tagKeys.Contains(ProjectCategory));
        }

        #endregion

        #region Access Control Scenarios

        [TestMethod]
        [Description("Simulates a finance department consumer who can only access finance-tagged challenges")]
        public async Task AccessControl_FinanceDeptConsumer_OnlyAccessesFinanceChallenges()
        {
            // Arrange: Create challenges for different departments
            await CreateManagedChallenge("challenge-finance-1", "*.acme-finance.com");
            await CreateManagedChallenge("challenge-finance-2", "*.billing.acme.com");
            await CreateManagedChallenge("challenge-engineering", "*.dev.acme.com");
            await CreateManagedChallenge("challenge-shared", "*.acme.com"); // Untagged - shared

            await TagChallenge("challenge-finance-1", DepartmentCategory, FinanceDept);
            await TagChallenge("challenge-finance-2", DepartmentCategory, FinanceDept);
            await TagChallenge("challenge-engineering", DepartmentCategory, EngineeringDept);

            // Simulate: Finance consumer's API token has tag scope for dept:finance
            var financeConsumerScopes = new List<TagScope>
            {
                new TagScope { CategoryKey = DepartmentCategory, Value = FinanceDept }
            };

            // Act: Get challenges accessible to finance consumer
            var accessibleChallenges = await AccessibleForTagScopes(
                financeConsumerScopes,
                requireAllTags: false,
                includeUntagged: false); // Untagged NOT accessible to scoped consumers

            // Assert
            Assert.HasCount(2, accessibleChallenges);
            var ids = accessibleChallenges.Select(c => c.Id).ToList();
            Assert.IsTrue(ids.Contains("challenge-finance-1"));
            Assert.IsTrue(ids.Contains("challenge-finance-2"));
            Assert.IsFalse(ids.Contains("challenge-engineering"));
            Assert.IsFalse(ids.Contains("challenge-shared")); // Untagged - not accessible
        }

        [TestMethod]
        [Description("Simulates a production ops consumer who can only access production-tagged challenges")]
        public async Task AccessControl_ProductionOpsConsumer_OnlyAccessesProductionChallenges()
        {
            // Arrange: Create challenges for different environments
            await CreateManagedChallenge("challenge-prod", "*.prod.example.com");
            await CreateManagedChallenge("challenge-staging", "*.staging.example.com");
            await CreateManagedChallenge("challenge-dev", "*.dev.example.com");

            await TagChallenge("challenge-prod", EnvironmentCategory, ProductionEnv);
            await TagChallenge("challenge-staging", EnvironmentCategory, "staging");
            await TagChallenge("challenge-dev", EnvironmentCategory, "development");

            // Simulate: Prod ops consumer's API token has tag scope for env:production
            var prodOpsScopes = new List<TagScope>
            {
                new TagScope { CategoryKey = EnvironmentCategory, Value = ProductionEnv }
            };

            // Act
            var accessibleChallenges = await AccessibleForTagScopes(
                prodOpsScopes,
                requireAllTags: false,
                includeUntagged: false);

            // Assert
            Assert.HasCount(1, accessibleChallenges);
            Assert.AreEqual("challenge-prod", accessibleChallenges.First().Id);
        }

        [TestMethod]
        [Description("Simulates an admin without tag restrictions who can access all challenges")]
        public async Task AccessControl_AdminWithoutTagScope_AccessesAllChallenges()
        {
            // Arrange: Create challenges with various tags
            await CreateManagedChallenge("challenge-1", "*.one.example.com");
            await CreateManagedChallenge("challenge-2", "*.two.example.com");
            await CreateManagedChallenge("challenge-3", "*.three.example.com");

            await TagChallenge("challenge-1", DepartmentCategory, FinanceDept);
            await TagChallenge("challenge-2", DepartmentCategory, EngineeringDept);
            // challenge-3 intentionally untagged

            // Act: Admin has no tag scope restrictions (null)
            var accessibleChallenges = await AccessibleForTagScopes(
                tagScopes: null,
                requireAllTags: false,
                includeUntagged: true);

            // Assert: Admin sees all challenges
            Assert.HasCount(3, accessibleChallenges);
        }

        [TestMethod]
        [Description("Consumer with multiple tag scopes can access challenges matching any scope")]
        public async Task AccessControl_MultiScopeConsumer_AccessesMatchingChallenges()
        {
            // Arrange: Create challenges
            await CreateManagedChallenge("challenge-finance-prod", "*.finance.prod.example.com");
            await CreateManagedChallenge("challenge-webapp-staging", "*.webapp.staging.example.com");
            await CreateManagedChallenge("challenge-other", "*.other.example.com");

            await TagChallenge("challenge-finance-prod", DepartmentCategory, FinanceDept);
            await TagChallenge("challenge-finance-prod", EnvironmentCategory, ProductionEnv);
            await TagChallenge("challenge-webapp-staging", ProjectCategory, WebAppProject);
            await TagChallenge("challenge-webapp-staging", EnvironmentCategory, "staging");
            await TagChallenge("challenge-other", DepartmentCategory, "other");

            // Consumer has scopes for finance OR webapp project
            var consumerScopes = new List<TagScope>
            {
                new TagScope { CategoryKey = DepartmentCategory, Value = FinanceDept },
                new TagScope { CategoryKey = ProjectCategory, Value = WebAppProject }
            };

            // Act
            var accessibleChallenges = await AccessibleForTagScopes(
                consumerScopes,
                requireAllTags: false,
                includeUntagged: false);

            // Assert: Both matching challenges accessible
            Assert.HasCount(2, accessibleChallenges);
            var ids = accessibleChallenges.Select(c => c.Id).ToList();
            Assert.IsTrue(ids.Contains("challenge-finance-prod"));
            Assert.IsTrue(ids.Contains("challenge-webapp-staging"));
            Assert.IsFalse(ids.Contains("challenge-other"));
        }

        #endregion

        #region ManagedChallengeAccess helper / FindBestMatch scope

        [TestMethod]
        [Description("Scoped Managed ACME role only sees matching tagged challenges")]
        public void ManagedChallengeAccess_ScopedRole_FiltersChallenges()
        {
            var scopedRole = new AssignedRole
            {
                Id = "ar-1",
                RoleId = StandardRoles.ManagedAcmeConsumer.Id,
                SecurityPrincipalId = "sp-1",
                ScopedTags =
                [
                    new TagScope { CategoryKey = DepartmentCategory, Value = FinanceDept }
                ]
            };

            var scope = new ManagedChallengeAccessScope
            {
                HasAccess = true,
                IsUnrestricted = false,
                AuthorizingRoles = [scopedRole],
                AllowUnscopedResources = false
            };

            Assert.IsTrue(scope.HasAccess);
            Assert.IsFalse(scope.IsUnrestricted);
            Assert.IsTrue(scope.RequiresTagFiltering);

            var challenges = new List<ManagedChallenge>
            {
                new() { Id = "c-finance", ChallengeConfig = new CertRequestChallengeConfig { DomainMatch = "*.finance.example.com", ChallengeType = "dns-01" } },
                new() { Id = "c-eng", ChallengeConfig = new CertRequestChallengeConfig { DomainMatch = "*.eng.example.com", ChallengeType = "dns-01" } },
                new() { Id = "c-unscoped", ChallengeConfig = new CertRequestChallengeConfig { DomainMatch = "*.example.com", ChallengeType = "dns-01" } }
            };

            var tags = new Dictionary<string, List<ItemTag>>
            {
                ["c-finance"] = [new ItemTag("c-finance", TaggedItemTypes.ManagedChallenge, DepartmentCategory, FinanceDept)],
                ["c-eng"] = [new ItemTag("c-eng", TaggedItemTypes.ManagedChallenge, DepartmentCategory, EngineeringDept)]
            };

            var accessible = ManagedChallengeAccess.FilterChallenges(challenges, tags, scope).ToList();
            Assert.HasCount(1, accessible);
            Assert.AreEqual("c-finance", accessible[0].Id);

            // Pref allows unscoped/untagged resources
            scope.AllowUnscopedResources = true;
            accessible = ManagedChallengeAccess.FilterChallenges(challenges, tags, scope).ToList();
            Assert.HasCount(2, accessible);
            Assert.IsTrue(accessible.Any(c => c.Id == "c-unscoped"));
        }

        [TestMethod]
        [Description("FindBestMatch only considers accessible challenges when scope is applied")]
        public void ManagedChallengeAccess_FindBestMatch_HonoursAccessibleSet()
        {
            var challenges = new List<ManagedChallenge>
            {
                new() { Id = "global", ChallengeConfig = new CertRequestChallengeConfig { DomainMatch = "", ChallengeType = "dns-01" } },
                new() { Id = "specific", ChallengeConfig = new CertRequestChallengeConfig { DomainMatch = "*.finance.example.com", ChallengeType = "dns-01" } }
            };

            // Without filtering, specific domain match wins
            var best = ManagedChallengeAccess.FindBestMatch(
                new ManagedChallengeRequest { Identifier = "app.finance.example.com", ChallengeType = "dns-01" },
                challenges);
            Assert.AreEqual("specific", best?.Id);

            // When only global is accessible, that is selected
            best = ManagedChallengeAccess.FindBestMatch(
                new ManagedChallengeRequest { Identifier = "app.finance.example.com", ChallengeType = "dns-01" },
                [challenges[0]]);
            Assert.AreEqual("global", best?.Id);
        }

        [TestMethod]
        [Description("A wildcard domain match rule matches a first level subdomain of a multi-level domain")]
        public void ManagedChallengeAccess_FindBestMatch_WildcardMatchesFirstLevelSubdomain()
        {
            var challenge = new ManagedChallenge
            {
                Id = "c-dev",
                ChallengeConfig = new CertRequestChallengeConfig { DomainMatch = "*.dev.projectbids.co.uk", ChallengeType = "dns-01" }
            };

            var best = ManagedChallengeAccess.FindBestMatch(
                new ManagedChallengeRequest { Identifier = "acme-01.dev.projectbids.co.uk", ChallengeType = "dns-01" },
                [challenge]);

            Assert.AreEqual("c-dev", best?.Id, "*.dev.projectbids.co.uk should match acme-01.dev.projectbids.co.uk");

            Assert.IsTrue(
                ManagedChallengeAccess.CanSatisfyIdentifiers(["acme-01.dev.projectbids.co.uk"], [challenge], out var unsatisfied),
                "identifier should be satisfiable, unsatisfied: " + string.Join(", ", unsatisfied));
        }

        [TestMethod]
        [Description("Tag scope matching ignores casing differences between stored tags and role tag scopes")]
        public void ManagedChallengeAccess_TagScopeMatch_IsCaseInsensitive()
        {
            // category keys are normalised to lowercase on save, but a role tag scope may have been
            // stored with the original casing (eg "Environment"/"Development")
            var challenge = new ManagedChallenge
            {
                Id = "c-dev",
                ChallengeConfig = new CertRequestChallengeConfig { DomainMatch = "*.dev.projectbids.co.uk", ChallengeType = "dns-01" }
            };

            var scopedRole = new AssignedRole
            {
                Id = "ar-dev",
                RoleId = StandardRoles.ManagedAcmeConsumer.Id,
                SecurityPrincipalId = "sp-dev",
                ScopedTags = [new TagScope { CategoryKey = "Environment", Value = "Development" }]
            };

            var scope = new ManagedChallengeAccessScope
            {
                HasAccess = true,
                IsUnrestricted = false,
                AuthorizingRoles = [scopedRole],
                AllowUnscopedResources = false
            };

            var tags = new Dictionary<string, List<ItemTag>>
            {
                ["c-dev"] = [new ItemTag("c-dev", TaggedItemTypes.ManagedChallenge, "environment", "development")]
            };

            var accessible = ManagedChallengeAccess.FilterChallenges([challenge], tags, scope).ToList();

            Assert.HasCount(1, accessible, "challenge tagged environment:development should be accessible to a role scoped to Environment:Development");

            Assert.IsTrue(
                ManagedChallengeAccess.CanSatisfyIdentifiers(["acme-01.dev.projectbids.co.uk"], accessible, out _),
                "identifier should be satisfiable once the challenge is accessible");
        }

        [TestMethod]
        [Description("Tag scope preview resource types are derived from what the role actually grants")]
        public void Policies_GetTaggedItemTypesForRoles_ReflectsRoleGrants()
        {
            // a Managed ACME Consumer only grants managed challenge access, not managed certificate listing
            var acmeConsumerTypes = Policies.GetTaggedItemTypesForRoles([StandardRoles.ManagedAcmeConsumer.Id]);

            CollectionAssert.Contains(acmeConsumerTypes, TaggedItemTypes.ManagedChallenge, "Managed ACME Consumer should scope to managed challenges");
            CollectionAssert.DoesNotContain(acmeConsumerTypes, TaggedItemTypes.ManagedCertificate, "Managed ACME Consumer should not scope to managed certificates");

            // a certificate manager should conversely include managed certificates
            var certManagerTypes = Policies.GetTaggedItemTypesForRoles([StandardRoles.CertificateManager.Id]);

            CollectionAssert.Contains(certManagerTypes, TaggedItemTypes.ManagedCertificate, "Certificate Manager should scope to managed certificates");

            // unknown roles contribute nothing
            Assert.IsEmpty(Policies.GetTaggedItemTypesForRoles(["not_a_real_role"]), "unknown roles should not contribute resource types");
        }

        [TestMethod]
        [Description("Unscoped authorizing role grants unrestricted challenge access")]
        public void ManagedChallengeAccess_UnscopedRole_IsUnrestricted()
        {
            var unscopedRole = new AssignedRole
            {
                Id = "ar-2",
                RoleId = StandardRoles.ManagedAcmeConsumer.Id,
                SecurityPrincipalId = "sp-2"
            };

            var scope = new ManagedChallengeAccessScope
            {
                HasAccess = true,
                IsUnrestricted = true,
                AuthorizingRoles = [unscopedRole],
                AllowUnscopedResources = false
            };

            Assert.IsTrue(scope.HasAccess);
            Assert.IsTrue(scope.IsUnrestricted);
            Assert.IsFalse(scope.RequiresTagFiltering);
        }

        [TestMethod]
        [Description("CanSatisfyIdentifiers reports domains with no matching accessible challenge")]
        public void ManagedChallengeAccess_CanSatisfyIdentifiers_ReportsMissing()
        {
            var challenges = new List<ManagedChallenge>
            {
                new() { Id = "finance", ChallengeConfig = new CertRequestChallengeConfig { DomainMatch = "*.finance.example.com", ChallengeType = "dns-01" } }
            };

            var ok = ManagedChallengeAccess.CanSatisfyIdentifiers(
                ["app.finance.example.com", "other.example.com"],
                challenges,
                out var unsatisfied);

            Assert.IsFalse(ok);
            Assert.HasCount(1, unsatisfied);
            Assert.AreEqual("other.example.com", unsatisfied[0]);
        }

        [TestMethod]
        [Description("Scope with no access resolves to no accessible challenges")]
        public async Task GetAccessibleManagedChallenges_NoAccessScope_ReturnsEmpty()
        {
            await CreateManagedChallenge("challenge-finance", "*.finance.example.com");
            await TagChallenge("challenge-finance", DepartmentCategory, FinanceDept);

            var accessible = await _manager.GetAccessibleManagedChallenges(
                new ManagedChallengeAccessScope { HasAccess = false });

            Assert.IsEmpty(accessible);
        }

        [TestMethod]
        [Description("Tag-scoped scope resolves stored challenge tags so matching tagged challenges remain accessible")]
        public async Task GetAccessibleManagedChallenges_ScopedRole_ResolvesStoredTags()
        {
            await CreateManagedChallenge("challenge-finance", "*.finance.example.com");
            await CreateManagedChallenge("challenge-eng", "*.eng.example.com");
            await CreateManagedChallenge("challenge-untagged", "*.example.com");

            await TagChallenge("challenge-finance", DepartmentCategory, FinanceDept);
            await TagChallenge("challenge-eng", DepartmentCategory, EngineeringDept);

            var scope = new ManagedChallengeAccessScope
            {
                HasAccess = true,
                IsUnrestricted = false,
                AllowUnscopedResources = false,
                AuthorizingRoles =
                [
                    new AssignedRole
                    {
                        Id = "ar-finance",
                        RoleId = StandardRoles.ManagedAcmeConsumer.Id,
                        SecurityPrincipalId = "sp-finance",
                        ScopedTags = [new TagScope { CategoryKey = DepartmentCategory, Value = FinanceDept }]
                    }
                ]
            };

            var accessible = await _manager.GetAccessibleManagedChallenges(scope);

            Assert.HasCount(1, accessible);
            Assert.AreEqual("challenge-finance", accessible.First().Id);
        }

        [TestMethod]
        [Description("Unrestricted scope returns all challenges regardless of tags")]
        public async Task GetAccessibleManagedChallenges_UnrestrictedScope_ReturnsAll()
        {
            await CreateManagedChallenge("challenge-finance", "*.finance.example.com");
            await CreateManagedChallenge("challenge-untagged", "*.example.com");
            await TagChallenge("challenge-finance", DepartmentCategory, FinanceDept);

            var accessible = await _manager.GetAccessibleManagedChallenges(
                new ManagedChallengeAccessScope { HasAccess = true, IsUnrestricted = true });

            Assert.HasCount(2, accessible);
        }

        #endregion

                #region Edge Cases

                [TestMethod]
                [Description("Empty tag scopes list behaves same as null (no filtering)")]
                public async Task AccessibleChallenges_EmptyScopesReturnsAll()
                {
                    // Arrange
                    await CreateManagedChallenge("challenge-1", "*.example.com");
            await CreateManagedChallenge("challenge-2", "*.test.com");

            await TagChallenge("challenge-1", DepartmentCategory, FinanceDept);

            // Act: Empty scopes list
            var challenges = await AccessibleForTagScopes(
                new List<TagScope>(),
                requireAllTags: false,
                includeUntagged: true);

            // Assert: All challenges returned (no filtering)
            Assert.HasCount(2, challenges);
        }

        [TestMethod]
        [Description("Challenge with multiple tags in same category is found by any matching value")]
        public async Task AccessibleChallenges_MultipleTagsInSameCategory()
        {
            // Arrange: Challenge tagged with multiple projects
            await CreateManagedChallenge("challenge-multi", "*.multi.example.com");
            await TagChallenge("challenge-multi", ProjectCategory, WebAppProject);
            await TagChallenge("challenge-multi", ProjectCategory, ApiProject);

            // Consumer scope for webapp only
            var scopes = new List<TagScope>
            {
                new TagScope { CategoryKey = ProjectCategory, Value = WebAppProject }
            };

            // Act
            var challenges = await AccessibleForTagScopes(
                scopes,
                requireAllTags: false,
                includeUntagged: false);

            // Assert: Challenge found (has webapp tag)
            Assert.HasCount(1, challenges);
            Assert.AreEqual("challenge-multi", challenges.First().Id);
        }

        [TestMethod]
        [Description("No challenges match when scope value doesn't exist")]
        public async Task AccessibleChallenges_NonexistentValueReturnsEmpty()
        {
            // Arrange
            await CreateManagedChallenge("challenge-1", "*.example.com");
            await TagChallenge("challenge-1", DepartmentCategory, FinanceDept);

            // Scope for a value that doesn't exist
            var scopes = new List<TagScope>
            {
                new TagScope { CategoryKey = DepartmentCategory, Value = "nonexistent" }
            };

            // Act
            var challenges = await AccessibleForTagScopes(
                scopes,
                requireAllTags: false,
                includeUntagged: false);

            // Assert: No challenges found
            Assert.IsEmpty(challenges);
        }

        #endregion

        #region request origin decides the action fulfillment checks

        /// <summary>
        /// Which action fulfillment checks used to be inferred from whether the request carried AuthKey/AuthSecret.
        /// A caller presenting the same credentials as X-Client-ID/X-Client-Secret headers leaves those empty, so a
        /// managed challenge consumer authenticating by header was checked against the managed ACME order action and
        /// denied against a role it was never meant to hold.
        /// </summary>
        [TestMethod]
        public void ManagedChallengeApiRequestChecksTheManagedChallengeActionHoweverCredentialsWerePresented()
        {
            // the caller may have presented credentials as headers or in the body; neither decides the action
            var headerAuthenticated = new ManagedChallengeCaller { SecurityPrincipalId = "sp-consumer" };

            Assert.AreEqual(
                ManagedChallengeRequestOrigins.ManagedChallengeApi,
                headerAuthenticated.Origin,
                "an API request is the default origin, so a request which arrives without one is checked against the more restrictive action");

            Assert.AreEqual(
                StandardResourceActions.ManagedChallengeRequest,
                ManagedChallengeRequestOrigins.GetRequiredResourceAction(headerAuthenticated.Origin, StandardResourceActions.ManagedChallengeRequest));

            Assert.AreEqual(
                StandardResourceActions.ManagedChallengeCleanup,
                ManagedChallengeRequestOrigins.GetRequiredResourceAction(headerAuthenticated.Origin, StandardResourceActions.ManagedChallengeCleanup));
        }

        /// <summary>
        /// Managed ACME authorizes the order as a whole, so every challenge performed and cleaned up for it is
        /// covered by that one action rather than the per-request managed challenge actions.
        /// </summary>
        [TestMethod]
        public void ManagedAcmeFulfillmentChecksTheOrderAction()
        {
            Assert.AreEqual(
                StandardResourceActions.ManagedAcmePerformOrder,
                ManagedChallengeRequestOrigins.GetRequiredResourceAction(ManagedChallengeRequestOrigins.ManagedAcme, StandardResourceActions.ManagedChallengeRequest));

            Assert.AreEqual(
                StandardResourceActions.ManagedAcmePerformOrder,
                ManagedChallengeRequestOrigins.GetRequiredResourceAction(ManagedChallengeRequestOrigins.ManagedAcme, StandardResourceActions.ManagedChallengeCleanup));
        }

        #endregion

        #region AuthorizeManagedChallengeIdentifiers

        // The single managed challenge authorization decision, for every caller. These cover what the hub API's
        // own scope service used to answer separately, plus the domain restrictions which only it enforced.

        /// <summary>
        /// Seed a principal holding one role which grants the action, optionally tag scoped and/or restricted to
        /// specific domains, so an authorization check against it resolves through the real access control chain.
        /// </summary>
        private async Task SeedPrincipalWithRole(
            string principalId,
            string actionId,
            List<TagScope> scopedTags = null,
            string domainRestriction = null)
        {
            const string policyId = "test_managedchallenge_policy";
            const string roleId = "test_managedchallenge_role";

            await _store.Add<SecurityPrincipal>(nameof(SecurityPrincipal), new SecurityPrincipal
            {
                Id = principalId,
                Username = principalId,
                PrincipalType = SecurityPrincipalType.Application
            });

            await _store.Add<ResourcePolicy>(nameof(ResourcePolicy), new ResourcePolicy
            {
                Id = policyId,
                Title = "Test managed challenge policy",
                SecurityPermissionType = SecurityPermissionType.ALLOW,
                ResourceActions = [actionId]
            });

            await _store.Add<Role>(nameof(Role), new Role(roleId, "Test managed challenge role", "", policies: [policyId]));

            await _store.Add<AssignedRole>(nameof(AssignedRole), new AssignedRole
            {
                Id = $"ar-{principalId}",
                RoleId = roleId,
                SecurityPrincipalId = principalId,
                ScopedTags = scopedTags,
                IncludedResources = domainRestriction == null
                    ? []
                    : [new Resource { Id = $"res-{principalId}", ResourceType = ResourceTypes.Domain, Identifier = domainRestriction }]
            });
        }

        private Task<Models.Config.ActionResult> Authorize(
            string principalId,
            string identifier,
            string actionId = StandardResourceActions.ManagedChallengeRequest,
            bool requireSatisfiableChallenge = false)
        {
            return _manager.AuthorizeManagedChallengeIdentifiers(new ManagedChallengeAuthorizationCheck
            {
                SecurityPrincipalId = principalId,
                Identifiers = [identifier],
                RequiredActionId = actionId,
                RequireSatisfiableChallenge = requireSatisfiableChallenge
            });
        }

        [TestMethod]
        [Description("A principal whose roles do not grant the action is denied")]
        public async Task AuthorizeIdentifiers_PrincipalWithoutAuthorizingRole_IsDenied()
        {
            await CreateManagedChallenge("challenge-any", "*.example.com");

            var result = await Authorize("sp-unknown", "www.example.com");

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.Message, "not authorised to use managed challenges");
        }

        [TestMethod]
        [Description("A tag scoped principal can act on an identifier a matching tagged challenge answers for")]
        public async Task AuthorizeIdentifiers_TagScopedPrincipal_WithMatchingChallengeTag_IsAuthorized()
        {
            await CreateManagedChallenge("challenge-finance", "*.finance.example.com");
            await TagChallenge("challenge-finance", DepartmentCategory, FinanceDept);

            await SeedPrincipalWithRole(
                "sp-finance",
                StandardResourceActions.ManagedChallengeRequest,
                scopedTags: [new TagScope { CategoryKey = DepartmentCategory, Value = FinanceDept }]);

            var result = await Authorize("sp-finance", "app.finance.example.com");

            Assert.IsTrue(result.IsSuccess, result.Message);
        }

        [TestMethod]
        [Description("A tag scoped principal is denied when no challenge carries a matching tag")]
        public async Task AuthorizeIdentifiers_TagScopedPrincipal_WithoutMatchingChallengeTag_IsDenied()
        {
            await CreateManagedChallenge("challenge-eng", "*.finance.example.com");
            await TagChallenge("challenge-eng", DepartmentCategory, EngineeringDept);

            await SeedPrincipalWithRole(
                "sp-finance",
                StandardResourceActions.ManagedChallengeRequest,
                scopedTags: [new TagScope { CategoryKey = DepartmentCategory, Value = FinanceDept }]);

            var result = await Authorize("sp-finance", "app.finance.example.com");

            Assert.IsFalse(result.IsSuccess);
        }

        /// <summary>
        /// Domain restrictions are Domain Match rules held as domain-typed IncludedResources on the authorizing
        /// roles. They used to be enforced only in the hub API, so any other caller reaching fulfillment was not
        /// subject to them at all.
        /// </summary>
        [TestMethod]
        [Description("An identifier outside the domain restrictions on the authorizing role is denied")]
        public async Task AuthorizeIdentifiers_DomainRestrictedPrincipal_DeniesIdentifierOutsideTheRules()
        {
            await CreateManagedChallenge("challenge-any", "*.example.com");

            await SeedPrincipalWithRole(
                "sp-restricted",
                StandardResourceActions.ManagedChallengeRequest,
                domainRestriction: "*.permitted.example.com");

            var permitted = await Authorize("sp-restricted", "www.permitted.example.com");
            Assert.IsTrue(permitted.IsSuccess, permitted.Message);

            var denied = await Authorize("sp-restricted", "www.other.example.com");
            Assert.IsFalse(denied.IsSuccess);
            StringAssert.Contains(denied.Message, "not permitted by the domain restrictions");
        }

        [TestMethod]
        [Description("An unrestricted principal is authorized without a matching challenge unless one is required")]
        public async Task AuthorizeIdentifiers_UnrestrictedPrincipal_OnlyNeedsAMatchWhenRequired()
        {
            await CreateManagedChallenge("challenge-other", "*.other.example.com");

            await SeedPrincipalWithRole("sp-unrestricted", StandardResourceActions.ManagedChallengeRequest);

            // fulfillment reports a missing challenge itself, so a direct call does not require one up front
            var permitted = await Authorize("sp-unrestricted", "www.example.com");
            Assert.IsTrue(permitted.IsSuccess, permitted.Message);

            // an ACME order which no challenge can answer for would only fail later, so that caller requires one
            var denied = await Authorize("sp-unrestricted", "www.example.com", requireSatisfiableChallenge: true);
            Assert.IsFalse(denied.IsSuccess);
            StringAssert.Contains(denied.Message, "No accessible managed challenge matches identifier");
        }

        [TestMethod]
        [Description("Managed ACME order fulfillment is checked against the order action, not the per-request one")]
        public async Task AuthorizeIdentifiers_ChecksTheActionItWasAskedFor()
        {
            await CreateManagedChallenge("challenge-any", "*.example.com");

            await SeedPrincipalWithRole("sp-acme", StandardResourceActions.ManagedAcmePerformOrder);

            var asOrder = await Authorize("sp-acme", "www.example.com", StandardResourceActions.ManagedAcmePerformOrder);
            Assert.IsTrue(asOrder.IsSuccess, asOrder.Message);

            // the same principal holds no role granting the per-request action
            var asRequest = await Authorize("sp-acme", "www.example.com", StandardResourceActions.ManagedChallengeRequest);
            Assert.IsFalse(asRequest.IsSuccess);
        }

        /// <summary>
        /// The credentials a caller presents authenticate the request and have no purpose beyond that. They used to
        /// live on the request itself, which meant a stored operation held a live API credential for its whole
        /// lifetime and the status endpoint had to build a redacted copy before returning one.
        /// </summary>
        [TestMethod]
        [Description("A stored operation keeps the identity it was authorized as, and none of the credentials")]
        public async Task BeginManagedChallengeRequest_KeepsTheIdentityAndNotTheCredentials()
        {
            var authorized = new AuthorizedManagedChallengeRequest
            {
                Request = new ManagedChallengeRequest
                {
                    ChallengeType = "dns-01",
                    Identifier = "www.example.com",
                    AuthKey = "client-id",
                    AuthSecret = "client-secret"
                },
                Caller = new ManagedChallengeCaller { SecurityPrincipalId = "sp-1", Origin = ManagedChallengeRequestOrigins.ManagedChallengeApi }
            };

            var operation = await _manager.BeginManagedChallengeRequest(authorized);
            var stored = await _manager.GetManagedChallengeOperation(operation.Id);

            Assert.IsNotNull(stored);
            Assert.IsEmpty(stored!.Request.AuthKey, "a stored operation must not retain the credentials it was created with");
            Assert.IsEmpty(stored.Request.AuthSecret, "a stored operation must not retain the credentials it was created with");

            Assert.AreEqual("sp-1", stored.Caller?.SecurityPrincipalId, "the identity it was authorized as is what a later status query is checked against");
            Assert.AreEqual("www.example.com", stored.Request.Identifier);
        }

        [TestMethod]
        [Description("A check with no security principal is denied rather than treated as unscoped")]
        public async Task AuthorizeIdentifiers_WithoutAPrincipal_IsDenied()
        {
            var result = await _manager.AuthorizeManagedChallengeIdentifiers(new ManagedChallengeAuthorizationCheck
            {
                Identifiers = ["www.example.com"]
            });

            Assert.IsFalse(result.IsSuccess);
        }

        #endregion
    }
}
