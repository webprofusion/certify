using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;

namespace Certify.Providers.DNS.CertifyDns
{
    public class DnsProviderCertifyDnsProvider : PluginProviderBase<IDnsProvider, ChallengeProviderDefinition>, IDnsProviderProviderPlugin { }

    public class DnsProviderCertifyDns : AcmeDns.DnsProviderAcmeDns, IDnsProvider
    {
        public new static ChallengeProviderDefinition Definition
        {
            get
            {
                return new ChallengeProviderDefinition
                {
                    Id = "DNS01.API.CertifyDns",
                    Title = "Certify DNS",
                    Description = "A managed acme-dns service. Validates via an Certify DNS (managed acme-dns) based server using CNAME redirection to an alternative DNS service dedicated to ACME challenge responses.",
                    HelpUrl = "https://docs.certifytheweb.com/docs/dns-acmedns",
                    PropagationDelaySeconds = 5,
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{ Key="api",Name="API Url", IsRequired=true, IsCredential=false, IsPassword=false, Value="https://certify-dns.certifytheweb.com", Description="Base URL for a managed version of acme-dns" },
                        new ProviderParameter{ Key="user",Name="API Username", IsRequired=true, IsCredential=true, IsPassword=false,  Description="API Username" },
                        new ProviderParameter{ Key="key",Name="API Key", IsRequired=true, IsCredential=true, IsPassword=false,  Description="API Key" },

                    },
                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.CertifyDns",
                    HandlerType = ChallengeHandlerType.INTERNAL
                };
            }
        }

        public DnsProviderCertifyDns() : base()
        {

        }

        public new async Task<ActionResult> Test()
        {
            // TODO check registration credentials are ok by performing a license check

            return await Task.FromResult(new ActionResult { IsSuccess = true, Message = "Test completed, but no zones returned." });
        }

    }
}
