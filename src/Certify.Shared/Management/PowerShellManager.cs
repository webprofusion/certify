using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;

namespace Certify.Management
{
    public class PowerShellManager
    {
        public static async Task<ActionResult> RunScript(CertificateRequestResult result, string scriptFile = null, Dictionary<string, object> parameters = null, string scriptContent = null)
        {
            // argument check for script file existance and .ps1 extension
            FileInfo scriptInfo = null;
            if (scriptContent == null)
            {
                scriptInfo = new FileInfo(scriptFile);
                if (!scriptInfo.Exists)
                {
                    throw new ArgumentException($"File '{scriptFile}' does not exist.");
                }
                if (scriptInfo.Extension != ".ps1")
                {
                    throw new ArgumentException($"File '{scriptFile}' is not a powershell script.");
                }
            }

            var config = SharedUtils.ServiceConfigManager.GetAppServiceConfig();

            try
            {
                // create a new runspace to isolate the scripts
                using (var runspace = RunspaceFactory.CreateRunspace())
                {
                    runspace.Open();

                    // set working directory to the script file's directory
                    if (scriptInfo != null)
                    {
                        runspace.SessionStateProxy.Path.SetLocation(scriptInfo.DirectoryName);
                    }

                    using (var shell = PowerShell.Create())
                    {
                        shell.Runspace = runspace;

                        // ensure execution policy will allow the script to run, default to "Unrestricted", set in service config as "Default" to skip.

                        if (config.PowershellExecutionPolicy != "Default")
                        {
                            shell.AddCommand("Set-ExecutionPolicy")
                                    .AddParameter("ExecutionPolicy", config.PowershellExecutionPolicy)
                                    .AddParameter("Scope", "Process")
                                    .AddParameter("Force")
                                    .Invoke();
                        }

                        // add script command to invoke
                        if (scriptFile != null)
                        {
                            shell.AddCommand(scriptFile);
                        }
                        else
                        {
                            shell.AddScript(scriptContent);
                        }

                        // pass the result to the script if present
                        if (result != null)
                        {
                            shell.AddParameter("result", result);
                        }

                        // pass parameters to script if present
                        if (parameters != null)
                        {
                            foreach (var a in parameters)
                            {
                                shell.AddParameter(a.Key, a.Value);
                            }
                        }

                        // accumulate output
                        var output = new StringBuilder();

                        // capture errors
                        shell.Streams.Error.DataAdded += (sender, args) =>
                        {
                            var error = shell.Streams.Error[args.Index];
                            var src = error.InvocationInfo.MyCommand?.ToString() ?? error.InvocationInfo.InvocationName;
                            output.AppendLine($"{src}: {error}\n{error.InvocationInfo.PositionMessage}");
                        };

                        // capture write-* methods (except write-host)
                        shell.Streams.Warning.DataAdded += (sender, args) => output.AppendLine(shell.Streams.Warning[args.Index].Message);
                        shell.Streams.Debug.DataAdded += (sender, args) => output.AppendLine(shell.Streams.Debug[args.Index].Message);
                        shell.Streams.Verbose.DataAdded += (sender, args) => output.AppendLine(shell.Streams.Verbose[args.Index].Message);

                        var outputData = new PSDataCollection<PSObject>();

                        outputData.DataAdded += (sender, args) =>
                        {
                            // capture all main output
                            var data = outputData[args.Index]?.BaseObject;
                            if (data != null)
                            {
                                output.AppendLine(data.ToString());
                            }
                        };


                        try
                        {
                            var async = shell.BeginInvoke<PSObject, PSObject>(null, outputData);
                            shell.EndInvoke(async);

                            return new ActionResult(output.ToString().TrimEnd('\n'), true);
                        }
                        catch (ParseException ex)
                        {
                            // this should only happen in case of script syntax errors, otherwise
                            // errors would be output via the invoke's error stream
                            output.AppendLine($"{ex.Message}");

                            return new ActionResult(output.ToString().TrimEnd('\n'), false);
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                return new ActionResult($"Error - {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", false);
            }
        }
    }
}
