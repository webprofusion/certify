using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Shared;
using Certify.SharedUtils;
using Newtonsoft.Json;

namespace Certify.Core.Management.Challenges
{

    public class ManagedDnsChallengeAuto : IDnsProvider, IDnsProviderProviderPlugin
    {
        int IDnsProvider.PropagationDelaySeconds => Definition.PropagationDelaySeconds;

        string IDnsProvider.ProviderId => Definition.Id;

        string IDnsProvider.ProviderTitle => Definition.Title;

        string IDnsProvider.ProviderDescription => Definition.Description;

        string IDnsProvider.ProviderHelpUrl => Definition.HelpUrl;

        bool IDnsProvider.IsTestModeSupported => Definition.IsTestModeSupported;

        List<ProviderParameter> IDnsProvider.ProviderParameters => Definition.ProviderParameters;

        private ILog _log;

        public static ChallengeProviderDefinition Definition => new ChallengeProviderDefinition
        {
            Id = "DNS01.ManagedChallengeHub",
            Title = "(Use Managed Challenge)",
            Description = "Use the currently defined Managed Challenges for automated DNS challenge responses.",
            ProviderParameters = [],
            HelpUrl = "https://docs.certifytheweb.com/",
            PropagationDelaySeconds = 60,
            IsTestModeSupported = false,
            ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
            HandlerType = ChallengeHandlerType.INTERNAL
        };

        public ManagedDnsChallengeAuto()
        {
        }

        public Task<bool> InitProvider(Dictionary<string, string> credentials, Dictionary<string, string> parameters, IHttpClientProvider clientProvider, ILog log = null) => Task.FromResult(true);
        public Task<ActionResult> Test() => Task.FromResult(new ActionResult { IsSuccess = true, Message = $"{Definition.Title} provider currently does not support tests." });
        public Task<ActionResult> CreateRecord(DnsRecord request) => Task.FromResult(new ActionResult { IsSuccess = true, Message = $"{Definition.Title} provider currently does not support tests." });
        public Task<ActionResult> DeleteRecord(DnsRecord request) => Task.FromResult(new ActionResult { IsSuccess = true, Message = $"{Definition.Title} provider currently does not support tests." });
        public Task<List<DnsZone>> GetZones()
        {
            return Task.FromResult(new List<DnsZone>());
        }

        public List<ChallengeProviderDefinition> GetProviders(Type pluginType)
        {
            return new List<ChallengeProviderDefinition> { Definition };
        }
        public IDnsProvider GetProvider(Type pluginType, string id)
        {
            return new ManagedDnsChallengeAuto();
        }
    }

    public struct DnsChallengeHelperResult
    {
        public DnsChallengeHelperResult(ActionResult result)
        {
            Result = result;
            IsAwaitingUser = false;
            PropagationSeconds = 0;
            Provider = null;
        }

        public DnsChallengeHelperResult(string failureMsg)
        {
            Result = new ActionResult(failureMsg, isSuccess: false);
            IsAwaitingUser = false;
            PropagationSeconds = 0;
            Provider = null;
        }

        public ActionResult Result;
        public int PropagationSeconds;
        public bool IsAwaitingUser;
        public IDnsProvider Provider;
    }

    public class DnsChallengeHelper
    {
        private const string CertifyManagedDnsProviderId = "DNS01.API.CertifyManaged";

        private readonly IdnMapping _idnMapping = new IdnMapping();
        private readonly ICredentialsManager _credentialsManager;
        public DnsChallengeHelper(ICredentialsManager credentialsManager)
        {
            _credentialsManager = credentialsManager;
        }
        public async Task<DnsChallengeHelperResult> GetDnsProvider(string providerTypeId, string credentialId, Dictionary<string, string> parameters, ICredentialsManager credentialsManager, ILog log = null)
        {
            var credentials = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(credentialId))
            {
                var failureResult = new DnsChallengeHelperResult(
                    failureMsg: "DNS Challenge API Credentials could not be decrypted or no longer exists. The original user must be used for decryption."
                    );

                // decode credentials string array
                try
                {
                    credentials = await credentialsManager.GetUnlockedCredentialsDictionary(credentialId);
                    if (credentials == null)
                    {
                        return failureResult;
                    }
                }
                catch (Exception exp)
                {
                    log?.Error(exp, $"The required stored credential {credentialId} could not be found or could not be decrypted.");
                    return failureResult;
                }
            }

