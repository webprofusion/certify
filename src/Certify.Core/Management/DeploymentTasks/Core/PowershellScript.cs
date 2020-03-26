using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Config;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Management;

namespace Certify.Providers.DeploymentTasks.Core
{
    public class PowershellScript : IDeploymentTaskProvider
    {
        public static DeploymentProviderDefinition Definition { get; }
        public DeploymentProviderDefinition GetDefinition(DeploymentProviderDefinition currentDefinition) => (currentDefinition ?? Definition);

        static PowershellScript()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.Script",
                Title = "Run Powershell Script",
                IsExperimental = true,
                Description = "Run a Powershell script",
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                    new ProviderParameter{ Key="scriptpath", Name="Program/Script", IsRequired=true, IsCredential=false, Description="Command to run, may require a full path"  },
                    new ProviderParameter{ Key="inputresult", Name="Pass Result as First Argument", IsRequired=false, IsCredential=false, Type= OptionType.Boolean, Value="1"  },
                    new ProviderParameter{ Key="args", Name="Arguments (optional)", IsRequired=false, IsCredential=false  },
                }
            };
        }

        /// <summary>
        /// Execute a local powershell script
        /// </summary>
        /// <param name="log"></param>
        /// <param name="managedCert"></param>
        /// <param name="settings"></param>
        /// <param name="credentials"></param>
        /// <param name="isPreviewOnly"></param>
        /// <returns></returns>
        public async Task<List<ActionResult>> Execute(
          ILog log,
          Models.ManagedCertificate managedCert,
          DeploymentTaskConfig settings,
          Dictionary<string, string> credentials,
          bool isPreviewOnly,
          DeploymentProviderDefinition definition = null
          )
        {

            definition = GetDefinition(definition);

            var command = settings.Parameters.FirstOrDefault(c => c.Key == "path")?.Value;
            var args = settings.Parameters.FirstOrDefault(c => c.Key == "args")?.Value;

            var commandList = new List<string>
            {
                $"{command} {args}"
            };

            log?.Information("Executing command via PowerShell");

            var results = await PowerShellManager.RunScript(null, command, null, null);

            return new List<ActionResult> {
                    new ActionResult { IsSuccess = true, Message = results}
                };
            /*
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
            }*/

        }

        public Task<List<ActionResult>> Validate(ManagedCertificate managedCert, DeploymentTaskConfig settings, Dictionary<string, string> credentials, DeploymentProviderDefinition definition)
        {
            throw new System.NotImplementedException();
        }
    }
}
