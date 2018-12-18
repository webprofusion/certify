using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management.Challenges.DNS;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers.DNS.AcmeDns;
using Certify.Providers.DNS.Aliyun;
using Certify.Providers.DNS.AWSRoute53;
using Certify.Providers.DNS.Azure;
using Certify.Providers.DNS.Cloudflare;
using Certify.Providers.DNS.DnsMadeEasy;
using Certify.Providers.DNS.GoDaddy;
using Certify.Providers.DNS.MSDNS;
using Certify.Providers.DNS.OVH;
using Certify.Providers.DNS.SimpleDNSPlus;

namespace Certify.Core.Management.Challenges
{
    public class ChallengeProviders
    {
        public class CredentialsRequiredException : Exception
        {
        }

        public static async Task<IDnsProvider> GetDnsProvider(string providerType, Dictionary<string, string> credentials, Dictionary<string, string> parameters, ILog log = null)
        {
            ProviderDefinition providerDefinition;
            IDnsProvider dnsAPIProvider = null;

            if (!string.IsNullOrEmpty(providerType))
            {
                providerDefinition = (await ChallengeProviders.GetChallengeAPIProviders()).FirstOrDefault(p => p.Id == providerType);
            }
            else
            {
                return null;
            }

            if (providerDefinition.HandlerType == Models.Config.ChallengeHandlerType.PYTHON_HELPER)
            {
                if (credentials == null || !credentials.Any())
                {
                    throw new CredentialsRequiredException();
                }

                dnsAPIProvider = new LibcloudDNSProvider(credentials);
            }
            else if (providerDefinition.HandlerType == Models.Config.ChallengeHandlerType.INTERNAL)
            {
                if (credentials == null)
                {
                    throw new CredentialsRequiredException();
                }

                // instantiate/initialise the required DNS provider
                if (providerDefinition.Id == DnsProviderAWSRoute53.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderAWSRoute53(credentials);
                }
                else if (providerDefinition.Id == DnsProviderAzure.Definition.Id)
                {
                    var azureDns = new DnsProviderAzure(credentials);

                    dnsAPIProvider = azureDns;
                }
                else if (providerDefinition.Id == DnsProviderCloudflare.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderCloudflare(credentials);
                }
                else if (providerDefinition.Id == DnsProviderGoDaddy.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderGoDaddy(credentials);
                }
                else if (providerDefinition.Id == DnsProviderSimpleDNSPlus.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderSimpleDNSPlus(credentials);
                }
                else if (providerDefinition.Id == DnsProviderDnsMadeEasy.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderDnsMadeEasy(credentials);
                }
                else if (providerDefinition.Id == DnsProviderOvh.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderOvh(credentials);
                }
                else if (providerDefinition.Id == DnsProviderAliyun.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderAliyun(credentials);
                }
                else if (providerDefinition.Id == DnsProviderMSDNS.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderMSDNS(credentials, parameters);
                }
                else if (providerDefinition.Id == DnsProviderAcmeDns.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderAcmeDns(credentials, parameters, Util.GetAppDataFolder());
                }
            }
            else if (providerDefinition.HandlerType == Models.Config.ChallengeHandlerType.MANUAL)
            {
                if (providerDefinition.Id == DNS.DnsProviderManual.Definition.Id)
                {
                    dnsAPIProvider = new DNS.DnsProviderManual(parameters);
                }
            }
            else if (providerDefinition.HandlerType == Models.Config.ChallengeHandlerType.CUSTOM_SCRIPT)
            {
                if (providerDefinition.Id == DNS.DnsProviderScripting.Definition.Id)
                {
                    dnsAPIProvider = new DNS.DnsProviderScripting(parameters);
                }
            }

            await dnsAPIProvider.InitProvider(log);

            return dnsAPIProvider;
        }

        public static async Task<List<ProviderDefinition>> GetChallengeAPIProviders()
        {
            var providers = new List<ProviderDefinition>
            {
                // IIS
                new ProviderDefinition
                {
                    Id = "HTTP01.IIS.Local",
                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP,
                    Title = "Local IIS Server",
                    Description = "Validates via standard http website bindings on port 80",
                    HandlerType = ChallengeHandlerType.INTERNAL
                },
                // DNS
                DnsProviderManual.Definition,
                DnsProviderScripting.Definition,
                Providers.DNS.AWSRoute53.DnsProviderAWSRoute53.Definition,
                Providers.DNS.Azure.DnsProviderAzure.Definition,
                Providers.DNS.Cloudflare.DnsProviderCloudflare.Definition,
                Providers.DNS.GoDaddy.DnsProviderGoDaddy.Definition,
                Providers.DNS.SimpleDNSPlus.DnsProviderSimpleDNSPlus.Definition,
                Providers.DNS.DnsMadeEasy.DnsProviderDnsMadeEasy.Definition,
                Providers.DNS.OVH.DnsProviderOvh.Definition,
                Providers.DNS.Aliyun.DnsProviderAliyun.Definition,
                Providers.DNS.MSDNS.DnsProviderMSDNS.Definition,
                Providers.DNS.AcmeDns.DnsProviderAcmeDns.Definition
            };

            return await Task.FromResult(providers);
        }
    }
}