            IDnsProvider dnsAPIProvider;
            try
            {
                dnsAPIProvider = await ChallengeProviders.GetDnsProvider(providerTypeId, credentials, parameters, log);
            }
            catch (ChallengeProviders.CredentialsRequiredException)
            {
                return new DnsChallengeHelperResult(failureMsg: "This DNS Challenge API requires one or more credentials to be specified.");
            }
            catch (Exception exp)
            {
                return new DnsChallengeHelperResult(
                    failureMsg: $"DNS Challenge API Provider could not be created. Check all required credentials are set and software dependencies installed. {exp.ToString()}"
                    );
            }

            if (dnsAPIProvider == null)
            {
                return new DnsChallengeHelperResult(failureMsg: "DNS Challenge API Provider not set or could not load.");
            }

            return new DnsChallengeHelperResult
            {
                Result = new ActionResult { IsSuccess = true, Message = "Create Provider Instance" },
                Provider = dnsAPIProvider
            };
        }

        private Dictionary<string, IDnsProvider> _dnsProviderCache = new Dictionary<string, IDnsProvider>();
        private bool _useDnsProviderCaching = false;

        /// <summary>
        /// Gets optionally cached DNS provider instance, caching may be based credentials/parameters to allow for zone query caching. TODO: log context will be first caller instead of current
        /// </summary>
        /// <param name="log"></param>
        /// <param name="challengeProvider"></param>
        /// <param name="credentials"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        private async Task<IDnsProvider> GetDnsProvider(ILog log, string challengeProvider, Dictionary<string, string> credentials, Dictionary<string, string> parameters)
        {

            IDnsProvider dnsAPIProvider = null;

            if (_useDnsProviderCaching)
            {
                // construct basic cache key for dns provider and credentials combo
                var providerCacheKey = challengeProvider + (challengeProvider + JsonConvert.SerializeObject(credentials ?? new Dictionary<string, string>()) + JsonConvert.SerializeObject(parameters ?? new Dictionary<string, string>())).GetHashCode().ToString();
                if (_dnsProviderCache.ContainsKey(providerCacheKey))
                {
                    log.Warning("Developer Note: DNS provider log context will be first caller instead of current");

                    dnsAPIProvider = _dnsProviderCache[providerCacheKey];
                }
                else
                {
                    dnsAPIProvider = await ChallengeProviders.GetDnsProvider(challengeProvider, credentials, parameters, log);
                    _dnsProviderCache.Add(providerCacheKey, dnsAPIProvider);
                }
            }
            else
            {
                dnsAPIProvider = await ChallengeProviders.GetDnsProvider(challengeProvider, credentials, parameters, log);
            }

            return dnsAPIProvider;
        }

