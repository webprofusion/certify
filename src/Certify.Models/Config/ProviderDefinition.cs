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
    public enum TaskPreconditionType
    {
        None = 0, // run task whether step was success or failure
        OnSuccess = 1, // run task only if last step was success
        OnFailure = 2, // run task only if last step was failure
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
        public bool IsTestModeSupported { get; set; } = true;
        public ProviderDefinition()
        {
            ProviderParameters = new List<ProviderParameter>();
        }
    }

    public class ChallengeProviderDefinition : ProviderDefinition
    {
        public string ChallengeType { get; set; }
        public ChallengeHandlerType HandlerType { get; set; }
        public int PropagationDelaySeconds { get; set; }

        public ChallengeProviderDefinition() : base()
        {

        }
    }

    public class DeploymentProviderDefinition : ProviderDefinition
    {

        /// <summary>
        /// If true, task requires either windows or SSH credentials depending on whether deployment target is local, remote windows or ssh
        /// </summary>
        public bool RequiresCredentials { get; set; }

        /// <summary>
        /// If true, deployment task supports remote target (scripts, commands, file copies etc)
        /// </summary>
        public bool EnableRemoteOptions { get; set; } = true;

        /// <summary>
        /// Default title for a new task of this type
        /// </summary>
        public string DefaultTitle { get; set; }

        /// <summary>
        /// Defines whether task will condition based on preconditions
        /// </summary>
        public TaskPreconditionType PreconditionType { get; set; } = TaskPreconditionType.OnSuccess;

    }
}
