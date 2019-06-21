using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Providers.DeploymentTasks
{
    public interface IDeploymentTaskProvider
    {
        Task<List<ActionResult>> Execute(
            ILog log,
            ManagedCertificate managedCert,
            DeploymentTaskConfig settings,
            Dictionary<string, string> credentials,
            bool isPreviewOnly,
            DeploymentProviderDefinition definition
            );

        DeploymentProviderDefinition GetDefinition(DeploymentProviderDefinition currentDefinition = null);
    }

    /// <summary>
    /// An attempt to establish or use a remote connection has failed
    /// </summary>
    public class RemoteConnectionException : Exception
    {
        public RemoteConnectionException(string message)
       : base(message)
        {
        }

        public RemoteConnectionException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    public class DeploymentTaskProviderBase : IDeploymentTaskProvider
    {
        public static DeploymentProviderDefinition Definition
        {
            get
            {
                return new DeploymentProviderDefinition { Title = "Deployment Task Definition Not Implemented in Provider" };
            }
        }

        public virtual Task<List<ActionResult>> Execute(
            ILog log, ManagedCertificate managedCert,
            DeploymentTaskConfig settings,
            Dictionary<string, string> credentials,
            bool isPreviewOnly,
            DeploymentProviderDefinition definition = null
            ) => throw new NotImplementedException();

        /// <summary>
        /// Returns either the current definition or the default for this instance type
        /// </summary>
        /// <param name="definition">Current definition or null. Used to override the base definition when inheriting</param>
        /// <returns></returns>
        public DeploymentProviderDefinition GetDefinition(DeploymentProviderDefinition currentDefinition)
        {
            if (currentDefinition != null)
            {
                return currentDefinition;
            }
            else
            {
                return Definition;
            }
        }
    }

    public class DeploymentTask
    {
        public DeploymentTask(IDeploymentTaskProvider provider, DeploymentTaskConfig config, Dictionary<string, string> credentials)
        {
            TaskConfig = config;
            TaskProvider = provider;

            _credentials = credentials;
        }

        public IDeploymentTaskProvider TaskProvider { get; set; }

        public DeploymentTaskConfig TaskConfig { get; set; }

        private Dictionary<string, string> _credentials;

        public async Task<List<ActionResult>> Execute(
            ILog log,
            ManagedCertificate managedCert,
            bool isPreviewOnly = true
            )
        {
            if (TaskProvider != null && TaskConfig != null)
            {
                try
                {
                    return await TaskProvider.Execute(log, managedCert, TaskConfig, _credentials, isPreviewOnly, null);
                }
                catch (Exception exp)
                {
                    return new List<ActionResult>{
                        new ActionResult { IsSuccess = false, Message = $"Task Failed: {TaskProvider.GetDefinition()?.Title } :: {exp?.ToString()}" }
                    };
                }

            }
            else
            {
                return new List<ActionResult>{
                    new ActionResult { IsSuccess = false, Message = "Cannot Execute Deployment Task: TaskProvider or Config not set." }
                    };
            }
        }
    }
}
