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
        /// Domain match rule evaluation is shared with <see cref="DomainMatchRules"/>.
        /// </summary>
        public static ManagedChallenge? FindBestMatch(ManagedChallengeRequest request, ICollection<ManagedChallenge> accessibleChallenges)
        {
            if (accessibleChallenges == null || accessibleChallenges.Count == 0)
            {
                return null;
            }

            return DomainMatchRules.FindBestMatch(
                request?.Identifier,
                accessibleChallenges,
                c => c.ChallengeConfig?.DomainMatch);
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
