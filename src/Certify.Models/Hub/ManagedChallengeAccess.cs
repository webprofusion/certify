using System;
using System.Collections.Generic;
using System.Linq;
using Certify.Models.Config;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Compatibility alias for managed-challenge consumers. Access resolution is centralized in
    /// <see cref="ResourceAccessScope"/> / Access Control.
    /// </summary>
    public class ManagedChallengeAccessScope : ResourceAccessScope
    {
        public ManagedChallengeAccessScope()
        {
        }

        public ManagedChallengeAccessScope(ResourceAccessScope scope)
        {
            if (scope == null)
            {
                return;
            }

            HasAccess = scope.HasAccess;
            IsUnrestricted = scope.IsUnrestricted;
            AuthorizingRoles = scope.AuthorizingRoles ?? [];
            AllowUnscopedResources = scope.AllowUnscopedResources;
        }
    }

    /// <summary>
    /// Domain-matching helpers for managed challenges. Role/policy access resolution lives in Access Control.
    /// </summary>
    public static class ManagedChallengeAccess
    {
        /// <summary>
        /// Filter managed challenges to those accessible under the resolved access scope.
        /// </summary>
        public static ICollection<ManagedChallenge> FilterChallenges(
            IEnumerable<ManagedChallenge> challenges,
            IDictionary<string, List<ItemTag>> tagsByChallengeId,
            ResourceAccessScope scope)
        {
            if (challenges == null)
            {
                return Array.Empty<ManagedChallenge>();
            }

            if (scope == null || !scope.HasAccess)
            {
                return Array.Empty<ManagedChallenge>();
            }

            if (scope.IsUnrestricted)
            {
                return challenges.ToList();
            }

            tagsByChallengeId ??= new Dictionary<string, List<ItemTag>>();
            var filtered = new List<ManagedChallenge>();

            foreach (var challenge in challenges)
            {
                if (IsChallengeAccessible(challenge, tagsByChallengeId, scope))
                {
                    filtered.Add(challenge);
                }
            }

            return filtered;
        }

        /// <summary>
        /// True when the challenge is accessible under the resolved access scope.
        /// </summary>
        public static bool IsChallengeAccessible(
            ManagedChallenge challenge,
            IDictionary<string, List<ItemTag>> tagsByChallengeId,
            ResourceAccessScope scope)
        {
            if (challenge == null)
            {
                return false;
            }

            tagsByChallengeId ??= new Dictionary<string, List<ItemTag>>();
            tagsByChallengeId.TryGetValue(challenge.Id, out var itemTags);

            return ResourceAccess.IsResourceInScope(scope, ResourceAccess.ToTagSummaries(itemTags));
        }

        /// <summary>
        /// Find the most specific matching managed challenge for an identifier within an already-accessible set.
        /// </summary>
        public static ManagedChallenge? FindBestMatch(ManagedChallengeRequest request, ICollection<ManagedChallenge> accessibleChallenges)
        {
            if (accessibleChallenges == null || accessibleChallenges.Count == 0)
            {
                return null;
            }

            // Prefer explicit domain matches over empty/global DomainMatch configs.
            var matchedConfig = accessibleChallenges.FirstOrDefault(c => string.IsNullOrEmpty(c.ChallengeConfig?.DomainMatch));

            if (request?.Identifier != null && !string.IsNullOrEmpty(request.Identifier))
            {
                var configsPerDomain = new Dictionary<string, ManagedChallenge>(StringComparer.OrdinalIgnoreCase);

                foreach (var managedChallenge in accessibleChallenges.Where(c => !string.IsNullOrEmpty(c.ChallengeConfig?.DomainMatch)))
                {
                    var c = managedChallenge.ChallengeConfig;
                    if (string.IsNullOrWhiteSpace(c?.DomainMatch))
                    {
                        continue;
                    }

                    var domains = c.DomainMatch.Split(';', ',').Where(d => !string.IsNullOrWhiteSpace(d));

                    foreach (var d in domains)
                    {
                        if (!configsPerDomain.ContainsKey(d))
                        {
                            configsPerDomain.Add(d, managedChallenge);
                        }
                    }
                }

                var identifierKey = request.Identifier.StartsWith("*.", StringComparison.Ordinal)
                                    ? request.Identifier.Substring(2)
                                    : request.Identifier;

                if (configsPerDomain.TryGetValue(request.Identifier, out var exact))
                {
                    return exact;
                }

                if (configsPerDomain.TryGetValue(identifierKey, out var exactNoWildcard))
                {
                    return exactNoWildcard;
                }

                if (configsPerDomain.TryGetValue("*." + identifierKey, out var wildExact))
                {
                    return wildExact;
                }

                var allMatchingConfigKeys = configsPerDomain.Keys.OrderByDescending(l => l.Length);

                foreach (var wildcard in allMatchingConfigKeys.Where(k => k.StartsWith("*.", StringComparison.OrdinalIgnoreCase)))
                {
                    if (ManagedCertificate.IsDomainOrWildcardMatch([wildcard], request.Identifier))
                    {
                        return configsPerDomain[wildcard];
                    }
                }

                foreach (var configDomain in allMatchingConfigKeys)
                {
                    if (identifierKey.EndsWith($".{configDomain}", StringComparison.OrdinalIgnoreCase))
                    {
                        return configsPerDomain[configDomain];
                    }
                }
            }

            return matchedConfig;
        }

        /// <summary>
        /// True when every identifier has a matching accessible managed challenge.
        /// </summary>
        public static bool CanSatisfyIdentifiers(
            IEnumerable<string> identifiers,
            ICollection<ManagedChallenge> accessibleChallenges,
            out List<string> unsatisfiedIdentifiers)
        {
            unsatisfiedIdentifiers = [];

            if (identifiers == null)
            {
                return true;
            }

            foreach (var identifier in identifiers.Where(i => !string.IsNullOrWhiteSpace(i)))
            {
                var match = FindBestMatch(
                    new ManagedChallengeRequest
                    {
                        Identifier = identifier,
                        ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS
                    },
                    accessibleChallenges);

                if (match == null)
                {
                    unsatisfiedIdentifiers.Add(identifier);
                }
            }

            return unsatisfiedIdentifiers.Count == 0;
        }

        public static List<TagSummary> ToTagSummaries(IEnumerable<ItemTag>? tags) => ResourceAccess.ToTagSummaries(tags);

        public static bool IsResourceTagScopeMatch(List<TagSummary>? resourceTags, List<TagScope>? scopedTags, bool requireAll)
            => ResourceAccess.IsResourceTagScopeMatch(resourceTags, scopedTags, requireAll);
    }
}
