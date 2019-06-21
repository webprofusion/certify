using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers.Deployment.Core.Shared;

namespace Certify.Providers.DeploymentTasks
{
    public class Script : DeploymentTaskProviderBase, IDeploymentTaskProvider
    {
        public static new DeploymentProviderDefinition Definition { get; }

        static Script()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.Script",
                Title = "Run...",
                IsExperimental = true,
                Description = "Run a program, batch file or custom script",
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                    new ProviderParameter{ Key="path", Name="Program/Script", IsRequired=true, IsCredential=false, Description="Command to run, may require a full path"  },
                    new ProviderParameter{ Key="args", Name="Arguments (optional)", IsRequired=false, IsCredential=false  },
                }
            };
        }

        /// <summary>
        /// Execute a script or program either locally or remotely, windows or ssh
        /// </summary>
        /// <param name="log"></param>
        /// <param name="managedCert"></param>
        /// <param name="settings"></param>
        /// <param name="credentials"></param>
        /// <param name="isPreviewOnly"></param>
        /// <returns></returns>
        public override async Task<List<ActionResult>> Execute(
          ILog log,
          Models.ManagedCertificate managedCert,
          DeploymentTaskConfig settings,
          Dictionary<string, string> credentials,
          bool isPreviewOnly,
          DeploymentProviderDefinition definition = null
          )
        {

            definition = GetDefinition(definition);

            var sshConfig = SshClient.GetConnectionConfig(settings, credentials);

            var ssh = new SshClient(sshConfig);

            var command = settings.Parameters.FirstOrDefault(c => c.Key == "path")?.Value;
            var args = settings.Parameters.FirstOrDefault(c => c.Key == "args")?.Value;

            var commandList = new List<string>
            {
                $"{command} {args}"
            };

            log?.Information("Executing command via SSH");

            var results = ssh.ExecuteCommands(commandList);

            if (results.Any(r => r.IsError))
            {
                var firstError = results.First(c => c.IsError == true);
                return new List<ActionResult> {
                    new ActionResult { IsSuccess = false, Message = $"One or more commands failed: {firstError.Command} :: {firstError.Result}" }
                };
            }
            else
            {
                return new List<ActionResult> {
                    new ActionResult { IsSuccess = true, Message = "Nothing to do" }
                };
            }

        }

    }
}
