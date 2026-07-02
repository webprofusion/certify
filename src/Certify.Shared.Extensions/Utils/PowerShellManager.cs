using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.SharedUtils;
using SimpleImpersonation;

namespace Certify.Management
{
    public enum PowerShellExecutionMode
    {
        Automatic,
        InProcess,
        SystemProcess
    }

    public enum PowerShellImpersonationMode
    {
        Default,
        Full,
        FullWithProfile
    }

    public class PowerShellScriptSettings
    {
        public string PowerShellExecutionPolicy { get; set; }
        public CertificateRequestResult Result { get; set; }
        public string ScriptFile { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
        public string ScriptContent { get; set; }
        public Dictionary<string, string> Credentials { get; set; }
        public string LogonType { get; set; }
        public string[] IgnoredCommandExceptions { get; set; }
        public int TimeoutMinutes { get; set; } = 5;
        public bool LaunchNewProcess { get; set; }
        public PowerShellExecutionMode ExecutionMode { get; set; } = PowerShellManager.GetDefaultExecutionMode();
        public PowerShellImpersonationMode ImpersonationMode { get; set; } = PowerShellImpersonationMode.Default;
        public bool LoadUserProfile { get; set; }

        /// <summary>
        /// When true, the noisy PowerShell diagnostic streams (Verbose and Debug) are captured in the
        /// execution log. These streams are suppressed by default and should only be enabled when the
        /// configured log level is Debug or Verbose, because some providers (e.g. Posh-ACME) emit very
        /// detailed request/response information (including headers) to them.
        /// </summary>
        public bool VerboseStreamLogging { get; set; }
    }

    /// <summary>
    /// PowerShell script execution manager. 
    /// Manage the execution of PowerShell scripts, either in-process or by launching a new process.
    /// When launching a separate process, script parameters are marshalled via a temporary encoded payload file
    /// so secrets are not exposed on the process command line.
    /// </summary>
    public partial class PowerShellManager
    {
        private const string PowerShellTelemetryOptOutEnvVar = "POWERSHELL_TELEMETRY_OPTOUT";
        private const string PowerShellTelemetryOptOutValue = "true";
        private const string PowerShellPayloadSecretEnvVar = "CERTIFY_POWERSHELL_PAYLOAD_SECRET";

        public static PowerShellExecutionMode GetDefaultExecutionMode()
        {
            return PowerShellExecutionMode.Automatic;
        }

        public static bool TryParseExecutionMode(string? value, out PowerShellExecutionMode executionMode)
        {
            executionMode = GetDefaultExecutionMode();

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Enum.TryParse(value.Trim(), ignoreCase: true, out executionMode);
        }

        private static bool UsesFullImpersonation(PowerShellImpersonationMode impersonationMode)
        {
            return impersonationMode == PowerShellImpersonationMode.Full || impersonationMode == PowerShellImpersonationMode.FullWithProfile;
        }

        private static bool ShouldLoadUserProfile(PowerShellImpersonationMode impersonationMode)
        {
            return impersonationMode == PowerShellImpersonationMode.FullWithProfile;
        }

        private sealed class PowerShellProcessPayload
        {
            public object Result { get; set; }
            public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Run a PowerShell script, either in-process or by launching a new process.
        /// </summary>
        /// <param name="settings">Settings for the PowerShell script execution.</param>
        /// <returns>ActionResult</returns>
        public static async Task<ActionResult> RunScript(PowerShellScriptSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var priorTelemetryOptOut = Environment.GetEnvironmentVariable(PowerShellTelemetryOptOutEnvVar);
            Environment.SetEnvironmentVariable(PowerShellTelemetryOptOutEnvVar, PowerShellTelemetryOptOutValue);

            try
            {
                if (settings.Credentials?.Any() == true)
                {
                    if (!HasCredentialValue(settings.Credentials, "username") || !HasCredentialValue(settings.Credentials, "password"))
                    {
                        return new ActionResult("Commands credentials require a username and password.", false);
                    }
                }

                // argument check for script file existence and .ps1 extension
                FileInfo scriptInfo = null;
                if (settings.ScriptContent == null)
                {
                    scriptInfo = new FileInfo(settings.ScriptFile);
                    if (!scriptInfo.Exists)
                    {
                        throw new ArgumentException($"File '{settings.ScriptFile}' does not exist.");
                    }

                    if (scriptInfo.Extension.ToLower() != ".ps1")
                    {
                        throw new ArgumentException($"File '{settings.ScriptFile}' is not a powershell script.");
                    }
                }

                if (UsesFullImpersonation(settings.ImpersonationMode))
                {
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        return new ActionResult("PowerShell Full Impersonation is only supported on Windows.", false);
                    }
                }

                var executionMode = ResolveExecutionMode(settings, out var executionModeMessage);

                if (executionMode == PowerShellExecutionMode.SystemProcess)
                {
                    return await ExecutePowershellAsProcess(settings.Result, settings.PowerShellExecutionPolicy, settings.ScriptFile, settings.Parameters, settings.Credentials, settings.LogonType, settings.ScriptContent, executionMode: executionMode, ignoredCommandExceptions: settings.IgnoredCommandExceptions, timeoutMinutes: settings.TimeoutMinutes, executionModeMessage: executionModeMessage, impersonationMode: settings.ImpersonationMode, loadUserProfile: ShouldLoadUserProfile(settings.ImpersonationMode));
                }

                if (executionMode == PowerShellExecutionMode.InProcess)
                {
                    return await Task.FromResult(RunScriptInProcess(settings.PowerShellExecutionPolicy, settings.Result, settings.ScriptFile, settings.Parameters, settings.ScriptContent, settings.Credentials, settings.LogonType, settings.IgnoredCommandExceptions, settings.TimeoutMinutes, scriptInfo, executionModeMessage, settings.VerboseStreamLogging));
                }

                return new ActionResult($"Unknown PowerShell execution mode: {executionMode}", false);
            }
            finally
            {
                Environment.SetEnvironmentVariable(PowerShellTelemetryOptOutEnvVar, priorTelemetryOptOut);
            }
        }

