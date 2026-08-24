using System;
using System.Collections.Generic;
using System.Linq;

namespace Certify.Models
{
    /// <summary>
    /// Canonical evaluation of Domain Match rules, shared by all consumers (challenge config selection,
    /// managed challenge selection etc) so the matching semantics are only defined in one place.
    ///
    /// A rule can take the form:
    /// <list type="bullet">
    /// <item><description><c>domain.com</c> - matches that exact domain</description></item>
    /// <item><description><c>*.domain.com</c> - matches the domain and its first level subdomains</description></item>
    /// </list>
    /// Multiple rules can be supplied in one value, separated by semicolons (commas are also tolerated).
    /// </summary>
    public static class DomainMatchRules
    {
        /// <summary>
        /// Split a domain match rule value into its individual normalised rules.
        /// </summary>
        public static IEnumerable<string> ParseRules(string? domainMatch)
        {
            if (string.IsNullOrWhiteSpace(domainMatch))
            {
                return Array.Empty<string>();
            }

            // users may enter comma separators instead of semicolons
            return domainMatch!
                .Split(';', ',')
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d.Trim().ToLowerInvariant());
        }

        /// <summary>
        /// True when the identifier is matched by any of the rules in the supplied domain match value.
        /// </summary>
        public static bool IsMatch(string? domainMatch, string? identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return false;
            }

            var rules = ParseRules(domainMatch).ToList();

            if (rules.Count == 0)
            {
                return false;
            }

            var literalKey = identifier!.Trim().ToLowerInvariant();
            var identifierKey = NormaliseIdentifier(identifier)!;

            // an exact rule for the identifier as supplied (including a wildcard identifier)
            if (rules.Contains(literalKey))
            {
                return true;
            }

            // an explicit *.identifier rule matches the root domain itself
            if (rules.Contains("*." + identifierKey))
            {
                return true;
            }

            if (rules.Contains(identifierKey))
            {
                return true;
            }

            return rules.Any(r => r.StartsWith("*.", StringComparison.Ordinal)
                && ManagedCertificate.IsDomainOrWildcardMatch(new List<string> { r }, identifierKey));
        }

        /// <summary>
        /// Select the most specific item whose domain match rules match the identifier. Items with no
        /// domain match rule act as the fallback (global) item.
        /// </summary>
        /// <typeparam name="T">The item type carrying a domain match rule.</typeparam>
        /// <param name="identifier">The identifier (domain) being matched.</param>
        /// <param name="items">Candidate items.</param>
        /// <param name="ruleSelector">Returns the domain match rule value for an item.</param>
        /// <returns>The best matching item, the first rule-less item, or null.</returns>
        public static T? FindBestMatch<T>(string? identifier, IEnumerable<T> items, Func<T, string?> ruleSelector) where T : class
        {
            if (items == null)
            {
                return null;
            }

            var candidates = items.Where(i => i != null).ToList();

            // fallback is the first item with no specific domain match rule
            var fallback = candidates.FirstOrDefault(i => string.IsNullOrWhiteSpace(ruleSelector(i)));

            var literalKey = identifier?.Trim().ToLowerInvariant();
            var identifierKey = NormaliseIdentifier(identifier);

            if (string.IsNullOrEmpty(identifierKey))
            {
                return fallback;
            }

            // expand items into a per rule lookup, first item to claim a rule wins
            var itemsPerRule = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in candidates.Where(i => !string.IsNullOrWhiteSpace(ruleSelector(i))))
            {
                foreach (var rule in ParseRules(ruleSelector(item)))
                {
                    if (!itemsPerRule.ContainsKey(rule))
                    {
                        itemsPerRule.Add(rule, item);
                    }
                }
            }

            // exact rule match for the identifier as supplied (including a wildcard identifier)
            if (itemsPerRule.TryGetValue(literalKey!, out var literalExact))
            {
                return literalExact;
            }

            // explicit wildcard rule for this exact domain (*.domain.com also covers domain.com)
            if (itemsPerRule.TryGetValue("*." + identifierKey, out var wildExact))
            {
                return wildExact;
            }

            if (itemsPerRule.TryGetValue(identifierKey!, out var exact))
            {
                return exact;
            }

            // most specific wildcard rule first (longest rule wins)
            foreach (var wildcard in itemsPerRule.Keys
                .Where(k => k.StartsWith("*.", StringComparison.Ordinal))
                .OrderByDescending(l => l.Length))
            {
                if (ManagedCertificate.IsDomainOrWildcardMatch(new List<string> { wildcard }, identifierKey))
                {
                    return itemsPerRule[wildcard];
                }
            }

            return fallback;
        }

        /// <summary>
        /// Normalise an identifier for rule comparison, treating a wildcard identifier as its root domain.
        /// </summary>
        private static string? NormaliseIdentifier(string? identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return null;
            }

            var value = identifier!.Trim().ToLowerInvariant();

            return value.StartsWith("*.", StringComparison.Ordinal) ? value.Substring(2) : value;
        }
    }
}
