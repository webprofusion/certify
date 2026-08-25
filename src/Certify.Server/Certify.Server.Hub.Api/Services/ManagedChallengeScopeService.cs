using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Resolves managed-challenge access for security principals and validates identifier coverage.
    /// Uses centralized Access Control evaluation rather than reconstructing role/policy graphs.
    /// </summary>
    public class ManagedChallengeScopeService
    {
        private readonly ICertifyInternalApiClient _client;
        private readonly ILogger<ManagedChallengeScopeService> _logger;

        private static readonly AuthContext SystemAuthContext = new() { UserId = StandardSecurityPrincipals.System };

        public ManagedChallengeScopeService(
            ICertifyInternalApiClient client,
            ILogger<ManagedChallengeScopeService> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// The security principal and role scope an API access token resolves to.
        /// </summary>
        public sealed record AccessTokenPrincipal(string SecurityPrincipalId, List<string>? ScopedAssignedRoles);

        /// <summary>
        /// Resolve the security principal an API access token belongs to, or null when the token is unknown.
        /// </summary>
        public async Task<AccessTokenPrincipal?> ResolveAccessTokenPrincipal(AccessToken? accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken?.ClientId) || string.IsNullOrWhiteSpace(accessToken?.Secret))
            {
                return null;
            }

            try
            {
                var assignedTokens = await _client.GetAssignedAccessTokens(SystemAuthContext);

                var matchingToken = assignedTokens?
                    .FirstOrDefault(at => at.AccessTokens?.Any(t =>
                        t.ClientId == accessToken.ClientId && t.Secret == accessToken.Secret) == true);

                if (string.IsNullOrWhiteSpace(matchingToken?.SecurityPrincipalId))
                {
                    return null;
                }

                return new AccessTokenPrincipal(matchingToken.SecurityPrincipalId, matchingToken.ScopedAssignedRoles?.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve security principal for access token {ClientId}", accessToken.ClientId);
                return null;
            }
        }

        /// <summary>
        /// Authorize a principal for the given identifiers. Principals whose authorizing roles are not
        /// tag-scoped are unrestricted, so identifier coverage is left to challenge fulfillment.
        /// Tag-scoped principals must have at least one accessible managed challenge per identifier.
        /// </summary>
        public async Task<(bool IsAuthorized, string? FailureReason)> AuthorizeIdentifiersForPrincipal(
            string securityPrincipalId,
            IEnumerable<string> identifiers,
            ICollection<string>? scopedAssignedRoles = null,
            string requiredActionId = StandardResourceActions.ManagedAcmePerformOrder)
        {
            var scope = await ResolveAccessScope(securityPrincipalId, scopedAssignedRoles, requiredActionId);

            if (!scope.HasAccess)
            {
                return (false, "Security principal is not authorised to use managed challenges");
            }

            // Domain restrictions apply regardless of tag scope, so check them before the tag filtering shortcut.
            var identifierList = identifiers?
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            var domainDenied = FindIdentifierDeniedByDomainRestrictions(scope, identifierList);

            if (domainDenied != null)
            {
                return (false, $"Identifier '{domainDenied}' is not permitted by the domain restrictions on this role assignment");
            }

            if (!scope.RequiresTagFiltering)
            {
                return (true, null);
            }

            var (canSatisfy, failureReason, _) = await ValidatePrincipalCanSatisfyIdentifiers(
                securityPrincipalId, identifiers, scopedAssignedRoles, requiredActionId);

            return (canSatisfy, failureReason);
        }

        /// <summary>
        /// Validate that the principal can satisfy managed challenges for every identifier.
        /// </summary>
        public async Task<(bool CanSatisfy, string? FailureReason, ICollection<ManagedChallenge> AccessibleChallenges)> ValidatePrincipalCanSatisfyIdentifiers(
            string securityPrincipalId,
            IEnumerable<string> identifiers,
            ICollection<string>? scopedAssignedRoles = null,
            string requiredActionId = StandardResourceActions.ManagedAcmePerformOrder)
        {
            if (string.IsNullOrWhiteSpace(securityPrincipalId))
            {
                return (false, "Security principal is required for managed ACME challenge authorization", Array.Empty<ManagedChallenge>());
            }

            var identifierList = identifiers?
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            if (identifierList.Count == 0)
            {
                return (false, "At least one identifier is required", Array.Empty<ManagedChallenge>());
            }

            try
            {
                var scope = await ResolveAccessScope(securityPrincipalId, scopedAssignedRoles, requiredActionId);

                if (!scope.HasAccess)
                {
                    _logger.LogWarning(
                        "Managed challenge access denied for principal {PrincipalId}: no authorizing role grants {ActionId} (scoped assigned roles: {ScopedRoles})",
                        securityPrincipalId,
                        requiredActionId,
                        scopedAssignedRoles?.Count > 0 ? string.Join(", ", scopedAssignedRoles) : "(none)");

                    return (false, "Security principal is not authorised to use managed challenges", Array.Empty<ManagedChallenge>());
                }

                // Enforce per-identifier domain restrictions on the authorizing roles.
                var domainDenied = FindIdentifierDeniedByDomainRestrictions(scope, identifierList);

                if (domainDenied != null)
                {
                    _logger.LogWarning(
                        "Managed challenge identifier '{Identifier}' denied for principal {PrincipalId} by domain restrictions on authorizing roles",
                        domainDenied,
                        securityPrincipalId);

                    return (false, $"Identifier '{domainDenied}' is not permitted by the domain restrictions on this role assignment", Array.Empty<ManagedChallenge>());
                }

                var accessible = await GetAccessibleManagedChallenges(scope);

                _logger.LogDebug(
                    "Managed challenge scope for principal {PrincipalId}: unrestricted={IsUnrestricted}, allowUnscoped={AllowUnscoped}, accessibleChallenges={Count}, identifiers={Identifiers}, rules={Rules}",
                    securityPrincipalId,
                    scope.IsUnrestricted,
                    scope.AllowUnscopedResources,
                    accessible.Count,
                    string.Join(", ", identifierList),
                    DescribeDomainMatchRules(accessible));

                if (accessible.Count == 0)
                {
                    _logger.LogWarning(
                        "No managed challenges are accessible to principal {PrincipalId} (unrestricted={IsUnrestricted}, allowUnscoped={AllowUnscoped}). Check managed challenge tags against the authorizing role tag scopes.",
                        securityPrincipalId,
                        scope.IsUnrestricted,
                        scope.AllowUnscopedResources);

                    return (
                        false,
                        "No managed challenges are accessible to this security principal. The authorizing role is tag scoped and no managed challenge matches that tag scope.",
                        accessible);
                }

                if (!ManagedChallengeAccess.CanSatisfyIdentifiers(identifierList, accessible, out var unsatisfied))
                {
                    var rulesSummary = DescribeDomainMatchRules(accessible);

                    var detail = unsatisfied.Count == 1
                        ? $"No accessible managed challenge matches identifier '{unsatisfied[0]}'. Accessible Domain Match rules: {rulesSummary}. Note that '*.example.com' matches example.com and one subdomain level only (not deeper subdomains)."
                        : $"No accessible managed challenge matches identifiers: {string.Join(", ", unsatisfied)}. Accessible Domain Match rules: {rulesSummary}. Note that '*.example.com' matches example.com and one subdomain level only (not deeper subdomains).";

                    _logger.LogWarning(
                        "Managed challenge identifier matching failed for principal {PrincipalId} against {Count} accessible challenge(s). Unsatisfied: {Identifiers}. Accessible challenges and their Domain Match rules: {Rules}",
                        securityPrincipalId,
                        accessible.Count,
                        string.Join(", ", unsatisfied),
                        rulesSummary);

                    return (false, detail, accessible);
                }

                return (true, null, accessible);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate managed challenge access for principal {PrincipalId}", securityPrincipalId);
                return (false, "Failed to validate managed challenge access for this account", Array.Empty<ManagedChallenge>());
            }
        }

        public async Task<ICollection<ManagedChallenge>> GetAccessibleManagedChallenges(
            string securityPrincipalId,
            ICollection<string>? scopedAssignedRoles = null,
            string requiredActionId = StandardResourceActions.ManagedAcmePerformOrder)
        {
            var scope = await ResolveAccessScope(securityPrincipalId, scopedAssignedRoles, requiredActionId);
            return await GetAccessibleManagedChallenges(scope);
        }

        /// <summary>
        /// Get managed challenges accessible under a previously resolved access scope.
        /// </summary>
        public async Task<ICollection<ManagedChallenge>> GetAccessibleManagedChallenges(ManagedChallengeAccessScope scope)
        {
            if (scope == null || !scope.HasAccess)
            {
                return Array.Empty<ManagedChallenge>();
            }

            var challenges = await _client.GetManagedChallenges(SystemAuthContext) ?? Array.Empty<ManagedChallenge>();
            if (scope.IsUnrestricted)
            {
                return challenges.ToList();
            }

            var allTags = await _client.GetAllHubItemTags(null, null, TaggedItemTypes.ManagedChallenge, null, SystemAuthContext)
                                ?? Array.Empty<ItemTag>();

            var tagsByChallengeId = allTags
                .GroupBy(t => t.TaggedItemId)
                .ToDictionary(g => g.Key, g => g.ToList());

            return ManagedChallengeAccess.FilterChallenges(challenges, tagsByChallengeId, scope);
        }

        public async Task<ManagedChallengeAccessScope> ResolveAccessScope(
            string securityPrincipalId,
            ICollection<string>? scopedAssignedRoles = null,
            string requiredActionId = StandardResourceActions.ManagedAcmePerformOrder)
        {
            var allowUnscoped = await GetAllowUnscopedManagedChallengesPreference();

            var check = new AccessCheck
            {
                SecurityPrincipalId = securityPrincipalId,
                ResourceType = requiredActionId == StandardResourceActions.ManagedAcmePerformOrder
                    ? ResourceTypes.ManagedAcme
                    : ResourceTypes.ManagedChallenge,
                ResourceActionId = requiredActionId,
                AllowUnscopedResources = allowUnscoped
            };

            if (scopedAssignedRoles?.Count > 0)
            {
                check.ScopedAssignedRoles = scopedAssignedRoles.ToList();
            }

            // Access resolution is centralized in Access Control. Fail closed if evaluation is unavailable,
            // otherwise a transient error would silently promote a tag-scoped principal to unrestricted access.
            try
            {
                var scope = await _client.EvaluateAccessScope(check, SystemAuthContext);
                return new ManagedChallengeAccessScope(scope ?? new ResourceAccessScope());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EvaluateAccessScope failed for principal {PrincipalId}; denying managed challenge access", securityPrincipalId);

                return new ManagedChallengeAccessScope
                {
                    HasAccess = false,
                    AllowUnscopedResources = allowUnscoped
                };
            }
        }

        private async Task<bool> GetAllowUnscopedManagedChallengesPreference()
        {
            try
            {
                var hubSettings = await _client.GetHubSettings(SystemAuthContext);
                return hubSettings?.ManagedChallenge?.AllowUnscopedForScopedPrincipals == true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read hub managed challenge settings; defaulting to strict scoping");
            }

            return false;
        }

        /// <summary>
        /// The first identifier not permitted by the domain restrictions on the authorizing roles, or null when
        /// all are permitted. Domain restrictions are Domain Match rules held as domain-typed IncludedResources,
        /// so a principal whose authorizing roles carry none is unrestricted.
        /// </summary>
        private static string? FindIdentifierDeniedByDomainRestrictions(ManagedChallengeAccessScope scope, ICollection<string> identifiers)
        {
            // resolve the rule set once, rather than rebuilding it for every identifier
            var domainRules = ResourceAccess.GetDomainRestrictionRules(scope.AuthorizingRoles);

            if (domainRules.Count == 0)
            {
                return null;
            }

            return identifiers.FirstOrDefault(id => !ResourceAccess.IsIdentifierPermittedByDomainRules(domainRules, id));
        }

        /// <summary>
        /// Summarise the accessible managed challenges and their Domain Match rules, so a matching
        /// failure reports exactly which rules were evaluated.
        /// </summary>
        private static string DescribeDomainMatchRules(ICollection<ManagedChallenge> accessible)
        {
            if (accessible == null || accessible.Count == 0)
            {
                return "(none)";
            }

            return string.Join(", ", accessible.Select(c =>
            {
                var name = string.IsNullOrWhiteSpace(c.Title) ? c.Id : c.Title;
                var rules = string.IsNullOrWhiteSpace(c.ChallengeConfig?.DomainMatch)
                    ? "(no Domain Match set - matches nothing unless it is the only fallback)"
                    : c.ChallengeConfig!.DomainMatch;

                return $"'{name}' => [{rules}]";
            }));
        }
    }
}
