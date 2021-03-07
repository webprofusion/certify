using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using SimpleImpersonation;

namespace Certify.Management
{
    public class PowerShellManager
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="powershellExecutionPolicy">Unrestricted etc, </param>
        /// <param name="result"></param>
        /// <param name="scriptFile"></param>
        /// <param name="parameters"></param>
        /// <param name="scriptContent"></param>
        /// <param name="credentials"></param>
        /// <returns></returns>
        public static async Task<ActionResult> RunScript(
            string powershellExecutionPolicy,
            CertificateRequestResult result = null,
            string scriptFile = null,
            Dictionary<string, object> parameters = null,
            string scriptContent = null,
            Dictionary<string, string> credentials = null,
            string logonType = null,
            string[] ignoredCommandExceptions = null
            )
        {
            // argument check for script file existence and .ps1 extension
            FileInfo scriptInfo = null;
            if (scriptContent == null)
            {
                scriptInfo = new FileInfo(scriptFile);
                if (!scriptInfo.Exists)
                {
                    throw new ArgumentException($"File '{scriptFile}' does not exist.");
                }
                if (scriptInfo.Extension.ToLower() != ".ps1")
                {
                    throw new ArgumentException($"File '{scriptFile}' is not a powershell script.");
                }
            }

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

                        if (credentials != null && credentials.Any())
                        {
                            // run as windows user
                            UserCredentials windowsCredentials = null;

                            if (credentials != null && credentials.Count > 0)
                            {
                                try
                                {
                                    windowsCredentials = GetWindowsCredentials(credentials);
                                }
                                catch
                                {
                                    var err = "Command with Windows Credentials requires username and password.";

                                    return new ActionResult(err, false);
                                }
                            }

                            // logon type affects the range of abilities the impersonated user has
                            var _defaultLogonType = LogonType.NewCredentials;

                            if (logonType == "network")
                            {
                                _defaultLogonType = LogonType.Network;
                            }
                            else if (logonType == "batch")
                            {
                                _defaultLogonType = LogonType.Batch;
                            }
                            else if (logonType == "service")
                            {
                                _defaultLogonType = LogonType.Service;
                            }
                            else if (logonType == "interactive")
                            {
                                _defaultLogonType = LogonType.Interactive;
                            }
                            else if (logonType == "newcredentials")
                            {
                                _defaultLogonType = LogonType.NewCredentials;
                            }

                            return Impersonation.RunAsUser(windowsCredentials, _defaultLogonType, () =>
                          {
                              // run as current user
                              return InvokePowershell(result, powershellExecutionPolicy, scriptFile, parameters, scriptContent, shell, ignoredCommandExceptions: ignoredCommandExceptions);
                          });
                        }
                        else
                        {
                            // run as current user
                            return InvokePowershell(result, powershellExecutionPolicy, scriptFile, parameters, scriptContent, shell, ignoredCommandExceptions: ignoredCommandExceptions);
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                return new ActionResult($"Error - {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", false);
            }
        }

        private static ActionResult InvokePowershell(CertificateRequestResult result, string executionPolicy, string scriptFile, Dictionary<string, object> parameters, string scriptContent, PowerShell shell, bool autoConvertBoolean = true, string[] ignoredCommandExceptions = null)
        {
            // ensure execution policy will allow the script to run, default to system default, default policy is set in service config object 

            if (!string.IsNullOrEmpty(executionPolicy))
            {
                shell.AddCommand("Set-ExecutionPolicy")
                        .AddParameter("ExecutionPolicy", executionPolicy)
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
                    var val = a.Value;
                    if (autoConvertBoolean)
                    {

                        if (val != null && val?.ToString().ToLower() == "true")
                        {
                            val = true;
                        }
                        else if (val != null && val?.ToString().ToLower() == "false")
                        {
                            val = false;
                        }

                    }
                    shell.AddParameter(a.Key, val);
                }
            }

            var errors = new List<string>();

            // accumulate output
            var output = new StringBuilder();

            // capture errors

            if (ignoredCommandExceptions == null)
            {
                ignoredCommandExceptions = new string[] { };
            }

            shell.Streams.Error.DataAdded += (sender, args) =>
                {
                    var error = shell.Streams.Error[args.Index];
                    var src = error.InvocationInfo.MyCommand?.ToString() ?? error.InvocationInfo.InvocationName;
                    var msg = $"{src}: {error}\n{error.InvocationInfo.PositionMessage}";
                    if (!ignoredCommandExceptions.Contains(error.InvocationInfo.MyCommand?.Name))
                    {
                        errors.Add(msg);
                    }
                };

            // capture write-* methods (except write-host)

            // TODO: one of these streams may be causing ssh hang when ssh spawned as part of script..

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

                var maxWait = 60 * 5; // 5 min timeout
                var currentWait = 0;
                var pollSeconds = 5;

                bool timeoutOccurred = false;

                while (!timeoutOccurred && !async.AsyncWaitHandle.WaitOne(pollSeconds * 1000, false))
                {
                    // poll while async task is still running
                    currentWait += pollSeconds;

                    if (currentWait <= maxWait)
                    {
                        output.AppendLine($"Waiting for powershell to complete..{currentWait}s");
                    }
                    else
                    {
                        output.AppendLine($"Timeout waiting for powershell to complete ({currentWait}s)");
                        errors.Add($"Script did not complete in the required time. ({maxWait}s)");
                        timeoutOccurred = true;
                    }
                }

                try
                {
                    if (async.IsCompleted)
                    {
                        shell.EndInvoke(async);
                        output.AppendLine($"Powershell Task Completed.");
                    }
                }
                catch (System.Management.Automation.RuntimeException ex)
                {
                    errors.Add($"{ex.ErrorRecord} {ex.ErrorRecord.ScriptStackTrace}");
                }
                catch (Exception ex)
                {
                    errors.Add($"Script invoke failed: {ex}");
                }

                if (errors.Any())
                {
                    foreach (var e in errors)
                    {
                        output.AppendLine("Error: " + e);
                    }
                }
                return new ActionResult(output.ToString().TrimEnd('\n'), !errors.Any());
            }
            catch (ParseException ex)
            {
                // this should only happen in case of script syntax errors, otherwise
                // errors would be output via the invoke's error stream
                output.AppendLine($"{ex.Message}");

                return new ActionResult(output.ToString().TrimEnd('\n'), false);
            }
        }

        public static UserCredentials GetWindowsCredentials(Dictionary<string, string> credentials)
        {
            UserCredentials windowsCredentials;

            var username = credentials["username"];
            var pwd = credentials["password"];

            credentials.TryGetValue("domain", out var domain);

            if (domain == null && !username.Contains(".\\") && !username.Contains("@"))
            {
                domain = ".";
            }

            if (domain != null)
            {
                windowsCredentials = new UserCredentials(domain, username, pwd);
            }
            else
            {
                windowsCredentials = new UserCredentials(username, pwd);
            }

            return windowsCredentials;
        }
    }
}
