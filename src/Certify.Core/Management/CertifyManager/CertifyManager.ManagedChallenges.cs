using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management.Challenges;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        private static string CreateManagedChallengeCleanupKey(ManagedChallengeRequest request)
        {
            return $"{request.ManagedCertId ?? string.Empty}|{request.Identifier ?? string.Empty}|{request.ResponseKey ?? string.Empty}|{request.ResponseValue ?? string.Empty}|{Guid.NewGuid():N}";
        }

        private static bool IsManagedChallengeTypeSupported(string? challengeType)
        {
            return string.Equals(challengeType, SupportedChallengeTypes.CHALLENGE_TYPE_DNS, StringComparison.OrdinalIgnoreCase);
        }

        public async Task<ICollection<ManagedChallenge>> GetManagedChallenges()
        {
            return await _configStore.GetItems<ManagedChallenge>(nameof(ManagedChallenge));
        }

        /// <summary>
        /// Get managed challenges filtered by tag scopes (for access control)
        /// </summary>
        /// <param name="tagScopes">Tag scopes to filter by. If null/empty, returns all challenges.</param>
        /// <param name="requireAllTags">If true, challenge must match ALL tag scopes (AND). If false, match ANY (OR).</param>
        /// <param name="includeUntagged">If true, include challenges with no tags. Default false for tag-scoped access.</param>
        /// <returns>Filtered collection of managed challenges</returns>
        public async Task<ICollection<ManagedChallenge>> GetManagedChallengesWithTagFilter(
            ICollection<TagScope>? tagScopes = null,
            bool requireAllTags = false,
            bool includeUntagged = false)
        {
            var challenges = await GetManagedChallenges();

            if (tagScopes == null || !tagScopes.Any())
            {
                // No tag filtering - return all
                return challenges;
            }

            // Get tags for all managed challenges
            var challengeTags = await GetAllHubItemTags(null, null, TaggedItemTypes.ManagedChallenge);
            var tagsByChallengeId = challengeTags.GroupBy(t => t.TaggedItemId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var filteredChallenges = new List<ManagedChallenge>();

            foreach (var challenge in challenges)
            {
                if (!tagsByChallengeId.TryGetValue(challenge.Id, out var itemTags) || !itemTags.Any())
                {
                    // Challenge has no tags
                    if (includeUntagged)
                    {
                        filteredChallenges.Add(challenge);
                    }

                    continue;
                }

                // Tag scope matching is centralized so role-scoped and explicit tag filtering stay consistent
                if (ResourceAccess.IsResourceTagScopeMatch(
                        ResourceAccess.ToTagSummaries(itemTags),
                        tagScopes.ToList(),
                        requireAllTags))
                {
                    filteredChallenges.Add(challenge);
                }
            }

            return filteredChallenges;
        }

        /// <summary>
        /// Get managed challenge summaries with tags included (for API responses)
        /// </summary>
        public async Task<ICollection<ManagedChallengeSummary>> GetManagedChallengeSummaries(
            ICollection<TagScope>? tagScopes = null,
            bool requireAllTags = false,
            bool includeUntagged = false)
        {
            var challenges = await GetManagedChallengesWithTagFilter(tagScopes, requireAllTags, includeUntagged);
            var summaries = new List<ManagedChallengeSummary>();

            foreach (var challenge in challenges)
            {
                var tags = await GetHubItemTags(TaggedItemTypes.ManagedChallenge, challenge.Id);
                summaries.Add(new ManagedChallengeSummary
                {
                    Id = challenge.Id,
                    Title = challenge.Title,
                    ChallengeConfig = challenge.ChallengeConfig,
                    Tags = tags.ToList()
                });
            }

            return summaries;
        }

        public async Task<ActionResult> UpdateManagedChallenge(ManagedChallenge update)
        {
            if (string.IsNullOrEmpty(update.Id))
            {
                update.Id = Guid.NewGuid().ToString();
            }

            await _configStore.Update<ManagedChallenge>(nameof(ManagedChallenge), update);
            return new ActionResult { IsSuccess = true };
        }

        public async Task<ActionResult> DeleteManagedChallenge(string id)
        {
            var deleted = await _configStore.Delete<ManagedChallenge>(nameof(ManagedChallenge), id);

            if (deleted)
            {
                await RemoveHubItemTagsForItem(TaggedItemTypes.ManagedChallenge, id);
            }

            return new ActionResult { IsSuccess = deleted };
        }

        /// <summary>
        /// Resolve managed-challenge access for a security principal for a specific resource action.
        /// When authorizing roles are tag-scoped, only matching tagged challenges are accessible
        /// unless <see cref="ManagedChallengeSettings.AllowUnscopedForScopedPrincipals"/> is enabled.
        /// </summary>
        public async Task<ManagedChallengeAccessScope> GetManagedChallengeAccessScope(
            string? securityPrincipalId,
            ICollection<string>? scopedAssignedRoles = null,
            string requiredActionId = StandardResourceActions.ManagedChallengeRequest)
        {
            if (string.IsNullOrWhiteSpace(securityPrincipalId))
            {
                return new ManagedChallengeAccessScope { HasAccess = false };
            }

            var access = await GetCurrentAccessControl();
            var hubSettings = await GetHubSettings();

            var check = new AccessCheck
            {
                SecurityPrincipalId = securityPrincipalId,
                ResourceType = requiredActionId == StandardResourceActions.ManagedAcmePerformOrder
                    ? ResourceTypes.ManagedAcme
                    : ResourceTypes.ManagedChallenge,
                ResourceActionId = requiredActionId,
                AllowUnscopedResources = hubSettings.ManagedChallenge.AllowUnscopedForScopedPrincipals
            };

            if (scopedAssignedRoles?.Count > 0)
            {
                check.ScopedAssignedRoles = scopedAssignedRoles.ToList();
            }

            // System context evaluates the target principal without auto-allowing access.
            var scope = await access.EvaluateAccessScope(StandardSecurityPrincipals.System, check);
            return new ManagedChallengeAccessScope(scope);
        }

        /// <summary>
        /// Get managed challenges accessible to the given principal for the specified action.
        /// </summary>
        public async Task<ICollection<ManagedChallenge>> GetAccessibleManagedChallenges(
            string? securityPrincipalId,
            ICollection<string>? scopedAssignedRoles = null,
            string requiredActionId = StandardResourceActions.ManagedChallengeRequest)
        {
            var scope = await GetManagedChallengeAccessScope(securityPrincipalId, scopedAssignedRoles, requiredActionId);
            return await GetAccessibleManagedChallenges(scope);
        }

        /// <summary>
        /// Get managed challenges accessible under a previously resolved access scope.
        /// </summary>
        public async Task<ICollection<ManagedChallenge>> GetAccessibleManagedChallenges(ManagedChallengeAccessScope scope)
        {
            var challenges = await GetManagedChallenges();

            if (scope == null || !scope.HasAccess)
            {
                return [];
            }

            if (scope.IsUnrestricted)
            {
                return challenges;
            }

            var challengeTags = await GetAllHubItemTags(null, null, TaggedItemTypes.ManagedChallenge);
            var tagsByChallengeId = challengeTags
                .GroupBy(t => t.TaggedItemId)
                .ToDictionary(g => g.Key, g => g.ToList());

            return ManagedChallengeAccess.FilterChallenges(challenges, tagsByChallengeId, scope);
        }

        /// <summary>
        /// Whether a security principal may use managed challenges for a set of identifiers.
        ///
        /// This is the one answer to that question. It checks, in order: that the principal holds a role granting
        /// the action, that every identifier is within the domain restrictions on the roles which granted it, and
        /// - when the caller needs the request to be fulfillable - that an accessible managed challenge matches
        /// every identifier.
        /// </summary>
        public async Task<ActionResult> AuthorizeManagedChallengeIdentifiers(ManagedChallengeAuthorizationCheck check)
        {
            if (check == null || string.IsNullOrWhiteSpace(check.SecurityPrincipalId))
            {
                return new ActionResult("A security principal is required for managed challenge authorization", false);
            }

            var identifiers = check.Identifiers?
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            ManagedChallengeAccessScope scope;

            try
            {
                scope = await GetManagedChallengeAccessScope(check.SecurityPrincipalId, check.ScopedAssignedRoles, check.RequiredActionId);
            }
            catch (Exception exp)
            {
                // fail closed: a transient evaluation error must not promote a scoped principal to unrestricted
                _serviceLog?.Error(exp, "Failed to evaluate managed challenge access scope for principal {principalId}", check.SecurityPrincipalId);
                return new ActionResult("Could not evaluate managed challenge access for this security principal", false);
            }

            if (!scope.HasAccess)
            {
                _serviceLog?.Warning(
                    "Managed challenge access denied for principal {principalId}: no assigned role grants {actionId} (scoped assigned roles: {scopedRoles}). {roleAssignments}",
                    check.SecurityPrincipalId,
                    check.RequiredActionId,
                    check.ScopedAssignedRoles?.Count > 0 ? string.Join(", ", check.ScopedAssignedRoles) : "(none)",
                    await DescribeStoredRoleAssignments(check.SecurityPrincipalId, check.RequiredActionId));

                return new ActionResult("Security principal is not authorised to use managed challenges", false);
            }

            // Domain restrictions are Domain Match rules held as domain-typed IncludedResources on the authorizing
            // roles, so a principal whose roles carry none is unrestricted. These used to be enforced only in the
            // hub API, which meant any other caller reaching this path was not subject to them at all.
            var domainRules = ResourceAccess.GetDomainRestrictionRules(scope.AuthorizingRoles);

            if (domainRules.Count > 0)
            {
                var denied = identifiers.Find(i => !ResourceAccess.IsIdentifierPermittedByDomainRules(domainRules, i));

                if (denied != null)
                {
                    _serviceLog?.Warning(
                        "Managed challenge identifier '{identifier}' denied for principal {principalId} by domain restrictions on the authorizing roles",
                        denied,
                        check.SecurityPrincipalId);

                    return new ActionResult($"Identifier '{denied}' is not permitted by the domain restrictions on this role assignment", false);
                }
            }

            if (!check.RequireSatisfiableChallenge && !scope.RequiresTagFiltering)
            {
                // unrestricted for this action, and the caller does not need the request to be fulfillable
                return new ActionResult("Authorized", true);
            }

            if (identifiers.Count == 0)
            {
                return new ActionResult("At least one identifier is required", false);
            }

            var accessible = await GetAccessibleManagedChallenges(scope);

            if (accessible.Count == 0)
            {
                _serviceLog?.Warning(
                    "No managed challenges are accessible to principal {principalId} (unrestricted={isUnrestricted}, allowUnscoped={allowUnscoped}). Check managed challenge tags against the authorizing role tag scopes.",
                    check.SecurityPrincipalId,
                    scope.IsUnrestricted,
                    scope.AllowUnscopedResources);

                return new ActionResult(
                    "No managed challenges are accessible to this security principal. The authorizing role is tag scoped and no managed challenge matches that tag scope.",
                    false);
            }

            if (!ManagedChallengeAccess.CanSatisfyIdentifiers(identifiers, accessible, out var unsatisfied))
            {
                var rules = DescribeDomainMatchRules(accessible);

                var detail = unsatisfied.Count == 1
                    ? $"No accessible managed challenge matches identifier '{unsatisfied[0]}'. Accessible Domain Match rules: {rules}. Note that '*.example.com' matches example.com and one subdomain level only (not deeper subdomains)."
                    : $"No accessible managed challenge matches identifiers: {string.Join(", ", unsatisfied)}. Accessible Domain Match rules: {rules}. Note that '*.example.com' matches example.com and one subdomain level only (not deeper subdomains).";

                _serviceLog?.Warning(
                    "Managed challenge identifier matching failed for principal {principalId} against {count} accessible challenge(s). Unsatisfied: {identifiers}. Accessible Domain Match rules: {rules}",
                    check.SecurityPrincipalId,
                    accessible.Count,
                    string.Join(", ", unsatisfied),
                    rules);

                return new ActionResult(detail, false);
            }

            return new ActionResult("Authorized", true);
        }

        /// <summary>
        /// Describe the role assignments a principal actually holds, and the actions those roles grant.
        ///
        /// This state is always a configuration problem, and the denial alone does not distinguish "nothing is
        /// assigned to this principal" from "assigned to a role which no longer exists" and "assigned to a role
        /// whose policies do not grant this action", which are fixed in different places.
        /// </summary>
        private async Task<string> DescribeStoredRoleAssignments(string securityPrincipalId, string requiredActionId)
        {
            try
            {
                var access = await GetCurrentAccessControl();
                var roleStatus = await access.GetSecurityPrincipalRoleStatus(StandardSecurityPrincipals.System, securityPrincipalId);

                var assignments = roleStatus?.AssignedRoles?.ToList() ?? [];

                if (assignments.Count == 0)
                {
                    return $"This principal holds no role assignments at all, so '{requiredActionId}' is granted to a different security principal or was never assigned. "
                        + "For a managed instance, check the role is assigned to the instance's own security principal (Users > Managed Instances).";
                }

                var roles = roleStatus!.Roles?.ToList() ?? [];
                var grantedActions = roleStatus.Policies?.SelectMany(p => p.ResourceActions ?? []).Distinct().ToList() ?? [];

                var described = assignments.Select(a => roles.Exists(r => r.Id == a.RoleId)
                    ? $"'{a.RoleId}' (assignment {a.Id})"
                    : $"'{a.RoleId}' (assignment {a.Id}, NO SUCH ROLE DEFINITION IN THE STORE)");

                return $"Roles assigned to this principal: {string.Join(", ", described)}. "
                    + $"Actions granted by those roles: {(grantedActions.Count == 0 ? "(none)" : string.Join(", ", grantedActions))}. "
                    + $"Required: {requiredActionId}.";
            }
            catch (Exception exp)
            {
                _serviceLog?.Debug($"Could not read role assignments for principal {securityPrincipalId} while reporting a managed challenge denial: {exp.Message}");
                return "The principal's role assignments could not be read.";
            }
        }

        /// <summary>
        /// Summarise the accessible managed challenges and their Domain Match rules, so a matching failure reports
        /// exactly which rules were evaluated.
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

        /// <summary>
        /// Find the best domain match within a set of challenges the caller is already authorised to use.
        /// Callers must supply an access-filtered set (see <see cref="GetAccessibleManagedChallenges(ManagedChallengeAccessScope)"/>).
        /// </summary>
        private static ManagedChallenge? ManagedChallengeFindBestMatch(
            ManagedChallengeRequest request,
            ICollection<ManagedChallenge> accessibleChallenges)
            => ManagedChallengeAccess.FindBestMatch(request, accessibleChallenges);

        /// <summary>
        /// Copy a request for deferred cleanup. Only the request is copied: the identity it was authorized as
        /// travels alongside it, so there is nothing here to remember to carry over.
        /// </summary>
        private static AuthorizedManagedChallengeRequest CloneAuthorizedRequest(AuthorizedManagedChallengeRequest authorized)
        {
            var request = authorized.Request;

            return new AuthorizedManagedChallengeRequest
            {
                Request = new ManagedChallengeRequest
                {
                    ChallengeType = request.ChallengeType,
                    Identifier = request.Identifier,
                    ResponseKey = request.ResponseKey,
                    ResponseValue = request.ResponseValue,
                    DateTimePerformed = request.DateTimePerformed,
                    ManagedCertId = request.ManagedCertId
                },
                Caller = authorized.Caller.Clone()
            };
        }

        /// <summary>
        /// maintain a set of changed challenge requests that we need to ensure get cleaned up later
        /// </summary>
        private ConcurrentDictionary<string, AuthorizedManagedChallengeRequest> _managedChallengesPendingCleanup = [];

        private ConcurrentDictionary<string, byte> _managedChallengesCleanupInProgress = [];

        /// <summary>
        /// A running or completed operation, and the identity it was authorized as.
        /// </summary>
        private sealed record ManagedChallengeOperationState(ManagedChallengeOperation Operation, ManagedChallengeCaller Caller);

        private ConcurrentDictionary<string, ManagedChallengeOperationState> _managedChallengeOperations = [];

        public Task<ManagedChallengeOperation> BeginManagedChallengeRequest(AuthorizedManagedChallengeRequest authorized)
        {
            CleanupExpiredManagedChallengeOperations();

            var cloned = CloneAuthorizedRequest(authorized);

            var operation = new ManagedChallengeOperation
            {
                Request = cloned.Request,
                Caller = cloned.Caller
            };

            _managedChallengeOperations[operation.Id] = new ManagedChallengeOperationState(operation, cloned.Caller);

            _ = RunManagedChallengeOperation(operation.Id);

            return Task.FromResult(operation);
        }

        public Task<ManagedChallengeOperation?> GetManagedChallengeOperation(string operationId)
        {
            _managedChallengeOperations.TryGetValue(operationId, out var state);
            return Task.FromResult(state?.Operation);
        }

        public Task<ActionResult> PerformManagedChallengeRequest(AuthorizedManagedChallengeRequest authorized)
        {
            return ExecuteManagedChallengeRequest(authorized);
        }

        private async Task RunManagedChallengeOperation(string operationId)
        {
            if (!_managedChallengeOperations.TryGetValue(operationId, out var state))
            {
                return;
            }

            var operation = state.Operation;

            try
            {
                operation.Status = ManagedChallengeOperationStates.Running;
                operation.DateStarted = DateTimeOffset.UtcNow;
                operation.DateLastUpdated = operation.DateStarted.Value;

                var result = await ExecuteManagedChallengeRequest(
                    new AuthorizedManagedChallengeRequest { Request = operation.Request, Caller = state.Caller });

                operation.Result = result;
                operation.Status = result.IsSuccess ? ManagedChallengeOperationStates.Succeeded : ManagedChallengeOperationStates.Failed;
                operation.DateCompleted = DateTimeOffset.UtcNow;
                operation.DateLastUpdated = operation.DateCompleted.Value;
            }
            catch (Exception exp)
            {
                _serviceLog?.Error($"Managed Challenge operation failed: {exp}");

                operation.Result = new ActionResult { IsSuccess = false, Message = $"Managed challenge operation failed: {exp.Message}" };
                operation.Status = ManagedChallengeOperationStates.Failed;
                operation.DateCompleted = DateTimeOffset.UtcNow;
                operation.DateLastUpdated = operation.DateCompleted.Value;
            }
        }

        private void CleanupExpiredManagedChallengeOperations()
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-12);

            foreach (var kvp in _managedChallengeOperations)
            {
                if (kvp.Value.Operation.IsCompleted && kvp.Value.Operation.DateLastUpdated < cutoff)
                {
                    _managedChallengeOperations.TryRemove(kvp.Key, out _);
                }
            }
        }

        /// <summary>
        /// The challenges a caller may use for an identifier, having first confirmed they are authorized to.
        ///
        /// Perform and cleanup ask the same question against different actions, so they share this rather than
        /// each resolving the scope themselves. A caller with no security principal is the unscoped local path -
        /// an instance performing its own challenge, with no hub principal to scope by - and every challenge is
        /// eligible for domain matching.
        /// </summary>
        private async Task<(bool IsAuthorized, string? FailureReason, ICollection<ManagedChallenge> Challenges)> ResolveEligibleChallenges(
            ManagedChallengeCaller caller,
            string? identifier,
            string defaultActionId)
        {
            if (string.IsNullOrWhiteSpace(caller?.SecurityPrincipalId))
            {
                return (true, null, await GetManagedChallenges());
            }

            // Managed ACME fulfillment is covered by the order action it was authorized under; a direct managed
            // challenge call is checked against the per-request action.
            var actionId = ManagedChallengeRequestOrigins.GetRequiredResourceAction(caller.Origin, defaultActionId);

            // This is the authorization decision for the request, and the same one every other caller asks, so
            // domain restrictions and role scope are enforced here rather than only in whichever layer happened to
            // be in front. Fulfillment reports a missing challenge on its own, so a match is not required.
            var authorized = await AuthorizeManagedChallengeIdentifiers(new ManagedChallengeAuthorizationCheck
            {
                SecurityPrincipalId = caller.SecurityPrincipalId,
                Identifiers = string.IsNullOrWhiteSpace(identifier) ? [] : [identifier],
                ScopedAssignedRoles = caller.ScopedAssignedRoles,
                RequiredActionId = actionId
            });

            if (!authorized.IsSuccess)
            {
                return (false, authorized.Message, []);
            }

            var accessScope = await GetManagedChallengeAccessScope(caller.SecurityPrincipalId, caller.ScopedAssignedRoles, actionId);

            return (true, null, await GetAccessibleManagedChallenges(accessScope));
        }

        private async Task<ActionResult> ExecuteManagedChallengeRequest(AuthorizedManagedChallengeRequest authorized)
        {
            var log = _serviceLog;
            var request = authorized.Request;
            var caller = authorized.Caller;

            var eligible = await ResolveEligibleChallenges(
                caller,
                request.Identifier,
                StandardResourceActions.ManagedChallengeRequest);

            if (!eligible.IsAuthorized)
            {
                return new ActionResult { IsSuccess = false, Message = eligible.FailureReason };
            }

            var matchingChallenge = ManagedChallengeFindBestMatch(request, eligible.Challenges);

            if (matchingChallenge == null)
            {
                return new ActionResult { IsSuccess = false, Message = "No matching challenge found" };
            }
            else if (matchingChallenge.ChallengeConfig == null)
            {
                return new ActionResult { IsSuccess = false, Message = "Managed challenge configuration is incomplete" };
            }
            else if (!IsManagedChallengeTypeSupported(matchingChallenge.ChallengeConfig.ChallengeType)
                || !IsManagedChallengeTypeSupported(request.ChallengeType))
            {
                return new ActionResult { IsSuccess = false, Message = "Managed challenge only supports dns-01 requests" };
            }
            else
            {
                // perform challenge
                var _dnsHelper = new DnsChallengeHelper(_credentialsManager);

                DnsChallengeHelperResult dnsResult;
                var managedCertificate = new ManagedCertificate
                {
                    RequestConfig = new CertRequestConfig
                    {
                        Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                        [
                           matchingChallenge.ChallengeConfig
                        ])
                    }
                };

                var domain = new CertIdentifierItem { IdentifierType = CertIdentifierType.Dns, Value = request.Identifier };

                dnsResult = await _dnsHelper.CompleteDNSChallenge(log, managedCertificate, domain, request.ResponseKey, request.ResponseValue, isTestMode: false);

                if (!dnsResult.Result.IsSuccess)
                {
                    if (dnsResult.IsAwaitingUser)
                    {
                        log?.Error($"Action Required: {dnsResult.Result.Message}");
                    }
                    else
                    {
                        log?.Error($"DNS update failed: {dnsResult.Result.Message}");
                    }

                    return dnsResult.Result;
                }
                else
                {
                    log.Information($"DNS: {dnsResult.Result.Message}");

                }

                // apply propagation delay
                var propagationSeconds = dnsResult.PropagationSeconds;
                if (propagationSeconds > 0)
                {
                    var propagationDelayMilliseconds = Math.Min((long)propagationSeconds * 1000L, int.MaxValue);

                    if (propagationDelayMilliseconds != (long)propagationSeconds * 1000L)
                    {
                        log?.Warning($"Managed challenge propagation delay of {propagationSeconds} seconds exceeds the supported delay range. Using the maximum supported wait instead.");
                    }

                    await Task.Delay((int)propagationDelayMilliseconds);
                }
                else if (propagationSeconds < 0)
                {
                    log?.Warning($"Managed challenge provider returned an invalid propagation delay of {propagationSeconds} seconds. Skipping propagation wait.");
                }

                request.DateTimePerformed = DateTimeOffset.UtcNow;
                _managedChallengesPendingCleanup[CreateManagedChallengeCleanupKey(request)] = CloneAuthorizedRequest(authorized);

                return new ActionResult { IsSuccess = true, Message = $"Challenge response {request.ChallengeType} completed {request.ResponseKey} : {request.ResponseValue}" };
            }
        }

        public async Task PerformManagedChallengeCleanup(string managedCertId = null)
        {
            try
            {
                if (managedCertId != null)
                {
                    // Process items one by one and keep failed cleanup entries for retry
                    foreach (var kvp in _managedChallengesPendingCleanup)
                    {
                        if (kvp.Value.Request.ManagedCertId == managedCertId &&
                            _managedChallengesCleanupInProgress.TryAdd(kvp.Key, 0))
                        {
                            try
                            {
                                var result = await CleanupManagedChallengeRequest(kvp.Value);
                                if (result.IsSuccess)
                                {
                                    _managedChallengesPendingCleanup.TryRemove(kvp.Key, out _);
                                }
                            }
                            finally
                            {
                                _managedChallengesCleanupInProgress.TryRemove(kvp.Key, out _);
                            }
                        }
                    }
                }
                else
                {
                    var cutoff = DateTimeOffset.UtcNow.AddMinutes(-15);
                    foreach (var kvp in _managedChallengesPendingCleanup)
                    {
                        if (kvp.Value.Request.DateTimePerformed < cutoff &&
                            _managedChallengesCleanupInProgress.TryAdd(kvp.Key, 0))
                        {
                            try
                            {
                                var result = await CleanupManagedChallengeRequest(kvp.Value);
                                if (result.IsSuccess)
                                {
                                    _managedChallengesPendingCleanup.TryRemove(kvp.Key, out _);
                                }
                            }
                            finally
                            {
                                _managedChallengesCleanupInProgress.TryRemove(kvp.Key, out _);
                            }
                        }
                    }
                }
            }
            catch (Exception exp)
            {
                _serviceLog?.Error($"Managed Challenge Cleanup Error. Cleanup will resume later: {exp}");
            }
        }

        public async Task<ActionResult> CleanupManagedChallengeRequest(AuthorizedManagedChallengeRequest authorized)
        {
            var log = _serviceLog;
            var request = authorized.Request;

            var eligible = await ResolveEligibleChallenges(
                authorized.Caller,
                request.Identifier,
                StandardResourceActions.ManagedChallengeCleanup);

            if (!eligible.IsAuthorized)
            {
                return new ActionResult { IsSuccess = false, Message = eligible.FailureReason };
            }

            var matchingChallenge = ManagedChallengeFindBestMatch(request, eligible.Challenges);

            if (matchingChallenge == null)
            {
                return new ActionResult { IsSuccess = false, Message = "No matching challenge found" };
            }
            else if (matchingChallenge.ChallengeConfig == null)
            {
                return new ActionResult { IsSuccess = false, Message = "Managed challenge configuration is incomplete" };
            }
            else if (!IsManagedChallengeTypeSupported(matchingChallenge.ChallengeConfig.ChallengeType)
                || !IsManagedChallengeTypeSupported(request.ChallengeType))
            {
                return new ActionResult { IsSuccess = false, Message = "Managed challenge only supports dns-01 requests" };
            }
            else
            {
                // perform challenge
                var _dnsHelper = new DnsChallengeHelper(_credentialsManager);

                var managedCertificate = new ManagedCertificate
                {
                    RequestConfig = new CertRequestConfig
                    {
                        Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                      new List<CertRequestChallengeConfig>
                      {
                           matchingChallenge.ChallengeConfig
                      })
                    }
                };

                var domain = new CertIdentifierItem { IdentifierType = CertIdentifierType.Dns, Value = request.Identifier };

                var dnsResult = await _dnsHelper.DeleteDNSChallenge(log, managedCertificate, domain, request.ResponseKey, request.ResponseValue);

                if (!dnsResult.Result.IsSuccess)
                {
                    if (dnsResult.IsAwaitingUser)
                    {
                        log?.Error($"Action Required: {dnsResult.Result.Message}");
                    }
                    else
                    {
                        log?.Error($"DNS cleanup failed: {dnsResult.Result.Message}");
                    }

                    return dnsResult.Result;
                }
                else
                {
                    log.Information($"Managed Challenge Cleanup - DNS: {dnsResult.Result.Message}");
                }

                return new ActionResult { IsSuccess = true, Message = $"Challenge cleanup {request.ChallengeType} completed {request.ResponseKey} : {request.ResponseValue}" };

            }
        }
    }
}
