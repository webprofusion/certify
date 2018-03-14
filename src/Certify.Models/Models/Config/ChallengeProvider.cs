using System.Collections.Generic;

namespace Certify.Models.Config
{
    public enum ChallengeHandlerType
    {
        MANUAL = 1,
        CUSTOM_SCRIPT = 2,
        PYTHON_HELPER = 3,
        PLUGIN = 4,
        INTERNAL = 5
    }

    public class ProviderDefinition
    {
        public string Id { get; set; }
        public string ChallengeType { get; set; }
        public ChallengeHandlerType HandlerType { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string HelpUrl { get; set; }
        public string RequiredCredentials { get; set; }
        public List<ProviderParameter> ProviderParameters { get; set; }
        public string Config { get; set; }

        public ProviderDefinition()
        {
            ProviderParameters = new List<ProviderParameter>();
        }
    }

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
                    new ProviderParameter{ Name="Access Key", IsRequired=true, IsPassword=false },
                    new ProviderParameter{ Name="Secret Access Key", IsRequired=true, IsPassword=true },
                    new ProviderParameter{ Name="Hosted Zone ID", IsRequired=true, IsPassword=false }
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
                    new ProviderParameter{Name="TenantId", IsRequired=true },
                    new ProviderParameter{Name="ClientId", IsRequired=true },
                    new ProviderParameter{Name="Secret", IsRequired=true , IsPassword=true},
                    new ProviderParameter{Name="DNS Subscription Id", IsRequired=true , IsPassword=true},
                    new ProviderParameter{Name="Resource Group Name", IsRequired=true , IsPassword=false},
                },
                RequiredCredentials="Subscription ID, Access Key",
                Config="Provider=PythonHelper;Driver=AZURE",
                HandlerType = ChallengeHandlerType.PYTHON_HELPER
            }
        };
    }
}