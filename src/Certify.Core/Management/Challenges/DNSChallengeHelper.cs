using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers.DNS.Azure;
using Certify.Providers.DNS.Cloudflare;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Certify.Core.Management.Challenges
{
    public class DNSChallengeHelper
    {
        /// <summary>
        /// For the given domain, get the matching challenge config (DNS provider variant etc) 
        /// </summary>
        /// <param name="managedSite"></param>
        /// <param name="domain"></param>
        /// <returns></returns>
        private CertRequestChallengeConfig GetChallengeConfig(ManagedSite managedSite, string domain)
        {
            if (managedSite.RequestConfig.Challenges == null || managedSite.RequestConfig.Challenges.Count == 0)
            {
                // there are no challenge configs defined return a default based on the parent
                return new CertRequestChallengeConfig
                {
                    ChallengeType = managedSite.RequestConfig.ChallengeType
                };
            }
            else
            {
                //identify matching challenge config based on domain etc
                if (managedSite.RequestConfig.Challenges.Count == 1)
                {
                    return managedSite.RequestConfig.Challenges[0];
                }
                else
                {
                    // start by matching first config with no specific domain
                    CertRequestChallengeConfig matchedConfig = managedSite.RequestConfig.Challenges.FirstOrDefault(c => String.IsNullOrEmpty(c.DomainMatch));

                    //if any more specific configs match, use that
                    foreach (var config in managedSite.RequestConfig.Challenges.Where(c => !String.IsNullOrEmpty(c.DomainMatch)).OrderByDescending(l => l.DomainMatch.Length))
                    {
                        if (config.DomainMatch.EndsWith(domain))
                        {
                            // use longest matching domain (so subdomain.test.com takes priority over test.com)
                            return config;
                        }
                    }

                    // no other matches, just use first
                    if (matchedConfig != null)
                    {
                        return matchedConfig;
                    }
                    else
                    {
                        // no match, return default
                        return new CertRequestChallengeConfig
                        {
                            ChallengeType = managedSite.RequestConfig.ChallengeType
                        };
                    }
                }
            }
        }

        public async Task<ActionResult> CompleteDNSChallenge(ManagedSite managedsite, string domain, string txtRecordName, string txtRecordValue)
        {
            // for a given managed site configuration, attempt to complete the required challenge by
            // creating the required TXT record

            var credentialsManager = new CredentialsManager();
            Dictionary<string, string> credentials = new Dictionary<string, string>();

            string zoneId = null;

            Models.Config.ProviderDefinition providerDefinition;
            IDnsProvider dnsAPIProvider = null;

            var challengeConfig = GetChallengeConfig(managedsite, domain);

            if (!String.IsNullOrEmpty(challengeConfig.ChallengeCredentialKey))
            {
                // decode credentials string array
                credentials = await credentialsManager.GetUnlockedCredentialsDictionary(challengeConfig.ChallengeCredentialKey);
            }
            else
            {
                return new ActionResult { IsSuccess = false, Message = "DNS Challenge API Credentials not set. Add or select API credentials to proceed." };
            }

            if (!String.IsNullOrEmpty(challengeConfig.ChallengeProvider))
            {
                providerDefinition = Models.Config.ChallengeProviders.Providers.FirstOrDefault(p => p.Id == challengeConfig.ChallengeProvider);
            }
            else
            {
                return new ActionResult { IsSuccess = false, Message = "DNS Challenge API Provider not set. Select an API to proceed." };
            }

            if (providerDefinition.HandlerType == Models.Config.ChallengeHandlerType.PYTHON_HELPER)
            {
                dnsAPIProvider = new LibcloudDNSProvider(credentials);
            }
            else
            {
                if (providerDefinition.HandlerType == Models.Config.ChallengeHandlerType.INTERNAL)
                {
                    if (providerDefinition.Id == "DNS01.API.Route53")
                    {
                        zoneId = credentials["zoneid"];

                        dnsAPIProvider = new Providers.DNS.AWSRoute53.DnsProviderAWSRoute53(credentials["accesskey"], credentials["secretaccesskey"]);
                    }

                    if (providerDefinition.Id == "DNS01.API.Azure")
                    {
                        zoneId = credentials["zoneid"];

                        var azureDns = new DnsProviderAzure(credentials);
                        await azureDns.InitProvider();
                        dnsAPIProvider = azureDns;
                    }

                    if (providerDefinition.Id == "DNS01.API.Cloudflare")
                    {
                        zoneId = credentials["zoneid"];

                        var azureDns = new DnsProviderCloudflare(credentials);
                        dnsAPIProvider = azureDns;
                    }
                }
            }

            if (dnsAPIProvider != null)
            {
                var result = await dnsAPIProvider.CreateRecord(new DnsCreateRecordRequest
                {
                    RecordType = "TXT",
                    TargetDomainName = domain,
                    RecordName = txtRecordName,
                    RecordValue = txtRecordValue,
                    ZoneId = zoneId
                });

                if (result.IsSuccess)
                {
                    // do our own txt record query before proceeding with challenge completion
                    /*
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
                    */

                    await Task.Delay(5000); // hold on a sec

                    return result;
                }
                else
                {
                    return result;
                }
            }
            else
            {
                return new ActionResult { IsSuccess = false, Message = "Error: Could not determine DNS API Provider." };
            }
        }
    }
}