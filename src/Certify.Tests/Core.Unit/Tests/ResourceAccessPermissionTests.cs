using System.Collections.Generic;
using Certify.Models.Hub;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// How a resolved access scope decides whether a concrete resource is reachable: each authorizing role assignment
    /// is evaluated as a unit, with its own tag scope, its own RequireAllScopedTags and its own domain restrictions,
    /// while domain restrictions remain a cap across the principal.
    /// </summary>
    [TestClass]
    public class ResourceAccessPermissionTests
    {
        private static TagSummary Tag(string category, string value) => new() { CategoryKey = category, Value = value };

        private static AssignedRole Role(string id, IEnumerable<TagScope>? tags = null, bool requireAll = false, params string[] domains)
        {
            var role = new AssignedRole
            {
                Id = id,
                RoleId = StandardRoles.CertificateConsumer.Id,
                ScopedTags = tags == null ? null : [.. tags],
                RequireAllScopedTags = requireAll,
                IncludedResources = []
            };

            foreach (var domain in domains)
            {
                role.IncludedResources.Add(new Resource { ResourceType = ResourceTypes.Domain, Identifier = domain });
            }

            return role;
        }

        private static ResourceAccessScope Scope(params AssignedRole[] roles) => new() { HasAccess = true, AuthorizingRoles = [.. roles] };

        private static TagScope Environment(string value) => new() { CategoryKey = "environment", Value = value };

        [TestMethod]
        [Description("One assignment's tag scope is not combined with another assignment's domains")]
        public void TagsAndDomainsMustBePermittedByTheSameAssignment()
        {
            var scope = Scope(
                Role("a", [Environment("production")], false, "*.a.example.com"),
                Role("b", [Environment("development")], false, "*.b.example.com"));

            Assert.IsTrue(ResourceAccess.IsResourcePermitted(scope, [Tag("environment", "production")], ["www.a.example.com"]));
            Assert.IsFalse(ResourceAccess.IsResourcePermitted(scope, [Tag("environment", "production")], ["www.b.example.com"]));
        }

        [TestMethod]
        [Description("An assignment requiring all of its tags reaches only resources carrying every one of them")]
        public void RequireAllScopedTagsIsHonoured()
        {
            var scope = Scope(Role("a", [Environment("production"), new TagScope { CategoryKey = "team", Value = "finance" }], requireAll: true));

            Assert.IsTrue(ResourceAccess.IsResourcePermitted(scope, [Tag("environment", "production"), Tag("team", "finance")], identifiers: null));
            Assert.IsFalse(ResourceAccess.IsResourcePermitted(scope, [Tag("environment", "production")], identifiers: null));
            Assert.IsFalse(ResourceAccess.IsResourcePermitted(scope, [Tag("team", "finance")], identifiers: null));
        }

        [TestMethod]
        [Description("A domain restriction on one assignment still caps an assignment which carries none of its own")]
        public void DomainRestrictionsRemainAPrincipalWideCap()
        {
            var scope = Scope(Role("unrestricted"), Role("finance", null, false, "*.finance.example.com"));

            Assert.IsTrue(ResourceAccess.IsResourcePermitted(scope, resourceTags: null, ["app.finance.example.com"]));
            Assert.IsFalse(ResourceAccess.IsResourcePermitted(scope, resourceTags: null, ["app.sales.example.com"]));
            Assert.IsFalse(ResourceAccess.IsScopeUnrestricted(scope));
        }

        [TestMethod]
        [Description("A tag scoped assignment does not reach an untagged resource, and an action level check is not narrowed by tags")]
        public void UntaggedResourcesAndActionLevelChecks()
        {
            var scope = Scope(Role("a", [Environment("production")]));

            Assert.IsFalse(ResourceAccess.IsResourcePermitted(scope, [], identifiers: null));
            Assert.IsTrue(ResourceAccess.IsResourcePermitted(scope, resourceTags: null, identifiers: null));
        }

        [TestMethod]
        [Description("A scope is unrestricted when an assignment carries no tag scope and nothing caps the principal")]
        public void UnrestrictedScope()
        {
            Assert.IsTrue(ResourceAccess.IsScopeUnrestricted(Scope(Role("a"), Role("b", [Environment("production")]))));
            Assert.IsFalse(ResourceAccess.IsScopeUnrestricted(Scope(Role("a", [Environment("production")]))));
            Assert.IsFalse(ResourceAccess.IsScopeUnrestricted(new ResourceAccessScope { HasAccess = false }));
        }

        [TestMethod]
        [Description("An instance is reached through its own tags or through an item it holds which the same assignment permits")]
        public void InstancesAreReachedThroughTheirTagsOrTheirItems()
        {
            var tagScoped = Scope(Role("a", [Environment("production")]));

            Assert.IsTrue(ResourceAccess.IsInstancePermitted(tagScoped, [Tag("environment", "production")], []));
            Assert.IsTrue(ResourceAccess.IsInstancePermitted(tagScoped, [], [([Tag("environment", "production")], ["www.example.com"])]));
            Assert.IsFalse(ResourceAccess.IsInstancePermitted(tagScoped, [Tag("environment", "development")], [([Tag("environment", "development")], ["www.example.com"])]));

            var domainOnly = Scope(Role("a", null, false, "*.example.com"));

            Assert.IsTrue(ResourceAccess.IsInstancePermitted(domainOnly, [], [([], ["www.example.com"])]));
            Assert.IsFalse(ResourceAccess.IsInstancePermitted(domainOnly, [], [([], ["www.example.org"])]));
            Assert.IsFalse(ResourceAccess.IsInstancePermitted(domainOnly, [], []), "an instance has no identifiers of its own to show it is within the domains");

            Assert.IsTrue(ResourceAccess.IsInstancePermitted(Scope(Role("a")), [], []));
        }

        [TestMethod]
        [Description("A dns-01 response record name is accepted only for the identifier's own _acme-challenge record")]
        public void ChallengeRecordNameMustBeDerivedFromTheIdentifier()
        {
            Assert.IsTrue(ManagedChallengeAccess.IsResponseKeyForIdentifier("app.example.com", "_acme-challenge.app.example.com"));
            Assert.IsTrue(ManagedChallengeAccess.IsResponseKeyForIdentifier("App.Example.com", "_ACME-challenge.app.example.com."));
            Assert.IsTrue(ManagedChallengeAccess.IsResponseKeyForIdentifier("*.example.com", "_acme-challenge.example.com"));
            Assert.IsTrue(ManagedChallengeAccess.IsResponseKeyForIdentifier("bücher.example.com", "_acme-challenge.xn--bcher-kva.example.com"));

            Assert.IsFalse(ManagedChallengeAccess.IsResponseKeyForIdentifier("app.example.com", "_acme-challenge.www.example.com"));
            Assert.IsFalse(ManagedChallengeAccess.IsResponseKeyForIdentifier("app.example.com", "example.com"));
            Assert.IsFalse(ManagedChallengeAccess.IsResponseKeyForIdentifier("app.example.com", "_acme-challenge.app.example.com.evil.net"));
            Assert.IsFalse(ManagedChallengeAccess.IsResponseKeyForIdentifier("app.example.com", ""));
            Assert.IsFalse(ManagedChallengeAccess.IsResponseKeyForIdentifier("not a host", "_acme-challenge.not a host"));
            Assert.IsNull(ManagedChallengeAccess.GetDnsChallengeRecordName(null));
        }
    }
}