        private async Task<Dictionary<string, string>> ApplyDefaultManagedChallengeHubCredentials(string challengeProvider, Dictionary<string, string> credentials, Dictionary<string, string> parameters, ILog log)
        {
            credentials ??= new Dictionary<string, string>();

            if (challengeProvider != CertifyManagedDnsProviderId)
            {
                return credentials;
            }

            if (credentials.TryGetValue("authkey", out var authKey) && !string.IsNullOrWhiteSpace(authKey)
                && credentials.TryGetValue("authsecret", out var authSecret) && !string.IsNullOrWhiteSpace(authSecret))
            {
                return credentials;
            }

            var serviceConfig = ServiceConfigManager.GetAppServiceConfig();
            var currentHubApi = serviceConfig?.ManagementServerHubAPI?.Trim().TrimEnd('/');

            if (string.IsNullOrWhiteSpace(currentHubApi))
            {
                return credentials;
            }

            var requestedHubApi = parameters != null && parameters.TryGetValue("api", out var apiValue)
                ? apiValue?.Trim().TrimEnd('/')
                : null;

            if (!string.IsNullOrWhiteSpace(requestedHubApi)
                && !string.Equals(requestedHubApi, currentHubApi, StringComparison.OrdinalIgnoreCase))
            {
                return credentials;
            }

            ClientSecret? joiningSecret = null;

            var envClientId = Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB_CLIENT_ID");
            var envClientSecret = Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB_CLIENT_SECRET");
            if (!string.IsNullOrWhiteSpace(envClientId) && !string.IsNullOrWhiteSpace(envClientSecret))
            {
                joiningSecret = new ClientSecret
                {
                    ClientId = envClientId,
                    Secret = envClientSecret
                };
            }

            if (joiningSecret == null)
            {
                try
                {
                    var storedSecret = await _credentialsManager.GetUnlockedCredential(CertifyManager.MgmtHubJoiningCredId);
                    if (!string.IsNullOrWhiteSpace(storedSecret))
                    {
                        joiningSecret = System.Text.Json.JsonSerializer.Deserialize<ClientSecret>(storedSecret, JsonOptions.DefaultJsonSerializerOptions);
                    }
                }
                catch (Exception exp)
                {
                    log?.Error(exp, "Failed to resolve default Management Hub joining credentials for managed DNS challenge.");
                }
            }

            if (joiningSecret == null || string.IsNullOrWhiteSpace(joiningSecret.ClientId) || string.IsNullOrWhiteSpace(joiningSecret.Secret))
            {
                return credentials;
            }

            credentials["authkey"] = joiningSecret.ClientId;
            credentials["authsecret"] = joiningSecret.Secret;

            if (!string.IsNullOrWhiteSpace(serviceConfig?.HubAssignedInstanceId))
            {
                parameters ??= new Dictionary<string, string>();
                parameters["hubassignedinstanceid"] = serviceConfig.HubAssignedInstanceId;

                try
                {
                    var requestAuthSecret = await _credentialsManager.GetUnlockedCredential(CertifyManager.MgmtHubRequestAuthSecretCredId);
                    if (!string.IsNullOrWhiteSpace(requestAuthSecret))
                    {
                        parameters["hubrequestauthsecret"] = requestAuthSecret;
                    }
                }
                catch (Exception exp)
                {
                    log?.Error(exp, "Failed to resolve Management Hub request auth secret for managed DNS challenge.");
                }
            }

            log?.Information("DNS: Using default Management Hub joining credentials for Certify Managed Challenge API on the current hub.");

            return credentials;
        }

        private static string RedactCredentialValues(string message, Dictionary<string, string> credentials)
        {
            if (string.IsNullOrWhiteSpace(message) || credentials == null || credentials.Count == 0)
            {
                return message ?? string.Empty;
            }

            var redacted = message;

            foreach (var credential in credentials)
            {
                if (!string.IsNullOrWhiteSpace(credential.Value))
                {
                    redacted = redacted.Replace(credential.Value, "[redacted]");
                }
            }

            return redacted;
        }

        private DnsChallengeHelperResult CreateSafeFailureResult(string failureMsg, Dictionary<string, string> credentials)
        {
            return new DnsChallengeHelperResult(failureMsg: RedactCredentialValues(failureMsg, credentials));
        }

        private ActionResult SanitizeResultMessage(ActionResult result, Dictionary<string, string> credentials)
        {
            if (result != null)
            {
                result.Message = RedactCredentialValues(result.Message, credentials);
            }

            return result;
        }

