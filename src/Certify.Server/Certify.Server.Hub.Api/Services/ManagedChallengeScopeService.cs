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
                var accessible = await GetAccessibleManagedChallenges(securityPrincipalId, scopedAssignedRoles, requiredActionId);

                if (!ManagedChallengeAccess.CanSatisfyIdentifiers(identifierList, accessible, out var unsatisfied))
                {
                    var detail = unsatisfied.Count == 1
                        ? $"No accessible managed challenge matches identifier '{unsatisfied[0]}' for this security principal's role scope."
                        : $"No accessible managed challenge matches identifiers: {string.Join(", ", unsatisfied)} for this security principal's role scope.";

                    _logger.LogWarning(
                        "Managed challenge scope validation failed for principal {PrincipalId}. Unsatisfied: {Identifiers}",
                        securityPrincipalId,
                        string.Join(", ", unsatisfied));

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
            if (!scope.HasAccess)
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
    }
}
