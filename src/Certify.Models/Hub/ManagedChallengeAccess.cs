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
        /// Filter managed challenges to those accessible under the resolved access scope. When an identifier is given,
        /// only challenges reachable through a role assignment which also permits that identifier are kept, so a
        /// challenge from one assignment's tag scope is never used for an identifier only another assignment permits.
        /// </summary>
        public static ICollection<ManagedChallenge> FilterChallenges(
            IEnumerable<ManagedChallenge> challenges,
            IDictionary<string, List<ItemTag>> tagsByChallengeId,
            ResourceAccessScope scope,
            string? identifier = null)
        {
            if (challenges == null)
            {
                return Array.Empty<ManagedChallenge>();
            }

            if (scope == null || !scope.HasAccess)
            {
                return Array.Empty<ManagedChallenge>();
            }

            tagsByChallengeId ??= new Dictionary<string, List<ItemTag>>();

            return challenges
                .Where(c => IsChallengeAccessible(c, tagsByChallengeId, scope, identifier))
                .ToList();
        }

        /// <summary>
        /// True when the challenge is accessible under the resolved access scope, and when an identifier is given,
        /// through a role assignment which also permits that identifier.
        /// </summary>
        public static bool IsChallengeAccessible(
            ManagedChallenge challenge,
            IDictionary<string, List<ItemTag>> tagsByChallengeId,
            ResourceAccessScope scope,
            string? identifier = null)
        {
            if (challenge == null)
            {
                return false;
            }

            tagsByChallengeId ??= new Dictionary<string, List<ItemTag>>();
            tagsByChallengeId.TryGetValue(challenge.Id, out var itemTags);

            return ResourceAccess.IsResourcePermitted(
                scope,
                itemTags ?? [],
                string.IsNullOrWhiteSpace(identifier) ? null : [identifier]);
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

        // ToTagSummaries and IsResourceTagScopeMatch used to be re-exposed here, forwarding to ResourceAccess.
        // Nothing called either, and a second name for one implementation is how a caller ends up believing there
        // are two behaviours to choose between.
    }
}
