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

                // Check if challenge matches tag scopes
                bool matches;

                if (requireAllTags)
                {
                    // Must match ALL scopes
                    matches = tagScopes.All(scope =>
                        itemTags.Any(t => t.CategoryKey == scope.CategoryKey &&
                            (scope.Value == null || t.Value == scope.Value)));
                }
                else
                {
                    // Must match ANY scope
                    matches = tagScopes.Any(scope =>
                        itemTags.Any(t => t.CategoryKey == scope.CategoryKey &&
                            (scope.Value == null || t.Value == scope.Value)));
                }

                if (matches)
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

        private ManagedChallenge ManagedChallengeFindBestMatch(ManagedChallengeRequest request, ICollection<ManagedChallenge> managedChallenges)
        {
            // find most specific matching challenge for the request - based on ManagedCertificate.GetChallengeConfig
            //TODO: filter based on access
            var matchedConfig = managedChallenges.FirstOrDefault(c => string.IsNullOrEmpty(c.ChallengeConfig?.DomainMatch));

            if (request.Identifier != null && !string.IsNullOrEmpty(request.Identifier))
            {
                // expand configs into per identifier list
                var configsPerDomain = new Dictionary<string, ManagedChallenge>();
                foreach (var managedChallenge in managedChallenges.Where(c => !string.IsNullOrEmpty(c.ChallengeConfig?.DomainMatch)))
                {
                    var c = managedChallenge.ChallengeConfig;
                    if (!string.IsNullOrWhiteSpace(c?.DomainMatch))
                    {
                        var normalizedDomainMatch = c.DomainMatch.Replace(",", ";"); // if user has entered comma separators instead of semicolons, convert now.

                        if (!normalizedDomainMatch.Contains(';'))
                        {
                            var domainMatchKey = normalizedDomainMatch.Trim().ToLowerInvariant();

                            // if identifier key is test.com for example we only support one matching config
                            if (!configsPerDomain.ContainsKey(domainMatchKey))
                            {
                                configsPerDomain.Add(domainMatchKey, managedChallenge);
                            }
                        }
                        else
                        {
                            var domains = normalizedDomainMatch.Split(';');
                            foreach (var d in domains)
                            {
                                if (!string.IsNullOrWhiteSpace(d))
                                {
                                    var domainMatchKey = d.Trim().ToLowerInvariant();
                                    if (!configsPerDomain.ContainsKey(domainMatchKey))
                                    {
                                        configsPerDomain.Add(domainMatchKey, managedChallenge);
                                    }
                                }
                            }
                        }
                    }
                }

                // if exact match exists, use that
                var identifierKey = request.Identifier.ToLowerInvariant() ?? "";
                if (configsPerDomain.TryGetValue(identifierKey, out var value))
                {
                    return value;
                }

                // if explicit wildcard match exists, use that
                if (configsPerDomain.TryGetValue("*." + identifierKey, out var wildValue))
                {
                    return wildValue;
                }

                //if a more specific config matches the identifier, use that, in order of longest identifier name match first
                var allMatchingConfigKeys = configsPerDomain.Keys.OrderByDescending(l => l.Length);

                foreach (var wildcard in allMatchingConfigKeys.Where(k => k.StartsWith("*.", StringComparison.CurrentCultureIgnoreCase)))
                {
                    if (ManagedCertificate.IsDomainOrWildcardMatch([wildcard], request.Identifier))
                    {
                        return configsPerDomain[wildcard];
                    }
                }

                foreach (var configDomain in allMatchingConfigKeys)
                {
                    if (identifierKey.EndsWith($".{configDomain}", StringComparison.CurrentCultureIgnoreCase))
                    {
                        // use longest matching identifier (so subdomain.test.com takes priority
                        // over test.com, )
                        return configsPerDomain[configDomain];
                    }
                }
            }

            // no other matches, just use first
            if (matchedConfig != null)
            {
                return matchedConfig;
            }
            else
            {
                // no match, return null
                return default;
            }
        }

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
                ManagedCertId = request.ManagedCertId
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

            // Get challenges filtered by caller's tag scope
            var managedChallenges = tagScopes?.Any() == true
                ? await GetManagedChallengesWithTagFilter(tagScopes, requireAllTags, includeUntagged: false)
                : await GetManagedChallenges();

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

            var managedChallenges = await GetManagedChallenges();

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
