using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Core.Management.Challenges
{
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
        private readonly IdnMapping _idnMapping = new IdnMapping();
        private readonly ICredentialsManager _credentialsManager;
        public DnsChallengeHelper(ICredentialsManager credentialsManager)
        {
            _credentialsManager = credentialsManager;
        }
        public async Task<DnsChallengeHelperResult> GetDnsProvider(string providerTypeId, string credentialsId, Dictionary<string, string> parameters, ICredentialsManager credentialsManager, ILog log = null)
        {
            var credentials = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(credentialsId))
            {
                var failureResult = new DnsChallengeHelperResult(
                    failureMsg: "DNS Challenge API Credentials could not be decrypted or no longer exists. The original user must be used for decryption."
                    );

                // decode credentials string array
                try
                {
                    credentials = await credentialsManager.GetUnlockedCredentialsDictionary(credentialsId);
                    if (credentials == null)
                    {
                        return failureResult;
                    }
                }
                catch (Exception exp)
                {
                    log?.Error(exp, $"The required stored credential {credentialsId} could not be found or could not be decrypted.");
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

        public async Task<DnsChallengeHelperResult> CompleteDNSChallenge(ILog log, ManagedCertificate managedcertificate, CertIdentifierItem domain, string txtRecordName, string txtRecordValue, bool isTestMode)
        {
            // for a given managed site configuration, attempt to complete the required challenge by
            // creating the required TXT record

            var credentials = new Dictionary<string, string>();

            IDnsProvider dnsAPIProvider = null;

            var challengeConfig = managedcertificate.GetChallengeConfig(domain);

            /*if (String.IsNullOrEmpty(challengeConfig.ZoneId))
            {
                return new ActionResult { IsSuccess = false, Message = "DNS Challenge Zone Id not set. Set the Zone Id to proceed." };
            }*/

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

            try
            {
                dnsAPIProvider = await ChallengeProviders.GetDnsProvider(challengeConfig.ChallengeProvider, credentials, parameters, log);
            }
            catch (ChallengeProviders.CredentialsRequiredException)
            {
                return new DnsChallengeHelperResult("This DNS Challenge API requires one or more credentials to be specified.");
            }
            catch (Exception exp)
            {
                return new DnsChallengeHelperResult($"DNS Challenge API Provider could not be created. Check all required credentials are set. {exp.ToString()}");
            }

            if (dnsAPIProvider == null)
            {
                return new DnsChallengeHelperResult("DNS Challenge API Provider not set or not recognised. Select an API to proceed.");
            }

            if (isTestMode && !dnsAPIProvider.IsTestModeSupported)
            {
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = true, Message = dnsAPIProvider.ProviderTitle + " does not perform any tests." },
                    PropagationSeconds = 0,
                    IsAwaitingUser = false
                };
            }

            string zoneId = null;
            if (parameters != null && parameters.ContainsKey("zoneid"))
            {
                zoneId = parameters["zoneid"]?.Trim();
            }
            else
            {
                zoneId = challengeConfig.ZoneId?.Trim();
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
                    return new DnsChallengeHelperResult(failureMsg: $"Failed [{dnsAPIProvider.ProviderTitle}]: {exp}");
                }

                //TODO: DNS query to check for new record
                /*
                if (result.IsSuccess)
                {
                    // do our own txt record query before proceeding with challenge completion

                    int attempts = 3;
                    bool recordCheckedOK = false;
                    var networkUtil = new NetworkUtils(false);

                    while (attempts > 0 && !recordCheckedOK)
                    {
                        recordCheckedOK = networkUtil.CheckDNSRecordTXT(domain, txtRecordName, txtRecordValue);
                        attempts--;
                        if (!recordCheckedOK)
                        {
                            await Task.Delay(1000); // hold on a sec
                        }
                    }

                // wait for provider specific propogation delay

                // FIXME: perform validation check in DNS nameservers await
                // Task.Delay(dnsAPIProvider.PropagationDelaySeconds * 1000);

                return result;
            }
            else
            {
                return result;
            }
          */
            }
            else
            {
                return new DnsChallengeHelperResult(failureMsg: "Error: Could not determine DNS API Provider.");
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

            try
            {
                dnsAPIProvider = await ChallengeProviders.GetDnsProvider(challengeConfig.ChallengeProvider, credentials, parameters);
            }
            catch (ChallengeProviders.CredentialsRequiredException)
            {
                return new DnsChallengeHelperResult(failureMsg: "This DNS Challenge API requires one or more credentials to be specified.");
            }
            catch (Exception exp)
            {
                return new DnsChallengeHelperResult(failureMsg: $"DNS Challenge API Provider could not be created. Check all required credentials are set. {exp.ToString()}");
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
                zoneId = challengeConfig.ZoneId?.Trim();
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
                    return new DnsChallengeHelperResult(failureMsg: $"Failed [{dnsAPIProvider.ProviderTitle}]: {exp.Message}");
                }
            }
            else
            {
                return new DnsChallengeHelperResult(failureMsg: "Error: Could not determine DNS API Provider.");
            }
        }
    }
}
