using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Core.Management.Challenges
{
    public class DNSChallengeHelper
    {
        public async Task<ActionResult> CompleteDNSChallenge(ILog log, ManagedCertificate managedcertificate, string domain, string txtRecordName, string txtRecordValue)
        {
            // for a given managed site configuration, attempt to complete the required challenge by
            // creating the required TXT record

            var credentialsManager = new CredentialsManager();
            Dictionary<string, string> credentials = new Dictionary<string, string>();

            IDnsProvider dnsAPIProvider = null;

            var challengeConfig = managedcertificate.GetChallengeConfig(domain);

            if (String.IsNullOrEmpty(challengeConfig.ZoneId))
            {
                return new ActionResult { IsSuccess = false, Message = "DNS Challenge Zone Id not set. Set the Zone Id to proceed." };
            }

            if (!String.IsNullOrEmpty(challengeConfig.ChallengeCredentialKey))
            {
                // decode credentials string array
                try
                {
                    credentials = await credentialsManager.GetUnlockedCredentialsDictionary(challengeConfig.ChallengeCredentialKey);
                }
                catch (Exception)
                {
                    return new ActionResult { IsSuccess = false, Message = "DNS Challenge API Credentials could not be decrypted. The original user must be used for decryption." };
                }
            }
            else
            {
                return new ActionResult { IsSuccess = false, Message = "DNS Challenge API Credentials not set. Add or select API credentials to proceed." };
            }

            dnsAPIProvider = await ChallengeProviders.GetDnsProvider(challengeConfig.ChallengeProvider, credentials);

            if (dnsAPIProvider == null)
            {
                return new ActionResult { IsSuccess = false, Message = "DNS Challenge API Provider not set or not recognised. Select an API to proceed." };
            }

            if (dnsAPIProvider != null)
            {
                try
                {
                    var result = await dnsAPIProvider.CreateRecord(new DnsRecord
                    {
                        RecordType = "TXT",
                        TargetDomainName = domain,
                        RecordName = txtRecordName,
                        RecordValue = txtRecordValue,
                        ZoneId = challengeConfig.ZoneId.Trim()
                    });

                    return result;
                }
                catch (Exception exp)
                {
                    return new ActionResult { IsSuccess = false, Message = $"Failed [{dnsAPIProvider.ProviderTitle}]: " + exp.Message };
                }

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
                return new ActionResult { IsSuccess = false, Message = "Error: Could not determine DNS API Provider." };
            }
        }
    }
}