        private static PowerShellExecutionMode ResolveExecutionMode(PowerShellScriptSettings settings, out string executionModeMessage)
        {
            var selectedExecutionMode = settings.ExecutionMode;
            var executionMode = selectedExecutionMode;

            if (UsesFullImpersonation(settings.ImpersonationMode) && executionMode != PowerShellExecutionMode.SystemProcess)
            {
                executionMode = PowerShellExecutionMode.SystemProcess;
                executionModeMessage = $"PowerShell Execution Mode: {executionMode} (selected {selectedExecutionMode}, overridden because Full Impersonation requires a new process).";
                return executionMode;
            }

            if (selectedExecutionMode == PowerShellExecutionMode.Automatic)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (settings.Credentials?.Any() == true)
                    {
                        executionMode = PowerShellExecutionMode.InProcess;
                        executionModeMessage = $"PowerShell Execution Mode: {executionMode} (selected {selectedExecutionMode}, using in-process PowerShell for Windows credential impersonation compatibility).";
                    }
                    else
                    {
                        executionMode = PowerShellExecutionMode.SystemProcess;
                        executionModeMessage = $"PowerShell Execution Mode: {executionMode} (selected {selectedExecutionMode}, using system PowerShell process on Windows by default).";
                    }
                }
                else
                {
                    executionMode = PowerShellExecutionMode.InProcess;
                    executionModeMessage = $"PowerShell Execution Mode: {executionMode} (selected {selectedExecutionMode}, using in-process PowerShell on this platform by default).";
                }

                return executionMode;
            }

            executionModeMessage = $"PowerShell Execution Mode: {executionMode}.";
            return executionMode;
        }

        private static bool HasCredentialValue(Dictionary<string, string> credentials, string key)
        {
            return credentials.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
        }

        private static LogonType GetLogonType(string logonType)
        {
            return logonType?.ToLower() switch
            {
                "network" => LogonType.Network,
                "batch" => LogonType.Batch,
                "service" => LogonType.Service,
                "interactive" => LogonType.Interactive,
                "newcredentials" => LogonType.NewCredentials,
                _ => LogonType.NewCredentials,
            };
        }

        private static ActionResult RunScriptInProcess(string powershellExecutionPolicy, CertificateRequestResult result, string scriptFile, Dictionary<string, object> parameters, string scriptContent, Dictionary<string, string> credentials, string logonType, string[] ignoredCommandExceptions, int timeoutMinutes, FileInfo scriptInfo, string executionModeMessage, bool verboseStreamLogging = false)
        {
            try
            {
                // the result object may be supplied directly or via the parameters collection (e.g. from deployment tasks).
                // resolve it here so the in-process path matches the new-process path and the script still receives its -result argument.
                if (result == null && parameters != null)
                {
                    result = parameters.FirstOrDefault(p => string.Equals(p.Key, "result", StringComparison.OrdinalIgnoreCase)).Value as CertificateRequestResult;
                }

                var iss = InitialSessionState.CreateDefault();

                using var runspace = RunspaceFactory.CreateRunspace(iss);
                runspace.Open();

                if (scriptInfo != null)
                {
                    runspace.SessionStateProxy.Path.SetLocation(scriptInfo.DirectoryName);
                }

                using var shell = PowerShell.Create();
                shell.Runspace = runspace;

                if (credentials?.Any() == true && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // run in-process with impersonation
                    UserCredentials windowsCredentials = null;

                    try
                    {
                        windowsCredentials = GetWindowsCredentials(credentials);
                    }
                    catch
                    {
                        return new ActionResult("Command with Windows Credentials requires username and password.", false);
                    }

                    ActionResult powerShellResult = null;
                    using (var userHandle = windowsCredentials.LogonUser(GetLogonType(logonType)))
                    {
                        WindowsIdentity.RunImpersonated(userHandle, () =>
                        {
                            powerShellResult = InvokePowershellCore(result, powershellExecutionPolicy, scriptFile, parameters, scriptContent, shell, ignoredCommandExceptions: ignoredCommandExceptions, timeoutMinutes: timeoutMinutes, executionModeMessage: executionModeMessage, verboseStreamLogging: verboseStreamLogging);
                        });
                    }

                    return powerShellResult;
                }
                else
                {
                    // run in=process without impersonation
                    return InvokePowershellCore(result, powershellExecutionPolicy, scriptFile, parameters, scriptContent, shell, ignoredCommandExceptions: ignoredCommandExceptions, timeoutMinutes: timeoutMinutes, executionModeMessage: executionModeMessage, verboseStreamLogging: verboseStreamLogging);
                }
            }
            catch (Exception ex)
            {
                return new ActionResult($"Error - {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", false);
            }
        }

