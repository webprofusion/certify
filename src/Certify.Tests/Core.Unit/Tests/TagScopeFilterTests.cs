using System.Collections.Generic;
using System.Linq;
using Certify.Models.Hub;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for the shared tag scope parsing and matching used by the hub API and its clients
    /// </summary>
    [TestClass]
    public class TagScopeFilterTests
    {
        private static List<TagSummary> Tags(params string[] categoryAndValue)
        {
            return categoryAndValue
                .Select(t => t.Split('='))
                .Select(parts => new TagSummary { CategoryKey = parts[0], Value = parts[1] })
                .ToList();
        }

        [TestMethod]
        [Description("A scope with no value should parse as a category wildcard")]
        public void Parse_CategoryOnly_HasNoValue()
        {
            var scope = TagScopeFilter.Parse("environment");

            Assert.IsNotNull(scope);
            Assert.AreEqual("environment", scope.CategoryKey);
            Assert.IsNull(scope.Value, "a category only scope should match any value");
        }

        [TestMethod]
        [Description("A scope expressed as category=value should parse into both parts")]
        public void Parse_CategoryAndValue_HasBothParts()
        {
            var scope = TagScopeFilter.Parse("environment=production");

            Assert.IsNotNull(scope);
            Assert.AreEqual("environment", scope.CategoryKey);
            Assert.AreEqual("production", scope.Value);
        }

        [TestMethod]
        [Description("Values containing the separator should keep the remainder intact")]
        public void Parse_ValueContainingSeparator_KeepsRemainder()
        {
            var scope = TagScopeFilter.Parse("filter=a=b");

            Assert.IsNotNull(scope);
            Assert.AreEqual("filter", scope.CategoryKey);
            Assert.AreEqual("a=b", scope.Value);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("=production")]
        [Description("Scopes with no category key are not valid and should be discarded")]
        public void Parse_MissingCategoryKey_ReturnsNull(string input)
        {
            Assert.IsNull(TagScopeFilter.Parse(input));
        }

        [TestMethod]
        [Description("An empty value should be treated the same as a category wildcard")]
        public void Parse_EmptyValue_TreatedAsWildcard()
        {
            var scope = TagScopeFilter.Parse("environment=");

            Assert.IsNotNull(scope);
            Assert.IsNull(scope.Value);
        }

        [TestMethod]
        [Description("Scopes should round trip through their query string representation")]
        public void ToQueryValue_RoundTrips()
        {
            var scopes = new List<TagScope>
            {
                new TagScope { CategoryKey = "environment" },
                new TagScope { CategoryKey = "environment", Value = "production" }
            };

            var queryValues = TagScopeFilter.ToQueryValues(scopes);

            CollectionAssert.AreEqual(new[] { "environment", "environment=production" }, queryValues);

            var parsed = TagScopeFilter.ParseAll(queryValues);

            Assert.AreEqual(2, parsed.Count);
            Assert.IsNull(parsed[0].Value);
            Assert.AreEqual("production", parsed[1].Value);
        }

        [TestMethod]
        [Description("Invalid entries should be skipped rather than producing unusable scopes")]
        public void ParseAll_SkipsInvalidEntries()
        {
            var parsed = TagScopeFilter.ParseAll(new[] { "environment=production", "", "=orphan", "team" });

            Assert.AreEqual(2, parsed.Count);
            Assert.AreEqual("environment", parsed[0].CategoryKey);
            Assert.AreEqual("team", parsed[1].CategoryKey);
        }

        [TestMethod]
        [Description("With no scopes supplied everything should match, including untagged items")]
        public void Matches_NoScopes_MatchesEverything()
        {
            Assert.IsTrue(TagScopeFilter.Matches(Tags("environment=production"), new List<TagScope>(), matchAll: false));
            Assert.IsTrue(TagScopeFilter.Matches(new List<TagSummary>(), new List<TagScope>(), matchAll: false));
        }

        [TestMethod]
        [Description("A category only scope should match any value within that category")]
        public void Matches_CategoryWildcard_MatchesAnyValue()
        {
            var scopes = TagScopeFilter.ParseAll(new[] { "environment" });

            Assert.IsTrue(TagScopeFilter.Matches(Tags("environment=staging"), scopes, matchAll: false));
            Assert.IsFalse(TagScopeFilter.Matches(Tags("team=web"), scopes, matchAll: false));
        }

        [TestMethod]
        [Description("Matching should be case insensitive so the hub and UI agree on results")]
        public void Matches_IsCaseInsensitive()
        {
            var scopes = TagScopeFilter.ParseAll(new[] { "Environment=Production" });

            Assert.IsTrue(TagScopeFilter.Matches(Tags("environment=production"), scopes, matchAll: false));
        }

        [TestMethod]
        [Description("When not requiring all tags, matching any one scope should be enough")]
        public void Matches_AnyScope_MatchesOnSingleScope()
        {
            var scopes = TagScopeFilter.ParseAll(new[] { "environment=production", "team=web" });

            Assert.IsTrue(TagScopeFilter.Matches(Tags("team=web"), scopes, matchAll: false));
        }

        [TestMethod]
        [Description("When requiring all tags, every scope must be matched")]
        public void Matches_AllScopes_RequiresEveryScope()
        {
            var scopes = TagScopeFilter.ParseAll(new[] { "environment=production", "team=web" });

            Assert.IsFalse(TagScopeFilter.Matches(Tags("team=web"), scopes, matchAll: true));
            Assert.IsTrue(TagScopeFilter.Matches(Tags("team=web", "environment=production"), scopes, matchAll: true));
        }

        [TestMethod]
        [Description("Untagged items should be excluded when a tag filter is applied unless explicitly included")]
        public void Matches_UntaggedItem_ControlledByIncludeUntagged()
        {
            var valueScope = TagScopeFilter.ParseAll(new[] { "environment=production" });
            var categoryScope = TagScopeFilter.ParseAll(new[] { "environment" });

            Assert.IsFalse(TagScopeFilter.Matches(new List<TagSummary>(), valueScope, matchAll: false));
            Assert.IsTrue(TagScopeFilter.Matches(new List<TagSummary>(), valueScope, matchAll: false, includeUntagged: true));

            // a category only scope must behave the same way as a category:value scope for untagged items
            Assert.IsFalse(TagScopeFilter.Matches(new List<TagSummary>(), categoryScope, matchAll: false));
            Assert.IsTrue(TagScopeFilter.Matches(new List<TagSummary>(), categoryScope, matchAll: false, includeUntagged: true));
        }

        [TestMethod]
        [Description("A null tag collection should be treated as an untagged item")]
        public void Matches_NullTags_TreatedAsUntagged()
        {
            var scopes = TagScopeFilter.ParseAll(new[] { "environment=production" });

            Assert.IsFalse(TagScopeFilter.Matches((IEnumerable<TagSummary>?)null, scopes, matchAll: false));
            Assert.IsTrue(TagScopeFilter.Matches((IEnumerable<TagSummary>?)null, scopes, matchAll: false, includeUntagged: true));
        }

        [TestMethod]
        [Description("Matching should work equally over the ItemTag representation used by the backend")]
        public void Matches_ItemTags_UsesSameSemantics()
        {
            var scopes = TagScopeFilter.ParseAll(new[] { "environment=production" });

            var itemTags = new List<ItemTag>
            {
                new ItemTag("item-1", TaggedItemTypes.ManagedCertificate, "Environment", "PRODUCTION")
            };

            Assert.IsTrue(TagScopeFilter.Matches(itemTags, scopes, matchAll: false));
        }
    }
}
