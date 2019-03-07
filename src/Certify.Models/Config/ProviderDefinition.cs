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
        public string Title { get; set; }
        public string Description { get; set; }
        public string HelpUrl { get; set; }
        public List<ProviderParameter> ProviderParameters { get; set; }
        public string Config { get; set; }
        public bool IsExperimental { get; set; }

        public ProviderDefinition()
        {
            ProviderParameters = new List<ProviderParameter>();
        }
    }

    public class ChallengeProviderDefinition: ProviderDefinition
    {
        public string ChallengeType { get; set; }
        public ChallengeHandlerType HandlerType { get; set; }
        public int PropagationDelaySeconds { get; set; }

        public ChallengeProviderDefinition(): base()
        {
            
        }
    }

    public class DeploymentProviderDefinition : ProviderDefinition
    {
        /// If true, task may execute against local windows server
        /// </summary>
        public bool SupportsLocalWindows { get; set; }

        /// <summary>
        /// If true, task may execute against a remote windows server
        /// </summary>
        public bool SupportsRemoteWindows { get; set; }

        /// <summary>
        /// If true, tasks may execute against a remote SSH server
        /// </summary>
        public bool SupportsRemoteSSH { get; set; }

        /// <summary>
        /// If true, task requires either windows or SSH credentials depending on whether deployment target is local, remote windows or ssh
        /// </summary>
        public bool RequiresCredentials { get; set; }
    }
}
