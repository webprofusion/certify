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
        public ActionResult Result;
        public int PropagationSeconds;
        public bool IsAwaitingUser;
        public IDnsProvider Provider;
    }

    public class DnsChallengeHelper
    {
        private readonly IdnMapping _idnMapping = new IdnMapping();

        public async Task<DnsChallengeHelperResult> GetDnsProvider(string providerTypeId, string credentialsId, Dictionary<string, string> parameters)
        {
            var credentialsManager = new CredentialsManager();
            var credentials = new Dictionary<string, string>();

            IDnsProvider dnsAPIProvider = null;

            if (!string.IsNullOrEmpty(credentialsId))
            {
                // decode credentials string array
                try
                {
                    credentials = await credentialsManager.GetUnlockedCredentialsDictionary(credentialsId);
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

            try
            {
                dnsAPIProvider = await ChallengeProviders.GetDnsProvider(providerTypeId, credentials, parameters);
            }
            catch (ChallengeProviders.CredentialsRequiredException)
            {
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = false, Message = "This DNS Challenge API requires one or more credentials to be specified." },
                    PropagationSeconds = 0,
                    IsAwaitingUser = false
                };
            }
            catch (Exception exp)
            {
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = false, Message = $"DNS Challenge API Provider could not be created. Check all required credentials are set. {exp.ToString()}" },
                    PropagationSeconds = 0,
                    IsAwaitingUser = false
                };
            }

            if (dnsAPIProvider == null)
            {
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = false, Message = "DNS Challenge API Provider not set or not recognised. Select an API to proceed." },
                    PropagationSeconds = 0,
                    IsAwaitingUser = false
                };
            }

            return new DnsChallengeHelperResult
            {
                Result = new ActionResult { IsSuccess = true, Message = "Create Provider Instance" },
                Provider = dnsAPIProvider
            };
        }

        public async Task<DnsChallengeHelperResult> CompleteDNSChallenge(ILog log, ManagedCertificate managedcertificate, string domain, string txtRecordName, string txtRecordValue)
        {
            // for a given managed site configuration, attempt to complete the required challenge by
            // creating the required TXT record

            var credentialsManager = new CredentialsManager();
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
                    credentials = await credentialsManager.GetUnlockedCredentialsDictionary(challengeConfig.ChallengeCredentialKey);
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
                dnsAPIProvider = await ChallengeProviders.GetDnsProvider(challengeConfig.ChallengeProvider, credentials, parameters, log);
            }
            catch (ChallengeProviders.CredentialsRequiredException)
            {
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = false, Message = "This DNS Challenge API requires one or more credentials to be specified." },
                    PropagationSeconds = 0,
                    IsAwaitingUser = false
                };
            }
            catch (Exception exp)
            {
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = false, Message = $"DNS Challenge API Provider could not be created. Check all required credentials are set. {exp.ToString()}" },
                    PropagationSeconds = 0,
                    IsAwaitingUser = false
                };
            }

            if (dnsAPIProvider == null)
            {
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = false, Message = "DNS Challenge API Provider not set or not recognised. Select an API to proceed." },
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
                txtRecordName = _idnMapping.GetAscii(txtRecordName).ToLower();

                log.Information($"DNS: Creating TXT Record '{txtRecordName}' with value '{txtRecordValue}', in Zone Id '{zoneId}' using API provider '{dnsAPIProvider.ProviderTitle}'");
                try
                {
                    var result = await dnsAPIProvider.CreateRecord(new DnsRecord
                    {
                        RecordType = "TXT",
                        TargetDomainName = domain,
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
                    return new DnsChallengeHelperResult
                    {
                        Result = new ActionResult { IsSuccess = false, Message = $"Failed [{dnsAPIProvider.ProviderTitle}]: " + exp.Message },
                        PropagationSeconds = 0,
                        IsAwaitingUser = false
                    };
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
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = false, Message = "Error: Could not determine DNS API Provider." },
                    PropagationSeconds = 0,
                    IsAwaitingUser = false
                };
            }
        }

        public async Task<DnsChallengeHelperResult> DeleteDNSChallenge(ILog log, ManagedCertificate managedcertificate, string domain, string txtRecordName, string txtRecordValue)
        {
            // for a given managed site configuration, attempt to delete the TXT record created for
            // the challenge
            var credentialsManager = new CredentialsManager();
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
                    credentials = await credentialsManager.GetUnlockedCredentialsDictionary(challengeConfig.ChallengeCredentialKey);
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
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = false, Message = "This DNS Challenge API requires one or more credentials to be specified." },
                    PropagationSeconds = 0,
                    IsAwaitingUser = false
                };
            }
            catch (Exception exp)
            {
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = false, Message = $"DNS Challenge API Provider could not be created. Check all required credentials are set. {exp.ToString()}" },
                    PropagationSeconds = 0,
                    IsAwaitingUser = false
                };
            }

            if (dnsAPIProvider == null)
            {
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = false, Message = "DNS Challenge API Provider not set or not recognised. Select an API to proceed." },
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
                txtRecordName = _idnMapping.GetAscii(txtRecordName).ToLower();

                log.Information($"DNS: Deleting TXT Record '{txtRecordName}', in Zone Id '{zoneId}' using API provider '{dnsAPIProvider.ProviderTitle}'");
                try
                {
                    var result = await dnsAPIProvider.DeleteRecord(new DnsRecord
                    {
                        RecordType = "TXT",
                        TargetDomainName = domain,
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
                    return new DnsChallengeHelperResult
                    {
                        Result = new ActionResult { IsSuccess = false, Message = $"Failed [{dnsAPIProvider.ProviderTitle}]: " + exp.Message },
                        PropagationSeconds = 0,
                        IsAwaitingUser = false
                    };
                }
            }
            else
            {
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = false, Message = "Error: Could not determine DNS API Provider." },
                    PropagationSeconds = 0,
                    IsAwaitingUser = false
                };
            }
        }
    }
}
