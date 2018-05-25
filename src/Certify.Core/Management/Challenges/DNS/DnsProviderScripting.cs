using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Core.Management.Challenges.DNS
{
    public class DnsProviderScripting : IDnsProvider
    {
        int IDnsProvider.PropagationDelaySeconds => Definition.PropagationDelaySeconds;

        string IDnsProvider.ProviderId => Definition.Id;

        string IDnsProvider.ProviderTitle => Definition.Title;

        string IDnsProvider.ProviderDescription => Definition.Description;

        string IDnsProvider.ProviderHelpUrl => Definition.HelpUrl;

        List<ProviderParameter> IDnsProvider.ProviderParameters => Definition.ProviderParameters;

        private string _createScriptPath = "";
        private string _deleteScriptPath = "";
        private ILog _log;

        public static ProviderDefinition Definition
        {
            get
            {
                return new ProviderDefinition
                {
                    Id = "DNS01.Scripting",
                    Title = "(Use Custom Script)",
                    Description = "Validates DNS challenges via a user provided custom script",
                    HelpUrl = "http://docs.certifytheweb.com/",
                    PropagationDelaySeconds = 60,
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{Key="CreateScriptPath", Name="Create Script Path", IsRequired=true },
                        new ProviderParameter{Key="DeleteScriptPath", Name="Delete Script Path", IsRequired=false },
                    },
                    ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.Scripting",
                    HandlerType = ChallengeHandlerType.CUSTOM_SCRIPT
                };
            }
        }

        public DnsProviderScripting(Dictionary<string, string> credentials)
        {
            _createScriptPath = credentials["CreateScriptPath"];
            _deleteScriptPath = credentials["DeleteScriptPath"];
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            // standard parameters are the subject domain/subdomain, full txt record name to create,
            // txt record value, zone id
            string parameters = $"{request.TargetDomainName} {request.RecordName} {request.RecordValue} {request.ZoneId}";
            await RunScript(_createScriptPath, parameters);

            return null;
        }

        Task<ActionResult> IDnsProvider.DeleteRecord(DnsRecord request)
        {
            throw new NotImplementedException();
        }

        Task<List<DnsZone>> IDnsProvider.GetZones()
        {
            return Task.FromResult(new List<DnsZone>());
        }

        Task<bool> IDnsProvider.InitProvider()
        {
            throw new NotImplementedException();
        }

        Task<ActionResult> IDnsProvider.Test()
        {
            throw new NotImplementedException();
        }

        private async Task RunScript(string script, string parameters)
        {
            // https://stackoverflow.com/questions/5519328/executing-batch-file-in-c-sharp and
            // attempting to have some argument compat with https://github.com/PKISharp/win-acme/blob/master/letsencrypt-win-simple/Plugins/ValidationPlugins/Dns/Script.cs

            var scriptProcessInfo = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(script))
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrWhiteSpace(parameters))
            {
                scriptProcessInfo.Arguments = parameters;
            }
            else
            {
                _log.Information($"{Definition.Title}: Running DNS script [{script} {parameters}]");
            }

            try
            {
                var process = new Process { StartInfo = scriptProcessInfo };

                var logMessages = new StringBuilder();

                // capture output streams and add to log
                process.OutputDataReceived += (obj, args) =>
                {
                    if (args.Data != null) logMessages.AppendLine(args.Data);
                };

                process.ErrorDataReceived += (obj, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        logMessages.AppendLine($"Error: {args.Data}");
                        _log.Error("Script error: {0}", args.Data);
                    }
                };

                process.Start();

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.WaitForExit((60 + Definition.PropagationDelaySeconds) * 1000);

                // send output to log
                _log.Information(logMessages.ToString());

                if (!process.HasExited)
                {
                    //process still running, kill task?
                }
                else if (process.ExitCode != 0)
                {
                    //process.ExitCode
                }
            }
            catch (Exception exp)
            {
            }
        }
    }
}
