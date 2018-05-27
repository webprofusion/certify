using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management.Challenges.DNS;
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
        public static async Task<IDnsProvider> GetDnsProvider(string providerType, Dictionary<string, string> credentials, Dictionary<string, string> parameters)
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
            else if (providerDefinition.HandlerType == Models.Config.ChallengeHandlerType.INTERNAL)
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
                    dnsAPIProvider = new DnsProviderCloudflare(credentials);
                }

                if (providerDefinition.Id == "DNS01.API.GoDaddy")
                {
                    dnsAPIProvider = new DnsProviderGoDaddy(credentials);
                }

                if (providerDefinition.Id == "DNS01.API.DnsMadeEasy")
                {
                    dnsAPIProvider = new DnsProviderDnsMadeEasy(credentials);
                }
            }
            else if (providerDefinition.HandlerType == Models.Config.ChallengeHandlerType.MANUAL)
            {
                if (providerDefinition.Id == "DNS01.Manual")
                {
                     dnsAPIProvider = new DNS.DnsProviderManual(parameters);
                }
            }
            else if (providerDefinition.HandlerType == Models.Config.ChallengeHandlerType.CUSTOM_SCRIPT)
            {
                if (providerDefinition.Id == "DNS01.Scripting")
                {
                    dnsAPIProvider = new DNS.DnsProviderScripting(parameters);
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
                DnsProviderManual.Definition,
                DnsProviderScripting.Definition,
                Providers.DNS.AWSRoute53.DnsProviderAWSRoute53.Definition,
                Providers.DNS.Azure.DnsProviderAzure.Definition,
                Providers.DNS.Cloudflare.DnsProviderCloudflare.Definition,
                Providers.DNS.GoDaddy.DnsProviderGoDaddy.Definition,
                Providers.DNS.DnsMadeEasy.DnsProviderDnsMadeEasy.Definition,
            };

            return await Task.FromResult(providers);
        }
    }
}
