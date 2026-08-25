using System.Collections.Generic;
using System.Linq;
using Certify.Models.Hub;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// Domain resource scope (a role restricted to specific domains). Domain resource identifiers are
    /// Domain Match rules and must evaluate identically to the shared DomainMatchRules implementation.
    /// </summary>
    [TestClass]
    public class ResourceScopeDomainTests
    {
        private static AssignedRole RoleWithDomains(params string[] domains)
        {
            var role = new AssignedRole
            {
                RoleId = StandardRoles.CertificateConsumer.Id,
                SecurityPrincipalId = "test_principal",
                IncludedResources = []
            };

            foreach (var d in domains)
            {
                role.IncludedResources.Add(new Resource { ResourceType = ResourceTypes.Domain, Identifier = d });
            }

            return role;
        }

        private static bool IsPermitted(string? identifier, params string[] domains)
            => ResourceAccess.IsIdentifierPermittedByDomainRestrictions([RoleWithDomains(domains)], identifier);

        [TestMethod]
        [Description("A role with no domain resources is unrestricted")]
        public void UnrestrictedWhenNoDomainResources()
        {
            Assert.IsTrue(ResourceAccess.IsIdentifierPermittedByDomainRestrictions([RoleWithDomains()], "anything.example.com"));
            Assert.IsTrue(ResourceAccess.IsIdentifierPermittedByDomainRestrictions([], "anything.example.com"));
            Assert.IsTrue(ResourceAccess.IsIdentifierPermittedByDomainRestrictions(null, "anything.example.com"));

            // a role scoped to a different resource type does not impose a domain restriction
            var otherType = new AssignedRole
            {
                RoleId = StandardRoles.CertificateConsumer.Id,
                SecurityPrincipalId = "test_principal",
                IncludedResources = [new Resource { ResourceType = ResourceTypes.ManagedItem, Identifier = "some_item" }]
            };

            Assert.IsTrue(ResourceAccess.IsIdentifierPermittedByDomainRestrictions([otherType], "anything.example.com"));
        }

        [TestMethod]
        [Description("A restricted role denies an empty or missing identifier")]
        public void RestrictedRoleDeniesEmptyIdentifier()
        {
            Assert.IsFalse(IsPermitted(null, "example.com"));
            Assert.IsFalse(IsPermitted("", "example.com"));
            Assert.IsFalse(IsPermitted("   ", "example.com"));
        }

        [TestMethod]
        [Description("An exact domain rule matches only that domain")]
        public void ExactDomainRule()
        {
            Assert.IsTrue(IsPermitted("www.example.com", "www.example.com"));
            Assert.IsFalse(IsPermitted("secure.example.com", "www.example.com"));
            Assert.IsFalse(IsPermitted("example.com", "www.example.com"));
        }

        [TestMethod]
        [Description("Domain rules are case insensitive, as DNS names are")]
        public void RulesAreCaseInsensitive()
        {
            Assert.IsTrue(IsPermitted("WWW.Example.com", "www.example.com"));
            Assert.IsTrue(IsPermitted("www.example.com", "WWW.EXAMPLE.COM"));
            Assert.IsTrue(IsPermitted("Sub.Example.com", "*.EXAMPLE.com"));
        }

        [TestMethod]
        [Description("Rules and identifiers with surrounding whitespace are tolerated")]
        public void RulesAreTrimmed()
        {
            Assert.IsTrue(IsPermitted("www.example.com", " www.example.com "));
            Assert.IsTrue(IsPermitted(" www.example.com ", "www.example.com"));
        }

        [TestMethod]
        [Description("A wildcard rule matches direct subdomains and the root domain, but not deeper subdomains")]
        public void WildcardRuleMatchesOneLabel()
        {
            Assert.IsTrue(IsPermitted("random.example.com", "*.example.com"));

            // an explicit *.domain rule also covers the root domain itself
            Assert.IsTrue(IsPermitted("example.com", "*.example.com"));

            // wildcards only span a single label
            Assert.IsFalse(IsPermitted("a.b.example.com", "*.example.com"));

            Assert.IsFalse(IsPermitted("random.notexample.com", "*.example.com"));
            Assert.IsFalse(IsPermitted("example.com.evil.net", "*.example.com"));
        }

        [TestMethod]
        [Description("A single rule value may carry multiple rules, separated by semicolon or comma")]
        public void MultipleRulesInOneValue()
        {
            Assert.IsTrue(IsPermitted("example.com", "example.com;*.other.com"));
            Assert.IsTrue(IsPermitted("sub.other.com", "example.com;*.other.com"));
            Assert.IsTrue(IsPermitted("sub.other.com", "example.com, *.other.com "));
            Assert.IsFalse(IsPermitted("nope.com", "example.com;*.other.com"));
        }

        [TestMethod]
        [Description("A wildcard identifier requires an explicit wildcard rule, a root domain rule does not grant it")]
        public void WildcardIdentifierRequiresExplicitWildcardRule()
        {
            Assert.IsTrue(IsPermitted("*.example.com", "*.example.com"));
            Assert.IsTrue(IsPermitted("*.example.com", "other.com;*.example.com"));

            // being scoped to example.com must not confer authority over every subdomain
            Assert.IsFalse(IsPermitted("*.example.com", "example.com"));
            Assert.IsFalse(IsPermitted("*.example.com", "www.example.com"));

            // a wildcard rule higher up does not grant a wildcard for a subdomain
            Assert.IsFalse(IsPermitted("*.sub.example.com", "*.example.com"));
        }

        [TestMethod]
        [Description("Malformed identifiers are rejected rather than matched loosely")]
        public void MalformedIdentifiersDenied()
        {
            Assert.IsFalse(IsPermitted("*  lkjhasdf98862364", "*.microsoft.com"));
            Assert.IsFalse(IsPermitted("lkjhasdf98862364.*.microsoft.com", "*.microsoft.com"));
        }

        [TestMethod]
        [Description("Restrictions from all authorizing roles are pooled")]
        public void RestrictionsPooledAcrossRoles()
        {
            var roles = new List<AssignedRole>
            {
                RoleWithDomains("*.finance.example.com"),
                RoleWithDomains("*.eng.example.com")
            };

            Assert.IsTrue(ResourceAccess.IsIdentifierPermittedByDomainRestrictions(roles, "app.finance.example.com"));
            Assert.IsTrue(ResourceAccess.IsIdentifierPermittedByDomainRestrictions(roles, "app.eng.example.com"));
            Assert.IsFalse(ResourceAccess.IsIdentifierPermittedByDomainRestrictions(roles, "app.sales.example.com"));
        }

        [TestMethod]
        [Description("A role carrying domain restrictions restricts the principal even when another role has none")]
        public void RestrictionAppliesEvenWhenAnotherRoleIsUnrestricted()
        {
            var roles = new List<AssignedRole>
            {
                RoleWithDomains(),
                RoleWithDomains("*.finance.example.com")
            };

            Assert.IsTrue(ResourceAccess.IsIdentifierPermittedByDomainRestrictions(roles, "app.finance.example.com"));
            Assert.IsFalse(ResourceAccess.IsIdentifierPermittedByDomainRestrictions(roles, "app.sales.example.com"));
        }

        [TestMethod]
        [Description("Blank rule identifiers are ignored rather than treated as a restriction")]
        public void BlankRulesIgnored()
        {
            Assert.IsTrue(IsPermitted("anything.example.com", "   "));
            Assert.IsTrue(IsPermitted("app.finance.example.com", "  ", "*.finance.example.com"));
            Assert.IsFalse(IsPermitted("app.sales.example.com", "  ", "*.finance.example.com"));
        }

        [TestMethod]
        [Description("Certificate access requires every identifier on the cert to be permitted, not just one")]
        public void AllCertificateIdentifiersMustBePermitted()
        {
            var rules = ResourceAccess.GetDomainRestrictionRules([RoleWithDomains("*.finance.example.com")]);

            string[] withinScope = ["app.finance.example.com", "api.finance.example.com"];
            string[] partiallyOutOfScope = ["app.finance.example.com", "app.sales.example.com"];

            Assert.IsTrue(
                withinScope.All(i => ResourceAccess.IsIdentifierPermittedByDomainRules(rules, i)),
                "a cert whose identifiers are all in scope is accessible");

            Assert.IsFalse(
                partiallyOutOfScope.All(i => ResourceAccess.IsIdentifierPermittedByDomainRules(rules, i)),
                "a cert carrying any out of scope identifier is not accessible");
        }

        [TestMethod]
        [Description("Domain rule evaluation agrees with the shared DomainMatchRules implementation")]
        public void MatchesSharedDomainMatchRules()
        {
            string[] rules = ["*.example.com", "specific.other.com", "a.com;*.b.com"];
            string[] identifiers =
            [
                "example.com", "www.example.com", "a.b.example.com", "specific.other.com",
                "other.com", "a.com", "sub.b.com", "b.com", "unrelated.net"
            ];

            foreach (var rule in rules)
            {
                foreach (var identifier in identifiers)
                {
                    Assert.AreEqual(
                        Certify.Models.DomainMatchRules.IsMatch(rule, identifier),
                        IsPermitted(identifier, rule),
                        $"Rule [{rule}] against identifier [{identifier}] must agree with DomainMatchRules");
                }
            }
        }
    }
}
