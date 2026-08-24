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
        /// True when every identifier can be satisfied by a managed challenge accessible to the principal.
        /// </summary>
        public async Task<(bool CanSatisfy, List<string> UnsatisfiedIdentifiers)> CanPrincipalSatisfyManagedChallengeIdentifiers(
            string? securityPrincipalId,
            IEnumerable<string> identifiers,
            ICollection<string>? scopedAssignedRoles = null,
            string requiredActionId = StandardResourceActions.ManagedAcmePerformOrder)
        {
            var accessible = await GetAccessibleManagedChallenges(securityPrincipalId, scopedAssignedRoles, requiredActionId);
            var ok = ManagedChallengeAccess.CanSatisfyIdentifiers(identifiers, accessible, out var unsatisfied);
            return (ok, unsatisfied);
        }

        /// <summary>
        /// Find the best domain match within a set of challenges the caller is already authorised to use.
        /// Callers must supply an access-filtered set (see <see cref="GetAccessibleManagedChallenges(ManagedChallengeAccessScope)"/>).
        /// </summary>
        private static ManagedChallenge? ManagedChallengeFindBestMatch(
            ManagedChallengeRequest request,
            ICollection<ManagedChallenge> accessibleChallenges)
            => ManagedChallengeAccess.FindBestMatch(request, accessibleChallenges);

        private static ManagedChallengeRequest CloneManagedChallengeRequest(ManagedChallengeRequest request)
        {
            return new ManagedChallengeRequest
            {
                ChallengeType = request.ChallengeType,
                Identifier = request.Identifier,
                ResponseKey = request.ResponseKey,
                ResponseValue = request.ResponseValue,
                AuthKey = request.AuthKey,
                AuthSecret = request.AuthSecret,
                DateTimePerformed = request.DateTimePerformed,
                ManagedCertId = request.ManagedCertId,
                SecurityPrincipalId = request.SecurityPrincipalId,
                ScopedAssignedRoles = request.ScopedAssignedRoles?.ToList()
            };
        }

        /// <summary>
        /// maintain a set of changed challenge requests that we need to ensure get cleaned up later
        /// </summary>
        private ConcurrentDictionary<string, ManagedChallengeRequest> _managedChallengesPendingCleanup = [];

        private ConcurrentDictionary<string, byte> _managedChallengesCleanupInProgress = [];

        private ConcurrentDictionary<string, ManagedChallengeOperation> _managedChallengeOperations = [];

        public Task<ManagedChallengeOperation> BeginManagedChallengeRequest(ManagedChallengeRequest request)
        {
            return BeginManagedChallengeRequest(request, tagScopes: null);
        }

        public Task<ManagedChallengeOperation> BeginManagedChallengeRequest(
            ManagedChallengeRequest request,
            ICollection<TagScope>? tagScopes,
            bool requireAllTags = false)
        {
            CleanupExpiredManagedChallengeOperations();

            var operation = new ManagedChallengeOperation
            {
                Request = CloneManagedChallengeRequest(request)
            };

            _managedChallengeOperations[operation.Id] = operation;

            _ = RunManagedChallengeOperation(operation.Id, tagScopes, requireAllTags);

            return Task.FromResult(operation);
        }

        public Task<ManagedChallengeOperation?> GetManagedChallengeOperation(string operationId)
        {
            _managedChallengeOperations.TryGetValue(operationId, out var operation);
            return Task.FromResult(operation);
        }

        public Task<ActionResult> PerformManagedChallengeRequest(ManagedChallengeRequest request)
        {
            return ExecuteManagedChallengeRequest(request, tagScopes: null);
        }

        /// <summary>
        /// Perform a managed challenge request with tag-based access control
        /// </summary>
        /// <param name="request">The challenge request details</param>
        /// <param name="tagScopes">Tag scopes the caller is authorized for. If null, no tag filtering applied.</param>
        /// <param name="requireAllTags">If true, challenge must match ALL tag scopes</param>
        /// <returns>Result of the challenge operation</returns>
        public Task<ActionResult> PerformManagedChallengeRequest(
            ManagedChallengeRequest request,
            ICollection<TagScope>? tagScopes,
            bool requireAllTags = false)
        {
            return ExecuteManagedChallengeRequest(request, tagScopes, requireAllTags);
        }

        private async Task RunManagedChallengeOperation(string operationId, ICollection<TagScope>? tagScopes, bool requireAllTags)
        {
            if (!_managedChallengeOperations.TryGetValue(operationId, out var operation))
            {
                return;
            }

            try
            {
                operation.Status = ManagedChallengeOperationStates.Running;
                operation.DateStarted = DateTimeOffset.UtcNow;
                operation.DateLastUpdated = operation.DateStarted.Value;

                var result = await ExecuteManagedChallengeRequest(operation.Request, tagScopes, requireAllTags);

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
                if (kvp.Value.IsCompleted && kvp.Value.DateLastUpdated < cutoff)
                {
                    _managedChallengeOperations.TryRemove(kvp.Key, out _);
                }
            }
        }

        private async Task<ActionResult> ExecuteManagedChallengeRequest(
            ManagedChallengeRequest request,
            ICollection<TagScope>? tagScopes,
            bool requireAllTags = false)
        {
            var log = _serviceLog;

            ICollection<ManagedChallenge> managedChallenges;

            if (!string.IsNullOrWhiteSpace(request.SecurityPrincipalId))
            {
                // Prefer principal-based scope resolution (Managed ACME / scoped consumer roles).
                // Managed ACME fulfillment uses ManagedAcmePerformOrder; external API consumers use ManagedChallengeRequest.
                var actionId = !string.IsNullOrWhiteSpace(request.AuthKey)
                    ? StandardResourceActions.ManagedChallengeRequest
                    : StandardResourceActions.ManagedAcmePerformOrder;

                var accessScope = await GetManagedChallengeAccessScope(
                    request.SecurityPrincipalId,
                    request.ScopedAssignedRoles,
                    actionId);

                if (!accessScope.HasAccess)
                {
                    return new ActionResult { IsSuccess = false, Message = "Security principal is not authorised to use managed challenges" };
                }

                managedChallenges = await GetAccessibleManagedChallenges(accessScope);
            }
            else if (tagScopes?.Any() == true)
            {
                // Explicit tag scopes from callers that already resolved token scopes.
                var includeUntagged = (await GetHubSettings()).ManagedChallenge.AllowUnscopedForScopedPrincipals;
                managedChallenges = await GetManagedChallengesWithTagFilter(tagScopes, requireAllTags, includeUntagged);
            }
            else
            {
                // Unscoped/system path - all challenges eligible for domain matching.
                managedChallenges = await GetManagedChallenges();
            }

            var matchingChallenge = ManagedChallengeFindBestMatch(request, managedChallenges);

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
                _managedChallengesPendingCleanup[CreateManagedChallengeCleanupKey(request)] = CloneManagedChallengeRequest(request);

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
                        if (kvp.Value.ManagedCertId == managedCertId &&
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
                        if (kvp.Value.DateTimePerformed < cutoff &&
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

        public async Task<ActionResult> CleanupManagedChallengeRequest(ManagedChallengeRequest request)
        {
            var log = _serviceLog;

            ICollection<ManagedChallenge> managedChallenges;

            if (!string.IsNullOrWhiteSpace(request.SecurityPrincipalId))
            {
                var actionId = !string.IsNullOrWhiteSpace(request.AuthKey)
                    ? StandardResourceActions.ManagedChallengeCleanup
                    : StandardResourceActions.ManagedAcmePerformOrder;

                var accessScope = await GetManagedChallengeAccessScope(
                    request.SecurityPrincipalId,
                    request.ScopedAssignedRoles,
                    actionId);

                if (!accessScope.HasAccess)
                {
                    return new ActionResult { IsSuccess = false, Message = "Security principal is not authorised to cleanup managed challenges" };
                }

                managedChallenges = await GetAccessibleManagedChallenges(accessScope);
            }
            else
            {
                managedChallenges = await GetManagedChallenges();
            }

            var matchingChallenge = ManagedChallengeFindBestMatch(request, managedChallenges);

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