        /// <summary>
        /// Get the path to the powershell exe for the requested mode, optionally using a preferred path first.
        /// </summary>
        private static string GetPowershellExePath(string powershellPathPreference = null)
        {
            var searchPaths = new List<string>();

            if (!string.IsNullOrWhiteSpace(powershellPathPreference))
            {
                searchPaths.Add(powershellPathPreference);
            }

            searchPaths.AddRange(GetConfiguredPowerShellSearchPaths());

            // if powershell exe path supplied, use that (with expansion) and check exe exists
            // otherwise detect powershell exe location
            foreach (var exePath in searchPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var filePath = Environment.ExpandEnvironmentVariables(exePath.Trim());
                if (File.Exists(filePath))
                {
                    return filePath;
                }

                if (Path.GetFileName(filePath) == filePath)
                {
                    var pathExe = ResolveExecutableFromPath(filePath);
                    if (!string.IsNullOrWhiteSpace(pathExe))
                    {
                        return pathExe;
                    }
                }
            }

            return null;
        }

        private static List<string> GetConfiguredPowerShellSearchPaths()
        {
            var searchPaths = new List<string>();
            var serviceConfig = GetServiceConfigSafe();

            if (serviceConfig?.CustomPowerShellPaths?.Any() == true)
            {
                searchPaths.AddRange(serviceConfig.CustomPowerShellPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (serviceConfig?.PreferModernPowershell == true)
                {
                    searchPaths.AddRange(GetModernPowerShellSearchPaths());
                    searchPaths.AddRange(GetClassicPowerShellSearchPaths());
                }
                else
                {
                    searchPaths.AddRange(GetClassicPowerShellSearchPaths());
                    searchPaths.AddRange(GetModernPowerShellSearchPaths());
                }
            }
            else
            {
                searchPaths.AddRange(GetModernNonWindowsPowerShellSearchPaths());
            }

            return searchPaths;
        }

        private static Certify.Shared.ServiceConfig GetServiceConfigSafe()
        {
            try
            {
                return ServiceConfigManager.GetAppServiceConfig();
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> GetClassicPowerShellSearchPaths()
        {
            return
            [
                "%WINDIR%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
                "powershell.exe"
            ];
        }

        private static IEnumerable<string> GetModernPowerShellSearchPaths()
        {
            return
            [
                "%PROGRAMFILES%\\PowerShell\\7\\pwsh.exe",
                "pwsh.exe",
                "/usr/bin/pwsh",
                "/snap/bin/pwsh",
                "pwsh"
            ];
        }

        private static IEnumerable<string> GetModernNonWindowsPowerShellSearchPaths()
        {
            return
            [
                "/usr/local/bin/pwsh",
                "/opt/homebrew/bin/pwsh",
                "/usr/bin/pwsh",
                "pwsh"
            ];
        }

        private static string ResolveExecutableFromPath(string executableName)
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            foreach (var pathEntry in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(pathEntry))
                {
                    continue;
                }

                var candidate = Path.Combine(pathEntry, executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private static async Task<ActionResult> ExecutePowershellAsProcess(CertificateRequestResult result, string executionPolicy, string scriptFile, Dictionary<string, object> parameters, Dictionary<string, string> credentials, string logonType, string scriptContent, bool autoConvertBoolean = true, PowerShellExecutionMode executionMode = PowerShellExecutionMode.SystemProcess, string[] ignoredCommandExceptions = null, int timeoutMinutes = 5, string powershellPathPreference = null, string executionModeMessage = null, PowerShellImpersonationMode impersonationMode = PowerShellImpersonationMode.Default, bool loadUserProfile = false)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            var _log = new StringBuilder();

            var commandExe = GetPowershellExePath(powershellPathPreference);

            if (commandExe == null)
            {
                return new ActionResult("Failed to locate powershell executable. Cannot launch as new process.", false);
            }

            _log.AppendLine(executionModeMessage ?? $"PowerShell Execution Mode: {executionMode}.");
            _log.AppendLine($"PowerShell Executable: {commandExe}");

            var resultObj = result ?? parameters?.Where(p => p.Key == "result" && p.Value != null).FirstOrDefault().Value;
            var payloadParameters = BuildProcessPayloadParameters(parameters, autoConvertBoolean);
            var useWrapperScript = resultObj != null || payloadParameters.Any();

            string payloadTempPath = null;
            string payloadSecret = null;
            string wrapperScriptTempPath = null;
            string scriptContentTempPath = null;
            string tempDirectoryPath = null;

            void CleanupTempFiles()
            {
                DeleteTempFile(payloadTempPath, _log, "secure payload file");
                DeleteTempFile(wrapperScriptTempPath, _log, "temp wrapper script");
                DeleteTempFile(scriptContentTempPath, _log, "temp script content file");
                DeleteTempDirectory(tempDirectoryPath, _log);
            }

            var appBasePath = AppContext.BaseDirectory;

            var wrapperScriptPath = Path.Combine([appBasePath, "Scripts", "Internal", "Script-Wrapper.ps1"]);
            var wrapperScriptSourceText = File.ReadAllText(wrapperScriptPath);

            var isUsingCredentials = (credentials != null && credentials.ContainsKey("username") && credentials.ContainsKey("password"));
            var executingUsername = isUsingCredentials && RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? GetWindowsCredentialsUsername(credentials, includeAutoLocalDomain: true) : null;
            var allowCredentialWriteAccess = isUsingCredentials && UsesFullImpersonation(impersonationMode);

            try
            {
                tempDirectoryPath = await CreateSecureTempDirectory(executingUsername, allowCredentialWriteAccess);
            }
            catch (Exception ex)
            {
                return new ActionResult($"Failed to prepare secure temporary PowerShell handoff directory. {ex.Message}", false);
            }

            if (!string.IsNullOrEmpty(scriptContent) && !useWrapperScript)
            {
                try
                {
                    scriptContentTempPath = await CreateSecureTempFile(tempDirectoryPath, ".ps1", executingUsername, allowExecute: true, allowCredentialWriteAccess: allowCredentialWriteAccess);
                    File.WriteAllText(scriptContentTempPath, scriptContent, new UTF8Encoding(false));
                    scriptFile = scriptContentTempPath;
                }
                catch (Exception ex)
                {
                    CleanupTempFiles();
                    return new ActionResult($"Failed to prepare secure temporary PowerShell script content. {ex.Message}", false);
                }
            }

            if (useWrapperScript && !string.IsNullOrEmpty(scriptContent))
            {
                try
                {
                    wrapperScriptSourceText = BuildScriptContentWrapper(wrapperScriptSourceText, scriptContent);
                    wrapperScriptPath = await CreateSecureTempFile(tempDirectoryPath, ".ps1", executingUsername, allowExecute: true, allowCredentialWriteAccess: allowCredentialWriteAccess);
                    wrapperScriptTempPath = wrapperScriptPath;
                    File.WriteAllText(wrapperScriptPath, wrapperScriptSourceText, new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    var err = $"Failed to prepare the secure PowerShell script content wrapper. {ex.Message}";

                    CleanupTempFiles();
                    return new ActionResult(err, false);
                }
            }
            else if (useWrapperScript && isUsingCredentials && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // The impersonating user must be able to read the script wrapper and target script so that the process starting under their credentials can call them.

                try
                {
                    wrapperScriptPath = await CreateSecureTempFile(tempDirectoryPath, ".ps1", executingUsername, allowExecute: true, allowCredentialWriteAccess: allowCredentialWriteAccess);
                    wrapperScriptTempPath = wrapperScriptPath;
                    File.WriteAllText(wrapperScriptPath, wrapperScriptSourceText);

                    if (!string.IsNullOrWhiteSpace(scriptFile))
                    {
                        scriptContentTempPath = await CreateSecureTempFile(tempDirectoryPath, ".ps1", executingUsername, allowExecute: true, allowCredentialWriteAccess: allowCredentialWriteAccess);
                        File.WriteAllBytes(scriptContentTempPath, File.ReadAllBytes(scriptFile));
                        scriptFile = scriptContentTempPath;
                    }
                }
                catch (Exception ex)
                {
                    var err = $"Failed to prepare the secure PowerShell wrapper for alternate credentials. {ex.Message}";

                    CleanupTempFiles();
                    return new ActionResult(err, false);
                }
            }

            try
            {
                if (resultObj != null || payloadParameters.Any())
                {
                    var payload = new PowerShellProcessPayload
                    {
                        Result = resultObj,
                        Parameters = payloadParameters
                    };

                    payloadSecret = CreatePayloadSecret();
                    payloadTempPath = await CreateSecureTempFile(tempDirectoryPath, ".dat", executingUsername, allowCredentialWriteAccess: allowCredentialWriteAccess);
                    File.WriteAllText(payloadTempPath, EncodePayload(payload, payloadSecret), new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                CleanupTempFiles();
                return new ActionResult($"Failed to prepare the secure PowerShell handoff payload. {ex.Message}", false);
            }

            // Always invoke via -File so that quoted parameters (e.g. -scriptFile / -payloadFile) retain their quoting.
            // Using -Command "& '...'" causes the host (notably classic powershell.exe) to re-parse and drop the quotes
            // around trailing arguments, which splits any script path containing spaces into multiple tokens.
            var arguments = useWrapperScript ? $" -File \"{wrapperScriptPath}\"" : $" -File \"{scriptFile}\"";

            if (parameters?.Any(p => p.Key.ToLower() == "executionpolicy") == true)
            {
                executionPolicy = parameters.FirstOrDefault(p => p.Key.ToLower() == "executionpolicy").Value?.ToString();
            }

            if (!string.IsNullOrEmpty(executionPolicy))
            {
                arguments = $"-ExecutionPolicy {executionPolicy} {arguments}";
            }

            if (isUsingCredentials && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                arguments = $"-NoProfile {arguments}";
            }

            if (useWrapperScript && !string.IsNullOrWhiteSpace(scriptFile))
            {
                arguments += $" -scriptFile \"{scriptFile}\"";
            }

            if (!string.IsNullOrWhiteSpace(payloadTempPath))
            {
                arguments += $" -payloadFile \"{payloadTempPath}\"";
            }

            var scriptProcessInfo = new ProcessStartInfo()
            {
                RedirectStandardInput = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                FileName = commandExe,
                Arguments = arguments,
                Verb = "RunAs",
                WorkingDirectory = Path.GetDirectoryName(wrapperScriptPath ?? scriptFile) ?? AppContext.BaseDirectory
            };

            scriptProcessInfo.Environment[PowerShellTelemetryOptOutEnvVar] = PowerShellTelemetryOptOutValue;

            if (isUsingCredentials && RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !string.IsNullOrWhiteSpace(tempDirectoryPath))
            {
                scriptProcessInfo.Environment["TEMP"] = tempDirectoryPath;
                scriptProcessInfo.Environment["TMP"] = tempDirectoryPath;
            }

            if (!string.IsNullOrWhiteSpace(payloadTempPath))
            {
                scriptProcessInfo.Environment[PowerShellPayloadSecretEnvVar] = payloadSecret;
            }

            if (UsesFullImpersonation(impersonationMode))
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    CleanupTempFiles();
                    return new ActionResult("PowerShell Full Impersonation is only supported on Windows.", false);
                }

                if (!isUsingCredentials)
                {
                    CleanupTempFiles();
                    return new ActionResult("PowerShell Full Impersonation requires username and password credentials.", false);
                }

                _log.AppendLine($"Launching Process {commandExe} using full Windows impersonation as {executingUsername}.");
                if (loadUserProfile)
                {
                    _log.AppendLine("PowerShell Full Impersonation: loading user profile and environment.");
                }
                else
                {
                    _log.AppendLine("PowerShell Full Impersonation: user profile loading is disabled.");
                }

                return RunProcessWithFullImpersonation(scriptProcessInfo, credentials, logonType, loadUserProfile, timeoutMinutes, _log, CleanupTempFiles);
            }

            // launch process with user credentials set
            if (credentials != null && credentials.ContainsKey("username") && credentials.ContainsKey("password"))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {

                    var username = credentials["username"];
                    var pwd = credentials["password"];

                    credentials.TryGetValue("domain", out var domain);

                    if (domain == null && !username.Contains(".\\") && !username.Contains("@"))
                    {
                        domain = ".";
                    }

                    // Note: process running as local system cannot start a process as different user due to lack of security token context
                    scriptProcessInfo.UserName = username;
                    scriptProcessInfo.Domain = domain;

                    var sPwd = new SecureString();
                    foreach (var c in pwd)
                    {
                        sPwd.AppendChar(c);
                    }

                    sPwd.MakeReadOnly();

                    scriptProcessInfo.Password = sPwd;

                    _log.AppendLine($"Launching Process {commandExe} using alternate Windows credentials.");
                }
                else
                {
                    _log.AppendLine($"Running PowerShell As New Process: Running as specific user credentials are not supported on this platform.");
                }
            }

            try
            {
                var process = new Process { StartInfo = scriptProcessInfo };

                var logMessages = new StringBuilder();
                var processErrorMessages = new List<string>();
                var processErrorMessagesSync = new object();

                // capture output streams and add to log
                process.OutputDataReceived += (obj, a) =>
                {
                    if (a.Data != null)
                    {
                        logMessages.AppendLine(a.Data);
                    }
                };

                process.ErrorDataReceived += (obj, a) =>
                {
                    if (!string.IsNullOrWhiteSpace(a.Data))
                    {
                        lock (processErrorMessagesSync)
                        {
                            processErrorMessages.Add(a.Data);
                        }

                        logMessages.AppendLine($"Error: {a.Data}");
                    }
                };

                try
                {
                    process.Start();

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (process.WaitForExit((timeoutMinutes * 60) * 1000))
                    {
                        process.WaitForExit();
                    }
                }
                catch (Exception exp)
                {
                    _log.AppendLine("Error Running Script: " + exp.ToString());
                }

                // append output to main log
                _log.Append(logMessages.ToString());

                if (!process.HasExited)
                {
                    // process still running, attempt graceful termination then force kill if required
                    try
                    {
                        process.CloseMainWindow();
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (!process.WaitForExit(5000))
                        {
                            process.Kill();
                            process.WaitForExit(5000);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.AppendLine($"Warning: Timed out PowerShell process could not be terminated cleanly. {ex.Message}");
                    }

                    _log.AppendLine("Warning: Script ran but took too long to exit and was terminated.");
                    return new ActionResult { IsSuccess = false, Message = _log.ToString() };
                }
                else if (process.ExitCode != 0)
                {
                    _log.AppendLine("Warning: Script exited with the following ExitCode: " + process.ExitCode);
                    return new ActionResult { IsSuccess = false, Message = _log.ToString() };
                }

                List<string> unhandledProcessErrorMessages;
                lock (processErrorMessagesSync)
                {
                    unhandledProcessErrorMessages = GetUnhandledProcessErrorMessages(processErrorMessages, ignoredCommandExceptions);
                }

                if (unhandledProcessErrorMessages.Any())
                {
                    _log.AppendLine("Warning: Script reported one or more PowerShell errors.");
                    return new ActionResult { IsSuccess = false, Message = _log.ToString() };
                }

                return new ActionResult { IsSuccess = true, Message = _log.ToString() };

            }
            catch (Exception exp)
            {
                _log.AppendLine("Error: " + exp.ToString());
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = _log.ToString()
                };
            }
            finally
            {
                CleanupTempFiles();
            }
        }

        private static string BuildScriptContentWrapper(string wrapperScriptSourceText, string scriptContent)
        {
            const string wrappedScriptInvocation = "& $scriptFile @wrappedArguments";

            if (string.IsNullOrWhiteSpace(wrapperScriptSourceText))
            {
                throw new InvalidOperationException("PowerShell wrapper script template is missing the wrapped script invocation.");
            }

            var embeddedScriptInvocation = $"$__certifyScriptContent = {{{Environment.NewLine}{scriptContent}{Environment.NewLine}}}{Environment.NewLine}& $__certifyScriptContent @wrappedArguments";

            if (wrapperScriptSourceText.Contains(wrappedScriptInvocation, StringComparison.Ordinal))
            {
                return wrapperScriptSourceText.Replace(wrappedScriptInvocation, embeddedScriptInvocation, StringComparison.Ordinal);
            }

            throw new InvalidOperationException("PowerShell wrapper script template is missing the wrapped script invocation.");
        }

        private static void DeleteTempFile(string tempFilePath, StringBuilder log, string description)
        {
            if (string.IsNullOrWhiteSpace(tempFilePath))
            {
                return;
            }

            try
            {
                File.Delete(tempFilePath);
            }
            catch
            {
                log.AppendLine($"Running Powershell As New Process: Could not delete {description}.");
            }
        }

        private static void DeleteTempDirectory(string tempDirectoryPath, StringBuilder log)
        {
            if (string.IsNullOrWhiteSpace(tempDirectoryPath))
            {
                return;
            }

            try
            {
                Directory.Delete(tempDirectoryPath, recursive: false);
            }
            catch
            {
                log.AppendLine("Running Powershell As New Process: Could not delete temp handoff directory.");
            }
        }

        private static Dictionary<string, object> BuildProcessPayloadParameters(Dictionary<string, object> parameters, bool autoConvertBoolean)
        {
            var payloadParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (parameters == null)
            {
                return payloadParameters;
            }

            foreach (var parameter in parameters)
            {
                if (parameter.Key.Equals("result", StringComparison.OrdinalIgnoreCase)
                    || parameter.Key.Equals("executionpolicy", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = parameter.Value;

                if (autoConvertBoolean && value is string stringValue)
                {
                    if (stringValue.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        value = true;
                    }
                    else if (stringValue.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        value = false;
                    }
                }

                payloadParameters[parameter.Key] = value;
            }

            return payloadParameters;
        }

        private static bool IsManagerOnlyParameter(string parameterName)
        {
            return parameterName.Equals("result", StringComparison.OrdinalIgnoreCase)
                || parameterName.Equals("executionpolicy", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> GetUnhandledProcessErrorMessages(IReadOnlyCollection<string> processErrorMessages, string[] ignoredCommandExceptions)
        {
            if (processErrorMessages == null || processErrorMessages.Count == 0)
            {
                return new List<string>();
            }

            var ignoredCommands = new HashSet<string>(
                ignoredCommandExceptions?.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (ignoredCommands.Count == 0)
            {
                return processErrorMessages.ToList();
            }

            var unhandledErrors = new List<string>();
            var currentErrorIgnored = false;

            foreach (var errorMessage in processErrorMessages)
            {
                if (string.IsNullOrWhiteSpace(errorMessage))
                {
                    currentErrorIgnored = false;
                    continue;
                }

                if (TryGetProcessErrorCommandName(errorMessage, out var commandName))
                {
                    currentErrorIgnored = ignoredCommands.Contains(commandName);
                }

                if (!currentErrorIgnored)
                {
                    unhandledErrors.Add(errorMessage);
                }
            }

            return unhandledErrors;
        }

        private static bool TryGetProcessErrorCommandName(string processErrorMessage, out string commandName)
        {
            commandName = null;

            if (string.IsNullOrWhiteSpace(processErrorMessage))
            {
                return false;
            }

            var separatorIndex = processErrorMessage.IndexOf(" : ", StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                return false;
            }

            commandName = processErrorMessage[..separatorIndex].Trim();
            return !string.IsNullOrWhiteSpace(commandName) && !commandName.Contains(' ');
        }

        private static async Task<string> CreateSecureTempDirectory(string fullUsername = null, bool allowCredentialWriteAccess = false)
        {
            var baseTempPath = GetPowerShellHandoffTempBasePath(fullUsername);
            Directory.CreateDirectory(baseTempPath);

            var directoryPath = Path.Combine(baseTempPath, $"certify-powershell-{Guid.NewGuid():N}");

            Directory.CreateDirectory(directoryPath);

            await EnsureSecureTempDirectoryAccess(directoryPath, fullUsername, allowCredentialWriteAccess);

            return directoryPath;
        }

        private static string GetPowerShellHandoffTempBasePath(string fullUsername)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !string.IsNullOrWhiteSpace(fullUsername))
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Certify", "Temp");
            }

            return Path.GetTempPath();
        }

        private static async Task<string> CreateSecureTempFile(string directoryPath, string extension, string fullUsername = null, bool allowExecute = false, bool allowCredentialWriteAccess = false)
        {
            var filePath = Path.Combine(directoryPath, $"certify-powershell-{Guid.NewGuid():N}{extension}");

            using (File.Create(filePath))
            {
            }

            await EnsureSecureTempFileAccess(filePath, fullUsername, allowExecute, allowCredentialWriteAccess);

            return filePath;
        }

        private static async Task EnsureSecureTempDirectoryAccess(string directoryPath, string fullUsername, bool allowCredentialWriteAccess = false)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!await ApplyDirectoryACL(directoryPath, fullUsername, allowCredentialWriteAccess))
                {
                    throw new InvalidOperationException($"Could not apply secure access controls to '{directoryPath}'.");
                }
            }
            else
            {
                if (!TrySetRestrictedUnixFileMode(directoryPath, allowExecute: true))
                {
                    throw new InvalidOperationException($"Could not apply secure directory mode to '{directoryPath}'.");
                }
            }
        }

        private static string CreatePayloadSecret()
        {
            var secret = new byte[32];
            RandomNumberGenerator.Fill(secret);

            return Convert.ToBase64String(secret);
        }

        private static string EncodePayload(PowerShellProcessPayload payload, string payloadSecret)
        {
            var payloadJson = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            var plainText = Encoding.UTF8.GetBytes(payloadJson);
            var masterKey = Convert.FromBase64String(payloadSecret);

            using var hmac = new HMACSHA256(masterKey);
            var encryptionKey = hmac.ComputeHash(Encoding.UTF8.GetBytes("Certify.PowerShell.Payload.Encryption"));
            var authenticationKey = hmac.ComputeHash(Encoding.UTF8.GetBytes("Certify.PowerShell.Payload.Authentication"));

            using var aes = Aes.Create();
            aes.Key = encryptionKey;
            aes.GenerateIV();

            byte[] cipherText;
            using (var encryptor = aes.CreateEncryptor())
            {
                cipherText = encryptor.TransformFinalBlock(plainText, 0, plainText.Length);
            }

            var version = Encoding.ASCII.GetBytes("CPS1");
            var authenticatedData = CombineBytes(version, aes.IV, cipherText);

            using var payloadHmac = new HMACSHA256(authenticationKey);
            var signature = payloadHmac.ComputeHash(authenticatedData);

            return Convert.ToBase64String(CombineBytes(authenticatedData, signature));
        }

        private static byte[] CombineBytes(params byte[][] values)
        {
            var length = values.Sum(v => v.Length);
            var output = new byte[length];
            var offset = 0;

            foreach (var value in values)
            {
                Buffer.BlockCopy(value, 0, output, offset, value.Length);
                offset += value.Length;
            }

            return output;
        }

        private static async Task EnsureSecureTempFileAccess(string filePath, string fullUsername, bool allowExecute = false, bool allowCredentialWriteAccess = false)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!await ApplyFileACL(filePath, fullUsername, allowExecute, allowCredentialWriteAccess))
                {
                    throw new InvalidOperationException($"Could not apply secure access controls to '{filePath}'.");
                }
            }
            else
            {
                if (!TrySetRestrictedUnixFileMode(filePath, allowExecute))
                {
                    throw new InvalidOperationException($"Could not apply secure file mode to '{filePath}'.");
                }
            }
        }

        private static bool TrySetRestrictedUnixFileMode(string filePath, bool allowExecute)
        {
            try
            {
                var setUnixFileModeMethod = typeof(File).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "SetUnixFileMode" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(string));

                if (setUnixFileModeMethod == null)
                {
                    return false;
                }

                var unixFileModeType = setUnixFileModeMethod.GetParameters()[1].ParameterType;
                var modeNames = allowExecute
                    ? "UserRead, UserWrite, UserExecute"
                    : "UserRead, UserWrite";
                var mode = Enum.Parse(unixFileModeType, modeNames);

                setUnixFileModeMethod.Invoke(null, [filePath, mode]);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> ApplyFileACL(string filePath, string fullUsername, bool allowExecute = false, bool allowCredentialWriteAccess = false)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var accessControl = new FileSecurity();
                    var currentIdentity = WindowsIdentity.GetCurrent();
                    var currentUser = currentIdentity.User;

                    if (currentUser == null)
                    {
                        return false;
                    }

                    accessControl.SetOwner(currentUser);
                    accessControl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                    accessControl.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, AccessControlType.Allow));
                    accessControl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
                    accessControl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));

                    if (!string.IsNullOrWhiteSpace(fullUsername))
                    {
                        var fileRights = allowCredentialWriteAccess ? FileSystemRights.Modify : allowExecute ? FileSystemRights.ReadAndExecute : FileSystemRights.Read;
                        accessControl.AddAccessRule(new FileSystemAccessRule(fullUsername, fileRights, AccessControlType.Allow));
                    }

                    fileInfo.SetAccessControl(accessControl);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                return await Task.FromResult(false);
            }
        }

        private static async Task<bool> ApplyDirectoryACL(string directoryPath, string fullUsername, bool allowCredentialWriteAccess = false)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var directoryInfo = new DirectoryInfo(directoryPath);
                    var accessControl = new DirectorySecurity();
                    var currentIdentity = WindowsIdentity.GetCurrent();
                    var currentUser = currentIdentity.User;

                    if (currentUser == null)
                    {
                        return false;
                    }

                    accessControl.SetOwner(currentUser);
                    accessControl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                    accessControl.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    accessControl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    accessControl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));

                    if (!string.IsNullOrWhiteSpace(fullUsername))
                    {
                        var directoryRights = allowCredentialWriteAccess ? FileSystemRights.Modify : FileSystemRights.ReadAndExecute;
                        accessControl.AddAccessRule(new FileSystemAccessRule(fullUsername, directoryRights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    }

                    directoryInfo.SetAccessControl(accessControl);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                return await Task.FromResult(false);
            }
        }

        private static ActionResult InvokePowershellCore(CertificateRequestResult result, string executionPolicy, string scriptFile, Dictionary<string, object> parameters, string scriptContent, PowerShell shell, bool autoConvertBoolean = true, string[] ignoredCommandExceptions = null, int timeoutMinutes = 5, string executionModeMessage = null, bool verboseStreamLogging = false)
        {
            // ensure execution policy will allow the script to run, default to system default, default policy is set in service config object 

            // option to set execution policy as a parameter at task level
            if (parameters?.Any(p => p.Key.ToLower() == "executionpolicy") == true)
            {
                executionPolicy = parameters.FirstOrDefault(p => p.Key.ToLower() == "executionpolicy").Value?.ToString();
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // on windows we may need to set execution policy depending on user preferences
                if (!string.IsNullOrEmpty(executionPolicy))
                {
                    shell.AddCommand("Set-ExecutionPolicy")
                            .AddParameter("ExecutionPolicy", executionPolicy)
                            .AddParameter("Scope", "Process")
                            .AddParameter("Force")
                            .Invoke();

                    shell.Commands.Clear();
                }
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
                    if (IsManagerOnlyParameter(a.Key))
                    {
                        continue;
                    }

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

            output.AppendLine(executionModeMessage ?? $"PowerShell Execution Mode: {PowerShellExecutionMode.InProcess}.");
            output.AppendLine($"PowerShell Host: {System.Management.Automation.PSVersionInfo.PSEdition} {System.Management.Automation.PSVersionInfo.PSVersion}");

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

            // capture write-* methods including write-host / write-information (Information stream)

            // TODO: one of these streams may be causing ssh hang when ssh spawned as part of script..

            shell.Streams.Warning.DataAdded += (sender, args) => output.AppendLine(shell.Streams.Warning[args.Index].Message);

            // The Verbose and Debug streams are very noisy for some providers (e.g. Posh-ACME emits full
            // HTTP request/response detail including headers), so only capture them when verbose stream
            // logging is enabled, i.e. when the configured log level is Debug or Verbose.
            if (verboseStreamLogging)
            {
                shell.Streams.Debug.DataAdded += (sender, args) => output.AppendLine(shell.Streams.Debug[args.Index].Message);
                shell.Streams.Verbose.DataAdded += (sender, args) => output.AppendLine(shell.Streams.Verbose[args.Index].Message);
            }

            // capture Write-Host / Write-Information output so in-process logging matches the new-process host output
            shell.Streams.Information.DataAdded += (sender, args) =>
            {
                var informationRecord = shell.Streams.Information[args.Index];
                if (informationRecord != null)
                {
                    output.AppendLine(informationRecord.ToString());
                }
            };

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

                var maxWait = 60 * timeoutMinutes; // N min timeout
                var currentWait = 0;
                var pollSeconds = 5;

                var timeoutOccurred = false;

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

                if (timeoutOccurred)
                {
                    try
                    {
                        shell.Stop();
                    }
                    catch (Exception ex)
                    {
                        output.AppendLine($"Warning: Failed to stop timed out powershell task cleanly. {ex.Message}");
                    }
                }

                try
                {
                    if (async.IsCompleted)
                    {
                        shell.EndInvoke(async);
                        output.Append($"Powershell Task Completed.");
                    }
                }
                catch (RuntimeException ex)
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

        public static string GetWindowsCredentialsUsername(Dictionary<string, string> credentials, bool includeAutoLocalDomain = false)
        {
            var username = credentials["username"];

            credentials.TryGetValue("domain", out var domain);

            if (includeAutoLocalDomain)
            {
                if (username.StartsWith(@".\", StringComparison.Ordinal))
                {
                    return $"{Environment.MachineName}\\{username.Substring(2)}";
                }

                if (domain == null && !username.Contains(".\\") && !username.Contains("@"))
                {
                    domain = Environment.MachineName;
                }
            }

            if (domain != null)
            {
                return $"{domain}\\{username}";
            }
            else
            {
                return username;
            }
        }
    }
}
