using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers.DNS.AWSRoute53;
using Certify.Providers.DNS.Azure;
using Certify.Providers.DNS.Cloudflare;
using Certify.Providers.DNS.DnsMadeEasy;
using Certify.Providers.DNS.GoDaddy;

namespace Certify.Core.Management.Challenges
{
    public class ChallengeProviders
    {
        public static async Task<IDnsProvider> GetDnsProvider(string providerType, Dictionary<string, string> credentials)
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
                dnsAPIProvider = new LibcloudDNSProvider(credentials);
            }
            else
            {
                if (providerDefinition.HandlerType == Models.Config.ChallengeHandlerType.INTERNAL)
                {
                    if (providerDefinition.Id == "DNS01.API.Route53")
                    {
                        dnsAPIProvider = new DnsProviderAWSRoute53(credentials);
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

                    if (providerDefinition.Id == "DNS01.API.GoDaddy")
                    {
                        var goDaddyDns = new DnsProviderGoDaddy(credentials);
                        dnsAPIProvider = goDaddyDns;
                    }

                    if (providerDefinition.Id == "DNS01.API.DnsMadeEasy")
                    {
                        var dnsMadeEasy = new DnsProviderDnsMadeEasy(credentials);
                        dnsAPIProvider = dnsMadeEasy;
                    }
                }
            }
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
                Providers.DNS.AWSRoute53.DnsProviderAWSRoute53.Definition,
                Providers.DNS.Azure.DnsProviderAzure.Definition,
                Providers.DNS.Cloudflare.DnsProviderCloudflare.Definition,
                Providers.DNS.GoDaddy.DnsProviderGoDaddy.Definition,
                Providers.DNS.DnsMadeEasy.DnsProviderDnsMadeEasy.Definition,

                /*
                 *  new ProviderDefinition
                {
                    Id = "DNS01.API.Azure",
                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Title = "Azure DNS API",
                    Description = "Validates via Azure DNS APIs using credentials",
                    HelpUrl="https://docs.microsoft.com/en-us/azure/dns/dns-sdk",
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{Name="TenantId", IsRequired=true },
                        new ProviderParameter{Name="ClientId", IsRequired=true },
                        new ProviderParameter{Name="Secret", IsRequired=true , IsPassword=true},
                        new ProviderParameter{Name="DNS Subscription Id", IsRequired=true , IsPassword=true},
                        new ProviderParameter{Name="Resource Group Name", IsRequired=true , IsPassword=false},
                    },
                    Config="Provider=PythonHelper;Driver=AZURE",
                    HandlerType = ChallengeHandlerType.PYTHON_HELPER
                }*/
            };

            return await Task.FromResult(providers);
        }
    }
}
