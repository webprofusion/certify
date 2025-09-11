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
        public async Task<ICollection<ManagedChallenge>> GetManagedChallenges()
        {
            return await _configStore.GetItems<ManagedChallenge>(nameof(ManagedChallenge));
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

            return new ActionResult { IsSuccess = deleted };
        }

        private ManagedChallenge ManagedChallengeFindBestMatch(ManagedChallengeRequest request, ICollection<ManagedChallenge> managedChallenges)
        {
            // find most specific matching challenge for the request - based on ManagedCertificate.GetChallengeConfig
            //TODO: filter based on access
            var matchedConfig = managedChallenges.FirstOrDefault(c => string.IsNullOrEmpty(c.ChallengeConfig.DomainMatch));

            if (request.Identifier != null && !string.IsNullOrEmpty(request.Identifier))
            {
                // expand configs into per identifier list
                var configsPerDomain = new Dictionary<string, ManagedChallenge>();
                foreach (var managedChallenge in managedChallenges.Where(c => !string.IsNullOrEmpty(c.ChallengeConfig.DomainMatch)))
                {
                    var c = managedChallenge.ChallengeConfig;
                    if (c != null)
                    {
                        if (c.DomainMatch != null && !string.IsNullOrEmpty(c.DomainMatch))
                        {
                            c.DomainMatch = c.DomainMatch.Replace(",", ";"); // if user has entered comma separators instead of semicolons, convert now.

                            if (!c.DomainMatch.Contains(';'))
                            {
                                var domainMatchKey = c.DomainMatch.Trim();

                                // if identifier key is test.com for example we only support one matching config
                                if (!configsPerDomain.ContainsKey(domainMatchKey))
                                {
                                    configsPerDomain.Add(domainMatchKey, managedChallenge);
                                }
                            }
                            else
                            {
                                var domains = c.DomainMatch.Split(';');
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
                    if (configDomain.EndsWith(request.Identifier.ToLowerInvariant(), StringComparison.CurrentCultureIgnoreCase))
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

        /// <summary>
        /// maintain a set of changed challenge requests that we need to ensure get cleaned up later
        /// </summary>
        private ConcurrentDictionary<string, ManagedChallengeRequest> _managedChallengesPendingCleanup = [];

        public async Task<ActionResult> PerformManagedChallengeRequest(ManagedChallengeRequest request)
        {
            var log = _serviceLog;

            var managedChallenges = await GetManagedChallenges();

            var matchingChallenge = ManagedChallengeFindBestMatch(request, managedChallenges);

            if (matchingChallenge == null)
            {
                return new ActionResult { IsSuccess = false, Message = "No matching challenge found" };
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
                await Task.Delay(dnsResult.PropagationSeconds * 1000);

                request.DateTimePerformed = DateTimeOffset.UtcNow;
                _managedChallengesPendingCleanup.TryAdd($"{request.ResponseKey}:{request.ResponseValue}", request);

                return new ActionResult { IsSuccess = true, Message = $"Challenge response {request.ChallengeType} completed {request.ResponseKey} : {request.ResponseValue}" };
            }
        }

        public async Task PerformManagedChallengeCleanup(string managedCertId = null)
        {
            try
            {
                if (managedCertId != null)
                {
                    // Process items one by one to avoid race conditions
                    foreach (var kvp in _managedChallengesPendingCleanup)
                    {
                        if (kvp.Value.ManagedCertId == managedCertId &&
                            _managedChallengesPendingCleanup.TryRemove(kvp.Key, out var request))
                        {
                            await CleanupManagedChallengeRequest(request);
                        }
                    }
                }
                else
                {
                    var cutoff = DateTimeOffset.UtcNow.AddMinutes(-15);
                    foreach (var kvp in _managedChallengesPendingCleanup)
                    {
                        if (kvp.Value.DateTimePerformed < cutoff &&
                            _managedChallengesPendingCleanup.TryRemove(kvp.Key, out var request))
                        {
                            await CleanupManagedChallengeRequest(request);
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
