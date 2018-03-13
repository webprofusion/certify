using System.Collections.Generic;

namespace Certify.Models.Config
{
    public class ProviderDefinition
    {
        public string Id { get; set; }
        public string ChallengeType { get; set; }
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
                Description = "Validates via standard http website bindings on port 80"
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
                Config="Provider=Certify.Providers.DNS.AWSRoute53"
            },
            new ProviderDefinition
            {
                Id = "DNS01.API.Azure",
                ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Title = "Azure DNS API",
                Description = "Validates via Azure DNS APIs using credentials",
                RequiredCredentials="Subscription ID, Access Key",
                Config="Provider=PythonHelper;Driver=AZURE"
            }
        };
    }
}