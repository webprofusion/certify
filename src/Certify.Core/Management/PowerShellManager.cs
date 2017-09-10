using Certify.Models;
using System;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Management
{
    public class PowerShellManager
    {
        public static async Task<string> RunScript(CertificateRequestResult result, string scriptFile)
        {
            // argument check for script file existance and .ps1 extension
            var scriptInfo = new FileInfo(scriptFile);
            if (!scriptInfo.Exists)
            {
                throw new ArgumentException($"File '{scriptFile}' does not exist.");
            }
            if (scriptInfo.Extension != ".ps1")
            {
                throw new ArgumentException($"File '{scriptFile}' is not a powershell script.");
            }

            try
            {
                // create a new runspace to isolate the scripts
                using (var runspace = RunspaceFactory.CreateRunspace())
                {
                    runspace.Open();
                    
                    // set working directory to the script file's directory 
                    runspace.SessionStateProxy.Path.SetLocation(scriptInfo.DirectoryName);

                    using (PowerShell shell = PowerShell.Create())
                    {
                        shell.Runspace = runspace;

                        // ensure execution policy will allow the script to run
                        shell.AddCommand("Set-ExecutionPolicy")
                                .AddParameter("ExecutionPolicy", "Unrestricted")
                                .AddParameter("Scope", "Process")
                                .AddParameter("Force")
                                .Invoke();

                        // add script command to invoke
                        shell.AddCommand(scriptFile);

                        // pass the result to the script
                        shell.AddParameter("result", result);

                        // accumulate output
                        var output = new StringBuilder();

                        // capture errors
                        shell.Streams.Error.DataAdded += (sender, args) =>
                        {
                            var error = shell.Streams.Error[args.Index];
                            string src = error.InvocationInfo.MyCommand?.ToString() ?? error.InvocationInfo.InvocationName;
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
                            object data = outputData[args.Index]?.BaseObject;
                            if (data != null) output.AppendLine(data.ToString());
                        };
                        await Task.Run(() =>
                        {
                            try
                            {
                                var async = shell.BeginInvoke<PSObject, PSObject>(null, outputData);
                                shell.EndInvoke(async);
                            }
                            catch (ParseException ex)
                            {
                                // this should only happen in case of script syntax errors, otherwise
                                // errors would be output via the invoke's error stream 
                                output.AppendLine($"{ex.Message}");
                            }
                        });
                        return output.ToString().TrimEnd('\n');
                    }
                }
            }
            catch (Exception ex)
            {
                return $"Error - {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
            }
        }
    }
}