        public async Task<DnsChallengeHelperResult> CompleteDNSChallenge(ILog log, ManagedCertificate managedcertificate, CertIdentifierItem domain, string txtRecordName, string txtRecordValue, bool isTestMode)
        {
            // for a given managed site configuration, attempt to complete the required challenge by
            // creating the required TXT record

            var credentials = new Dictionary<string, string>();

            IDnsProvider dnsAPIProvider = null;

            var challengeConfig = managedcertificate.GetChallengeConfig(domain);

            if (!string.IsNullOrEmpty(challengeConfig.ChallengeCredentialKey))
            {
                // decode credentials string array
                try
                {
                    credentials = await _credentialsManager.GetUnlockedCredentialsDictionary(challengeConfig.ChallengeCredentialKey);
                }
                catch (Exception)
                {
                    return new DnsChallengeHelperResult(failureMsg: "DNS Challenge API Credentials could not be decrypted. The original user must be used for decryption.");
                }
            }

            var parameters = new Dictionary<string, string>();
            if (challengeConfig.Parameters != null)
            {
                foreach (var p in challengeConfig.Parameters)
                {
                    parameters.Add(p.Key, p.Value);
                }
            }

            credentials = await ApplyDefaultManagedChallengeHubCredentials(challengeConfig.ChallengeProvider, credentials, parameters, log);

            try
            {
                dnsAPIProvider = await GetDnsProvider(log, challengeConfig.ChallengeProvider, credentials, parameters);
            }
            catch (ChallengeProviders.CredentialsRequiredException)
            {
                return new DnsChallengeHelperResult("This DNS Challenge API requires one or more credentials to be specified.");
            }
            catch (Exception exp)
            {
                return CreateSafeFailureResult($"DNS Challenge API Provider could not be created. Check all required credentials are set. {exp}", credentials);
            }

            if (dnsAPIProvider == null)
            {
                return new DnsChallengeHelperResult("DNS Challenge API Provider not set or not recognised. Select an API to proceed.");
            }

            string zoneId = null;
            if (parameters != null && parameters.ContainsKey("zoneid"))
            {
                zoneId = parameters["zoneid"]?.Trim();
            }
            else
            {
#pragma warning disable CS0618 // Type or member is obsolete
                zoneId = challengeConfig.ZoneId?.Trim();
#pragma warning restore CS0618 // Type or member is obsolete
            }

            //most DNS providers require domains to by ASCII
            txtRecordName = _idnMapping.GetAscii(txtRecordName).ToLower().Trim();

            if (!string.IsNullOrEmpty(challengeConfig.ChallengeDelegationRule))
            {
                var delegatedTxtRecordName = ApplyChallengeDelegationRule(domain.Value, txtRecordName, challengeConfig.ChallengeDelegationRule);
                log.Information($"DNS: Challenge Delegation Domain enabled, using {delegatedTxtRecordName} in place of {txtRecordName}.");

                txtRecordName = delegatedTxtRecordName;
            }

            log.Information($"DNS: Creating TXT Record '{txtRecordName}' with value '{txtRecordValue}', [{domain.Value}] {(zoneId != null ? $"in ZoneId '{zoneId}'" : "")} using API provider '{dnsAPIProvider.ProviderTitle}'");
            try
            {
                var result = await dnsAPIProvider.CreateRecord(new DnsRecord
                {
                    RecordType = "TXT",
                    TargetDomainName = domain.Value.Trim(),
                    RecordName = txtRecordName,
                    RecordValue = txtRecordValue,
                    ZoneId = zoneId
                });

                result = SanitizeResultMessage(result, credentials);
                result.Message = $"{dnsAPIProvider.ProviderTitle} :: {result.Message}";

                var isAwaitingUser = false;

                if (challengeConfig.ChallengeProvider.Contains(".Manual") || result.Message.Contains("[Action Required]"))
                {
                    isAwaitingUser = true;
                }

                return new DnsChallengeHelperResult
                {
                    Result = result,
                    PropagationSeconds = dnsAPIProvider.PropagationDelaySeconds,
                    IsAwaitingUser = isAwaitingUser
                };
            }
            catch (Exception exp)
            {
                return CreateSafeFailureResult($"Failed [{dnsAPIProvider.ProviderTitle}]: {exp}", credentials);
            }
        }

