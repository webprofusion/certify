using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers.DNS.Azure;
using Certify.Providers.DNS.Cloudflare;
using Serilog;
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
        /// <param name="managedCertificate"></param>
        /// <param name="domain"></param>
        /// <returns></returns>
        private CertRequestChallengeConfig GetChallengeConfig(ManagedCertificate managedCertificate, string domain)
        {
            if (managedCertificate.RequestConfig.Challenges == null || managedCertificate.RequestConfig.Challenges.Count == 0)
            {
                // there are no challenge configs defined return a default based on the parent
                return new CertRequestChallengeConfig
                {
                    ChallengeType = managedCertificate.RequestConfig.ChallengeType
                };
            }
            else
            {
                //identify matching challenge config based on domain etc
                if (managedCertificate.RequestConfig.Challenges.Count == 1)
                {
                    return managedCertificate.RequestConfig.Challenges[0];
                }
                else
                {
                    // start by matching first config with no specific domain
                    CertRequestChallengeConfig matchedConfig = managedCertificate.RequestConfig.Challenges.FirstOrDefault(c => String.IsNullOrEmpty(c.DomainMatch));

                    //if any more specific configs match, use that
                    foreach (var config in managedCertificate.RequestConfig.Challenges.Where(c => !String.IsNullOrEmpty(c.DomainMatch)).OrderByDescending(l => l.DomainMatch.Length))
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
                            ChallengeType = managedCertificate.RequestConfig.ChallengeType
                        };
                    }
                }
            }
        }

        public async Task<ActionResult> CompleteDNSChallenge(ILogger log, ManagedCertificate managedcertificate, string domain, string txtRecordName, string txtRecordValue)
        {
            // for a given managed site configuration, attempt to complete the required challenge by
            // creating the required TXT record

            var credentialsManager = new CredentialsManager();
            Dictionary<string, string> credentials = new Dictionary<string, string>();

            Models.Config.ProviderDefinition providerDefinition;
            IDnsProvider dnsAPIProvider = null;

            var challengeConfig = GetChallengeConfig(managedcertificate, domain);

            if (String.IsNullOrEmpty(challengeConfig.ZoneId))
            {
                return new ActionResult { IsSuccess = false, Message = "DNS Challenge Zone Id not set. Set the Zone Id to proceed." };
            }

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
                        dnsAPIProvider = new Providers.DNS.AWSRoute53.DnsProviderAWSRoute53(credentials["accesskey"], credentials["secretaccesskey"]);
                    }

                    if (providerDefinition.Id == "DNS01.API.Azure")
                    {
                        var azureDns = new DnsProviderAzure(credentials);
                        await azureDns.InitProvider();
                        dnsAPIProvider = azureDns;
                    }

                    if (providerDefinition.Id == "DNS01.API.Cloudflare")
                    {
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
                    ZoneId = challengeConfig.ZoneId.Trim()
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

                    // FIXME: need a proper way to tell if DNS has updated
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