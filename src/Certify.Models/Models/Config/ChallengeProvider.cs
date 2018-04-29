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
        public int PropagationDelaySeconds { get; set; }

        public ProviderDefinition()
        {
            ProviderParameters = new List<ProviderParameter>();
        }
    }
}
