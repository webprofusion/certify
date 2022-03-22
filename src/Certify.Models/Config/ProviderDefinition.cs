using System;
using System.Collections.Generic;

namespace Certify.Models.Config
{
    public enum ChallengeHandlerType
    {
        MANUAL = 1,
        CUSTOM_SCRIPT = 2,
        PYTHON_HELPER = 3,
        PLUGIN = 4,
        INTERNAL = 5,
        POWERSHELL = 6
    }

    public class ProviderDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string HelpUrl { get; set; } = string.Empty;
        public List<ProviderParameter> ProviderParameters { get; set; } = new();
        public string? Config { get; set; }
        public bool IsExperimental { get; set; }

        /// <summary>
        /// If not enabled, provider will be skipped when loading
        /// </summary>
        public bool IsEnabled { get; set; } = true;
        public bool IsTestModeSupported { get; set; } = true;
        public bool HasDynamicParameters { get; set; }

        public ProviderDefinition()
        {
            ProviderParameters = new List<ProviderParameter>();
        }
    }

    public class ChallengeProviderDefinition : ProviderDefinition
    {
        public string ChallengeType { get; set; } = String.Empty;
        public ChallengeHandlerType HandlerType { get; set; }
        public int PropagationDelaySeconds { get; set; }

        public ChallengeProviderDefinition() : base()
        {

        }
    }

    [Flags]
    public enum DeploymentProviderUsage
    {
        Any = 0,
        PreRequest = 1,
        PostRequest = 2,
        Disabled = 8
    }

    [Flags]
    public enum DeploymentContextType
    {
        LocalAsService = 0,
        LocalAsUser = 2,
        WindowsNetwork = 4,
        SSH = 8,
        ExternalCredential = 16
    }

    public class DeploymentProviderDefinition : ProviderDefinition
    {
        /// <summary>
        /// Default title for a new task of this type
        /// </summary>
        public string DefaultTitle { get; set; } = string.Empty;

        /// <summary>
        /// Flags for allowed usage types
        /// </summary>
        public DeploymentProviderUsage UsageType { get; set; } = DeploymentProviderUsage.Any;

        /// <summary>
        /// Flags for supported execution context (local, local as user, windows network, remote ssh)
        /// </summary>
        public DeploymentContextType SupportedContexts { get; set; } = DeploymentContextType.LocalAsService;

        /// <summary>
        /// If set, challenge type of external credential required
        /// </summary>
        public string? ExternalCredentialType { get; set; }
    }
}
