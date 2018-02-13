using System.Collections.Generic;

namespace Certify.Models.Config
{
    public class ChallengeProvider
    {
        public string Id { get; set; }
        public string ChallengeType { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string HelpUrl { get; set; }
        public string RequiredCredentials { get; set; }

        public string Config { get; set; }
    }

    public class ChallengeProviders
    {
        public static List<ChallengeProvider> Providers = new List<ChallengeProvider>
        {
            // IIS
            new ChallengeProvider
            {
                Id = "HTTP01.IIS.Local",
                ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP,
                Title = "Local IIS Server",
                Description = "Validates via standard http website bindings on port 80"
            },
            // DNS
            new ChallengeProvider
            {
                Id = "DNS01.API.Route53",
                ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Title = "Amazon Route 53 DNS API",
                Description = "Validates via Route 53 APIs using AMI service credentials",
                RequiredCredentials="Access Key, Secret Access Key",
                Config="Provider=PythonHelper;Driver=ROUTE53"
            },
            new ChallengeProvider
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