        /// <summary>
        /// For a given identifier (domain) and source TXT record name, apply rule *.source.domain:*.delegate.domain to return new TXT record fully qualified name
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="sourceChallengeTXTRecordName"></param>
        /// <param name="challengeDelegationRule"></param>
        /// <returns></returns>
        public static string ApplyChallengeDelegationRule(string identifier, string sourceChallengeTXTRecordName, string challengeDelegationRule)
        {
            if (challengeDelegationRule == null)
            {
                return sourceChallengeTXTRecordName;
            }

            var rules = challengeDelegationRule.Split(';');
            foreach (var r in rules)
            {
                if (!string.IsNullOrWhiteSpace(r))
                {
                    // rule format is sourceDomain:targetDomain (one to one), *.sourceDomain:*.targetDomain (many to many) or *.sourceDomain:targetDomain (many to one)

                    var ruleComponents = r.Split(':');
                    if (ruleComponents.Length == 2)
                    {
                        var ruleSourceDomain = ruleComponents[0].ToLower().Trim();
                        var ruleTargetDomain = ruleComponents[1].ToLower().Trim();

                        // if rule source domain matches our domain identifier, apply this rule
                        if (identifier == ruleSourceDomain || (ruleSourceDomain.StartsWith("*.") && identifier.EndsWith(ruleSourceDomain.Replace("*.", ""))))
                        {
                            // if wildcard rule matches on both sides, substitute record name value, e.g.  _acme-challenge.www.[test.com] becomes _acme-challenge.www.[auth.exmaple.com]

                            if (ruleTargetDomain.StartsWith("*.") && identifier.EndsWith(ruleSourceDomain.Replace("*.", "")))
                            {
                                return sourceChallengeTXTRecordName.Replace(ruleSourceDomain.Replace("*.", ""), ruleTargetDomain.Replace("*.", ""));

                            }
                            else if (!ruleTargetDomain.StartsWith("*."))
                            {
                                // non wildcard substitution, all source variants point to same level
                                // eg. _acme-challenge.[test.com] and _acme-challenge.[www.test.com] point directly to _acme-challenge.[auth.example.com]
                                var recordName = sourceChallengeTXTRecordName.Split('.')[0];
                                return $"{recordName}.{ruleTargetDomain}";
                            }
                        }
                    }
                }
            }

            // no match, fallback to original
            return sourceChallengeTXTRecordName;
        }

