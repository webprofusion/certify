using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Management;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Providers.DeploymentTasks
{

    public class DeploymentContext
    {
        public string PowershellExecutionPolicy { get; set; } = "Unrestricted";
    }

    public class DeploymentTaskExecutionParams
    {
        public DeploymentTaskExecutionParams(
            ILog log,
            ICredentialsManager credentialsManager,
            object subject,
            DeploymentTaskConfig settings,
            Dictionary<string, string> credentials,
            bool isPreviewOnly,
            DeploymentProviderDefinition definition,
            CancellationToken cancellationToken,
            DeploymentContext context
            )
        {
            Log = log;
            CredentialsManager = credentialsManager;
            Subject = subject;
            Settings = settings;
            Credentials = credentials;
            IsPreviewOnly = isPreviewOnly;
            Definition = definition;
            CancellationToken = cancellationToken;
            Context = context;
        }

        /// <summary>
        /// Create new set of exec params from a source with a different provider definition
        /// </summary>
        /// <param name="execParams"></param>
        /// <param name="definition"></param>
        public DeploymentTaskExecutionParams(DeploymentTaskExecutionParams execParams, DeploymentProviderDefinition definition)
        {
            Log = execParams.Log;
            CredentialsManager = execParams.CredentialsManager;
            Subject = execParams.Subject;
            Settings = execParams.Settings;
            Credentials = execParams.Credentials;
            IsPreviewOnly = execParams.IsPreviewOnly;
            Definition = definition ?? execParams.Definition;
            CancellationToken = execParams.CancellationToken;
            Context = execParams.Context;
        }

        public ILog Log { get; }
        public ICredentialsManager CredentialsManager { get; }
        public object Subject { get; set; }
        public DeploymentTaskConfig Settings { get; }
        public Dictionary<string, string> Credentials { get; }
        public bool IsPreviewOnly { get; }
        public DeploymentProviderDefinition Definition { get; }
        public CancellationToken CancellationToken { get; }

        public DeploymentContext Context { get; }
    }

    public interface IDeploymentTaskProvider
    {

        Task<List<ActionResult>> Execute(DeploymentTaskExecutionParams execParams);

        DeploymentProviderDefinition GetDefinition(DeploymentProviderDefinition currentDefinition = null);

        Task<List<ActionResult>> Validate(DeploymentTaskExecutionParams execParams);
    }

}
