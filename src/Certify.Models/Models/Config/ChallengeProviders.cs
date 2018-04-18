using System.Collections.Generic;

namespace Certify.Models.Config
{
    public class ChallengeProviders
    {
        public static List<ProviderDefinition> Providers = new List<ProviderDefinition>
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
            new ProviderDefinition
            {
                Id = "DNS01.API.Route53",
                ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Title = "Amazon Route 53 DNS API",
                Description = "Validates via Route 53 APIs using AMI service credentials",
                ProviderParameters= new List<ProviderParameter>{
                    new ProviderParameter{ Key="accesskey",Name="Access Key", IsRequired=true, IsPassword=false },
                    new ProviderParameter{ Key="secretaccesskey",Name="Secret Access Key", IsRequired=true, IsPassword=true }
                },
                Config="Provider=Certify.Providers.DNS.AWSRoute53",
                HandlerType = ChallengeHandlerType.INTERNAL
            },
            new ProviderDefinition
            {
                Id = "DNS01.API.Azure",
                ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Title = "Azure DNS API",
                Description = "Validates via Azure DNS APIs using credentials",
                HelpUrl="https://docs.microsoft.com/en-us/azure/dns/dns-sdk",
                ProviderParameters = new List<ProviderParameter>{
                    new ProviderParameter{Key="tenantid", Name="Tenant Id", IsRequired=false },
                    new ProviderParameter{Key="clientid", Name="Application Id", IsRequired=false },
                    new ProviderParameter{Key="secret",Name="Svc Principal Secret", IsRequired=true , IsPassword=true},
                    new ProviderParameter{Key="subscriptionid",Name="DNS Subscription Id", IsRequired=true , IsPassword=false},
                    new ProviderParameter{Key="resourcegroupname",Name="Resource Group Name", IsRequired=true , IsPassword=false}
                },
                Config="Provider=Certify.Providers.DNS.Azure",
                HandlerType = ChallengeHandlerType.INTERNAL
            },
              new ProviderDefinition
            {
                Id = "DNS01.API.Cloudflare",
                ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Title = "Cloudflare DNS API",
                Description = "Validates via Cloudflare DNS APIs using credentials",
                HelpUrl="https://api.cloudflare.com/#dns-records-for-a-zone-dns-record-details",
                ProviderParameters = new List<ProviderParameter>{
                    new ProviderParameter{Key="emailaddress", Name="Email Address", IsRequired=true },
                    new ProviderParameter{Key="authkey", Name="Auth Key", IsRequired=true }
                },
                Config="Provider=Certify.Providers.DNS.Azure",
                HandlerType = ChallengeHandlerType.INTERNAL
            }

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
    }
}