        public async Task<DnsChallengeHelperResult> DeleteDNSChallenge(ILog log, ManagedCertificate managedcertificate, CertIdentifierItem domain, string txtRecordName, string txtRecordValue)
        {
            // for a given managed site configuration, attempt to delete the TXT record created for
            // the challenge

            var credentials = new Dictionary<string, string>();

            IDnsProvider dnsAPIProvider = null;

            var challengeConfig = managedcertificate.GetChallengeConfig(domain);

            if (challengeConfig == null || challengeConfig.ChallengeProvider == null)
            {
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = true, Message = $"The DNS record {txtRecordName} can now be removed." },
                    PropagationSeconds = 0,
                    IsAwaitingUser = false
                };
            }

            if (challengeConfig.ChallengeProvider.Contains(".Manual"))
            {
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = true, Message = $"The DNS record {txtRecordName} can now be removed." },
                    PropagationSeconds = 0,
                    IsAwaitingUser = true
                };
            }

            if (!string.IsNullOrEmpty(challengeConfig.ChallengeCredentialKey))
            {
                // decode credentials string array
                try
                {
                    credentials = await _credentialsManager.GetUnlockedCredentialsDictionary(challengeConfig.ChallengeCredentialKey);
                }
                catch (Exception)
                {
                    return new DnsChallengeHelperResult
                    {
                        Result = new ActionResult { IsSuccess = false, Message = "DNS Challenge API Credentials could not be decrypted. The original user must be used for decryption." },
                        PropagationSeconds = 0,
                        IsAwaitingUser = false
                    };
                }
            }

            var parameters = new Dictionary<string, string>();
            if (challengeConfig.Parameters != null)
            {
                foreach (var p in challengeConfig.Parameters)
                {
                    parameters.Add(p.Key, p.Value);
                }
            }

            credentials = await ApplyDefaultManagedChallengeHubCredentials(challengeConfig.ChallengeProvider, credentials, parameters, log);

            try
            {
                dnsAPIProvider = await GetDnsProvider(log, challengeConfig.ChallengeProvider, credentials, parameters);
            }
            catch (ChallengeProviders.CredentialsRequiredException)
            {
                return new DnsChallengeHelperResult("This DNS Challenge API requires one or more credentials to be specified.");
            }
            catch (Exception exp)
            {
                return CreateSafeFailureResult($"DNS Challenge API Provider could not be created. Check all required credentials are set. {exp}", credentials);
            }

            if (dnsAPIProvider == null)
            {
                return new DnsChallengeHelperResult(failureMsg: "DNS Challenge API Provider not set or not recognised. Select an API to proceed.");
            }

            string zoneId = null;
            if (parameters != null && parameters.ContainsKey("zoneid"))
            {
                zoneId = parameters["zoneid"]?.Trim();
            }
            else
            {
#pragma warning disable CS0618 // Type or member is obsolete
                zoneId = challengeConfig.ZoneId?.Trim();
#pragma warning restore CS0618 // Type or member is obsolete
            }

            if (dnsAPIProvider != null)
            {
                //most DNS providers require domains to by ASCII
                txtRecordName = _idnMapping.GetAscii(txtRecordName).ToLower().Trim();

                if (!string.IsNullOrEmpty(challengeConfig.ChallengeDelegationRule))
                {
                    var delegatedTXTRecordName = ApplyChallengeDelegationRule(domain.Value, txtRecordName, challengeConfig.ChallengeDelegationRule);
                    log.Information($"DNS: Challenge Delegation Domain enabled, using {delegatedTXTRecordName} in place of {txtRecordName}.");

                    txtRecordName = delegatedTXTRecordName;
                }

                log.Information($"DNS: Deleting TXT Record '{txtRecordName}' :'{txtRecordValue}', [{domain.Value}] {(zoneId != null ? $"in ZoneId '{zoneId}'" : "")} using API provider '{dnsAPIProvider.ProviderTitle}'");
                try
                {
                    var result = await dnsAPIProvider.DeleteRecord(new DnsRecord
                    {
                        RecordType = "TXT",
                        TargetDomainName = domain.Value,
                        RecordName = txtRecordName,
                        RecordValue = txtRecordValue,
                        ZoneId = zoneId
                    });

                    result = SanitizeResultMessage(result, credentials);
                    result.Message = $"{dnsAPIProvider.ProviderTitle} :: {result.Message}";

                    return new DnsChallengeHelperResult
                    {
                        Result = result,
                        PropagationSeconds = dnsAPIProvider.PropagationDelaySeconds,
                        IsAwaitingUser = challengeConfig.ChallengeProvider.Contains(".Manual")
                    };
                }
                catch (Exception exp)
                {
                    return CreateSafeFailureResult($"Failed [{dnsAPIProvider.ProviderTitle}]: {exp}", credentials);
                }
            }
            else
            {
                return new DnsChallengeHelperResult(failureMsg: "Error: Could not determine DNS API Provider.");
            }
        }
    }
}
