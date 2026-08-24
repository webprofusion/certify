using System;
using System.Collections.Generic;
using System.Linq;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Parsing and matching of tag scopes, shared by the hub API and its clients so that tag filtering
    /// behaves identically wherever it is applied
    /// </summary>
    public static class TagScopeFilter
    {
        /// <summary>
        /// Separator between category and value when a scope is expressed as a single string, e.g. "environment=production"
        /// </summary>
        public const char ValueSeparator = '=';

        /// <summary>
        /// Express a tag scope as a single string for use as a query string value. A scope with no value
        /// (meaning any value within the category) is expressed as the category key alone
        /// </summary>
        /// <param name="scope"></param>
        /// <returns></returns>
        public static string? ToQueryValue(this TagScope scope)
        {
            if (string.IsNullOrWhiteSpace(scope?.CategoryKey))
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(scope!.Value)
                ? scope.CategoryKey
                : $"{scope.CategoryKey}{ValueSeparator}{scope.Value}";
        }

        /// <summary>
        /// Parse a tag scope expressed as "category" (any value in the category) or "category=value"
        /// </summary>
        /// <param name="value"></param>
        /// <returns>the parsed scope, or null if no category key was present</returns>
        public static TagScope? Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var separatorIndex = value!.IndexOf(ValueSeparator);

            if (separatorIndex < 0)
            {
                return new TagScope { CategoryKey = value.Trim() };
            }

            var categoryKey = value.Substring(0, separatorIndex).Trim();

            if (string.IsNullOrEmpty(categoryKey))
            {
                return null;
            }

            var scopeValue = value.Substring(separatorIndex + 1).Trim();

            return new TagScope
            {
                CategoryKey = categoryKey,

                // an empty value means any value within the category
                Value = string.IsNullOrEmpty(scopeValue) ? null : scopeValue
            };
        }

        /// <summary>
        /// Parse a set of tag scopes, ignoring any which do not specify a category key
        /// </summary>
        /// <param name="values"></param>
        /// <returns></returns>
        public static List<TagScope> ParseAll(IEnumerable<string?>? values)
        {
            if (values == null)
            {
                return new List<TagScope>();
            }

            return values
                .Select(Parse)
                .Where(s => s != null)
                .Select(s => s!)
                .ToList();
        }

        /// <summary>
        /// Express a set of tag scopes as query string values
        /// </summary>
        /// <param name="scopes"></param>
        /// <returns></returns>
        public static List<string> ToQueryValues(IEnumerable<TagScope>? scopes)
        {
            if (scopes == null)
            {
                return new List<string>();
            }

            return scopes
                .Select(ToQueryValue)
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .ToList();
        }

        /// <summary>
        /// Determine whether a single tag matches the given scope. A scope with no value matches any
        /// value within the category. Comparisons are case insensitive
        /// </summary>
        /// <param name="tag"></param>
        /// <param name="scope"></param>
        /// <returns></returns>
        public static bool MatchesScope(ITaggedValue? tag, TagScope? scope)
        {
            if (tag == null || scope == null)
            {
                return false;
            }

            if (!string.Equals(tag.CategoryKey, scope.CategoryKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return scope.Value == null || string.Equals(tag.Value, scope.Value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determine whether an item's tags satisfy the given tag scopes
        /// </summary>
        /// <param name="itemTags">the tags applied to the item</param>
        /// <param name="scopes">scopes to match against, an empty set matches everything</param>
        /// <param name="matchAll">if true every scope must be matched, otherwise any one scope is enough</param>
        /// <param name="includeUntagged">if true an item with no tags at all is also considered a match</param>
        /// <returns></returns>
        public static bool Matches(IEnumerable<ITaggedValue>? itemTags, IReadOnlyCollection<TagScope>? scopes, bool matchAll, bool includeUntagged = false)
        {
            if (scopes == null || scopes.Count == 0)
            {
                return true;
            }

            var tags = itemTags?.Where(t => t != null).ToList() ?? new List<ITaggedValue>();

            if (tags.Count == 0)
            {
                return includeUntagged;
            }

            bool ScopeMatched(TagScope scope) => tags.Any(t => MatchesScope(t, scope));

            return matchAll ? scopes.All(ScopeMatched) : scopes.Any(ScopeMatched);
        }

        /// <summary>
        /// Determine whether an item's tags satisfy the given tag scopes
        /// </summary>
        /// <typeparam name="TTag"></typeparam>
        /// <param name="itemTags"></param>
        /// <param name="scopes"></param>
        /// <param name="matchAll"></param>
        /// <param name="includeUntagged"></param>
        /// <returns></returns>
        public static bool Matches<TTag>(IEnumerable<TTag>? itemTags, IReadOnlyCollection<TagScope>? scopes, bool matchAll, bool includeUntagged = false) where TTag : ITaggedValue
        {
            return Matches(itemTags?.Cast<ITaggedValue>(), scopes, matchAll, includeUntagged);
        }
    }
}
