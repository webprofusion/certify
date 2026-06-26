using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests
{
    [TestClass]
    public class PowerShellManagerTests
    {
        private const string TestOutputPath = @"C:\Temp\Certify\TestOutput\TestPSOutput.txt";
        private const string ScriptRelativePath = "Assets\\Powershell\\Simple.ps1";
        private const string PowerShellVersionPrefix = "PowerShellVersion: ";

        [TestMethod, Description("Test Script runs OK")]
        public async Task TestLoadManagedCertificates()
        {
            CleanupOutput();

            await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                Result = new CertificateRequestResult(new ManagedCertificate()),
                ScriptFile = GetScriptPath()
            });

            var outputExists = File.Exists(TestOutputPath);
            Assert.IsTrue(outputExists, "Powershell output file should exist");

            CleanupOutput();
        }

        [TestMethod, Description("Test Script runs OK in system process mode with secure payload parameters")]
        public async Task TestLoadManagedCertificatesSystemProcessSecurePayload()
        {
            CleanupOutput();

            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                Result = new CertificateRequestResult(new ManagedCertificate()),
                ScriptFile = GetScriptPath(),
                Parameters = new Dictionary<string, object>
                {
                    ["message"] = "secret-from-payload",
                    ["flag"] = "true"
                },
                ExecutionMode = PowerShellExecutionMode.SystemProcess
            });

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.IsTrue(File.Exists(TestOutputPath), "Powershell output file should exist");

            var output = File.ReadAllText(TestOutputPath);
            StringAssert.Contains(output, "Message: secret-from-payload");
            StringAssert.Contains(output, "Flag: True");

            CleanupOutput();
        }

        [TestMethod, Description("Test explicit PowerShell execution modes without parameters")]
        [DataRow(PowerShellExecutionMode.Automatic)]
        [DataRow(PowerShellExecutionMode.InProcess)]
        [DataRow(PowerShellExecutionMode.SystemProcess)]
        public async Task TestExecutionModeWithoutParameters(PowerShellExecutionMode executionMode)
        {
            var result = await RunSimpleScript(executionMode);

            Assert.IsTrue(result.IsSuccess, result.Message);
            AssertOutputContains("Message: ");
            AssertOutputContains("Flag: False");

            CleanupOutput();
        }

        [TestMethod, Description("Test explicit PowerShell execution modes with parameters")]
        [DataRow(PowerShellExecutionMode.Automatic)]
        [DataRow(PowerShellExecutionMode.InProcess)]
        [DataRow(PowerShellExecutionMode.SystemProcess)]
        public async Task TestExecutionModeWithParameters(PowerShellExecutionMode executionMode)
        {
            var result = await RunSimpleScript(
                executionMode,
                new Dictionary<string, object>
                {
                    ["message"] = $"message-from-{executionMode}",
                    ["flag"] = "true",
                    ["executionpolicy"] = "Unrestricted"
                });

            Assert.IsTrue(result.IsSuccess, result.Message);
            AssertOutputContains($"Message: message-from-{executionMode}");
            AssertOutputContains("Flag: True");
            AssertOutputContains("ExecutionPolicyParameter: ");

            CleanupOutput();
        }

        [TestMethod, Description("Incomplete credentials fail fast")]
        public async Task TestIncompleteCredentialsFailFast()
        {
            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                ScriptFile = GetScriptPath(),
                Credentials = new Dictionary<string, string>
                {
                    ["username"] = "test-user"
                }
            });

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.Message, "username and password");
        }

        [TestMethod, Description("Automatic and in-process modes report the expected PowerShell versions")]
        public async Task TestAutomaticAndInProcessModesReportExpectedPowerShellVersions()
        {
            var automaticVersion = await RunSimpleScriptAndGetPowerShellVersion(PowerShellExecutionMode.Automatic);
            var inProcessVersion = await RunSimpleScriptAndGetPowerShellVersion(PowerShellExecutionMode.InProcess);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.AreNotEqual(automaticVersion, inProcessVersion, $"Automatic and InProcess should report different PowerShell versions on Windows when Automatic uses the system process host. Automatic: {automaticVersion}; InProcess: {inProcessVersion}");
            }
            else
            {
                Assert.AreEqual(automaticVersion, inProcessVersion, $"Automatic should resolve to the in-process PowerShell host on this platform. Automatic: {automaticVersion}; InProcess: {inProcessVersion}");
            }
        }

        [TestMethod, Description("Default execution mode reports the expected platform host")]
        public async Task TestDefaultExecutionModeReportsExpectedPlatformHost()
        {
            CleanupOutput();

            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                Result = new CertificateRequestResult(new ManagedCertificate()),
                ScriptFile = GetScriptPath()
            });

            Assert.IsTrue(result.IsSuccess, result.Message);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                StringAssert.Contains(result.Message, "PowerShell Execution Mode: SystemProcess");
            }
            else
            {
                StringAssert.Contains(result.Message, "PowerShell Execution Mode: InProcess");
                StringAssert.Contains(result.Message, "PowerShell Host:");
            }

            CleanupOutput();
        }

        [TestMethod, Description("Null settings throws")]
        public async Task TestNullSettingsThrows()
        {
            await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => PowerShellManager.RunScript(null));
        }

        [TestMethod, Description("Missing script file throws")]
        public async Task TestMissingScriptFileThrows()
        {
            var missingScript = Path.Combine(AppContext.BaseDirectory, "Assets\\Powershell\\Missing.ps1");

            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() => PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                ScriptFile = missingScript
            }));

            StringAssert.Contains(ex.Message, "does not exist");
        }

        [TestMethod, Description("Non PowerShell script file throws")]
        public async Task TestNonPowerShellScriptFileThrows()
        {
            var scriptPath = CreateTempScript("txt", "not powershell");

            try
            {
                var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() => PowerShellManager.RunScript(new PowerShellScriptSettings
                {
                    PowerShellExecutionPolicy = "Unrestricted",
                    ScriptFile = scriptPath
                }));

                StringAssert.Contains(ex.Message, "is not a powershell script");
            }
            finally
            {
                DeleteTempScript(scriptPath);
            }
        }

        [TestMethod, Description("Unknown execution mode fails")]
        public async Task TestUnknownExecutionModeFails()
        {
            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                ScriptFile = GetScriptPath(),
                ExecutionMode = (PowerShellExecutionMode)999
            });

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.Message, "Unknown PowerShell execution mode");
        }

        [TestMethod, Description("Unknown execution mode names fall back to the default mode")]
        public void TestUnknownExecutionModeNamesFallbackToDefaultMode()
        {
            var parsed = PowerShellManager.TryParseExecutionMode("CompatibilityMode", out var executionMode);

            Assert.IsFalse(parsed);
            Assert.AreEqual(PowerShellManager.GetDefaultExecutionMode(), executionMode);
        }

        [TestMethod, Description("System process mode runs script content")]
        public async Task TestSystemProcessModeRunsScriptContent()
        {
            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                ScriptContent = "Write-Output 'process-script-content-output'",
                ExecutionMode = PowerShellExecutionMode.SystemProcess
            });

            Assert.IsTrue(result.IsSuccess, result.Message);
            StringAssert.Contains(result.Message, "process-script-content-output");
        }

        [TestMethod, Description("Automatic mode runs script content")]
        public async Task TestAutomaticModeRunsScriptContent()
        {
            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                ScriptContent = "Write-Output 'compatibility-script-content-output'",
                ExecutionMode = PowerShellExecutionMode.Automatic
            });

            Assert.IsTrue(result.IsSuccess, result.Message);
            StringAssert.Contains(result.Message, "compatibility-script-content-output");
        }

        [TestMethod, Description("Automatic mode uses in-process PowerShell when Windows credentials are supplied")]
        [TestCategory("RequiresLocalUser")]
        public async Task TestAutomaticModeUsesInProcessPowerShellWhenWindowsCredentialsAreSupplied()
        {
            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                ScriptContent = "$identity = if ($IsWindows) { [System.Security.Principal.WindowsIdentity]::GetCurrent().Name } else { whoami }; Write-Output $identity",
                ExecutionMode = PowerShellExecutionMode.Automatic,
                Credentials = new Dictionary<string, string>
                {
                    ["username"] = "testuser",
                    ["password"] = "testing123"
                }
            });

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !result.IsSuccess && result.Message.Contains("only supported on Windows", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Inconclusive(result.Message);
            }

            Assert.IsTrue(result.IsSuccess, result.Message);
            StringAssert.Contains(result.Message, "PowerShell Execution Mode: InProcess");
        }

        [TestMethod, Description("Automatic mode keeps the Windows credential compatibility path when parameters are supplied")]
        [TestCategory("RequiresLocalUser")]
        public async Task TestAutomaticModeKeepsWindowsCredentialCompatibilityPathWithParameters()
        {
            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                ScriptContent = "param($message, [bool]$flag); $identity = if ($IsWindows) { [System.Security.Principal.WindowsIdentity]::GetCurrent().Name } else { whoami }; Write-Output $identity; Write-Output \"Message: $message\"; Write-Output \"Flag: $flag\"",
                ExecutionMode = PowerShellExecutionMode.Automatic,
                Parameters = new Dictionary<string, object>
                {
                    ["message"] = "impersonated-payload-message",
                    ["flag"] = "true"
                },
                Credentials = new Dictionary<string, string>
                {
                    ["username"] = "testuser",
                    ["password"] = "testing123"
                }
            });

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !result.IsSuccess && result.Message.Contains("only supported on Windows", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Inconclusive(result.Message);
            }

            Assert.IsTrue(result.IsSuccess, result.Message);
            StringAssert.Contains(result.Message, "PowerShell Execution Mode: InProcess");
            StringAssert.Contains(result.Message, "Message: impersonated-payload-message");
            StringAssert.Contains(result.Message, "Flag: True");
        }

        [TestMethod, Description("Full impersonation with profile process mode runs script content with parameters as a different local user")]
        [TestCategory("RequiresLocalUser")]
        public async Task TestFullImpersonationWithProfileProcessModeRunsScriptContentWithParametersAsDifferentLocalUser()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Inconclusive("PowerShell Full Impersonation is only supported on Windows.");
            }

            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                ScriptContent = "param($message, [bool]$flag); $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name; Write-Output $identity; Write-Output \"Message: $message\"; Write-Output \"Flag: $flag\"",
                ExecutionMode = PowerShellExecutionMode.SystemProcess,
                ImpersonationMode = PowerShellImpersonationMode.FullWithProfile,
                Parameters = new Dictionary<string, object>
                {
                    ["message"] = "full-impersonation-payload-message",
                    ["flag"] = "true"
                },
                Credentials = new Dictionary<string, string>
                {
                    ["username"] = "testuser",
                    ["password"] = "testing123"
                }
            });

            Assert.IsTrue(result.IsSuccess, result.Message);
            StringAssert.Contains(result.Message.ToLowerInvariant(), "testuser");
            StringAssert.Contains(result.Message, "Message: full-impersonation-payload-message");
            StringAssert.Contains(result.Message, "Flag: True");
            StringAssert.Contains(result.Message, "PowerShell Full Impersonation: user logon token acquired.");
        }

        [TestMethod, Description("In-process mode runs script content")]
        public async Task TestInProcessModeRunsScriptContent()
        {
            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                ScriptContent = "Write-Output 'script-content-output'",
                ExecutionMode = PowerShellExecutionMode.InProcess
            });

            Assert.IsTrue(result.IsSuccess, result.Message);
            StringAssert.Contains(result.Message, "script-content-output");
        }

        [TestMethod, Description("In-process mode suppresses verbose and debug streams unless verbose stream logging is enabled")]
        public async Task TestInProcessModeSuppressesVerboseAndDebugStreamsByDefault()
        {
            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                ScriptContent = "$VerbosePreference = 'Continue'; $DebugPreference = 'Continue'; Write-Verbose 'verbose-stream-marker'; Write-Debug 'debug-stream-marker'; Write-Output 'main-output-marker'",
                ExecutionMode = PowerShellExecutionMode.InProcess
            });

            Assert.IsTrue(result.IsSuccess, result.Message);
            StringAssert.Contains(result.Message, "main-output-marker");
            Assert.IsFalse(result.Message.Contains("verbose-stream-marker"), "Verbose stream output should be suppressed when verbose stream logging is disabled.");
            Assert.IsFalse(result.Message.Contains("debug-stream-marker"), "Debug stream output should be suppressed when verbose stream logging is disabled.");
        }

        [TestMethod, Description("In-process mode includes verbose and debug streams when verbose stream logging is enabled")]
        public async Task TestInProcessModeIncludesVerboseAndDebugStreamsWhenEnabled()
        {
            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                ScriptContent = "$VerbosePreference = 'Continue'; $DebugPreference = 'Continue'; Write-Verbose 'verbose-stream-marker'; Write-Debug 'debug-stream-marker'; Write-Output 'main-output-marker'",
                ExecutionMode = PowerShellExecutionMode.InProcess,
                VerboseStreamLogging = true
            });

            Assert.IsTrue(result.IsSuccess, result.Message);
            StringAssert.Contains(result.Message, "main-output-marker");
            StringAssert.Contains(result.Message, "verbose-stream-marker");
            StringAssert.Contains(result.Message, "debug-stream-marker");
        }

        [TestMethod, Description("In-process mode reports parse errors")]
        public async Task TestInProcessModeReportsParseErrors()
        {
            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                ScriptContent = "if (",
                ExecutionMode = PowerShellExecutionMode.InProcess
            });

            Assert.IsFalse(result.IsSuccess);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Message));
        }

        [TestMethod, Description("In-process mode can ignore selected command exceptions")]
        public async Task TestInProcessModeIgnoresSelectedCommandExceptions()
        {
            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                ScriptContent = "Get-Item 'Certify-Definitely-Missing-File'; Write-Output 'after-error'",
                ExecutionMode = PowerShellExecutionMode.InProcess,
                IgnoredCommandExceptions = new[] { "Get-Item" }
            });

            Assert.IsTrue(result.IsSuccess, result.Message);
            StringAssert.Contains(result.Message, "after-error");
        }

        [TestMethod, Description("In-process mode returns failure for non ignored command exceptions")]
        public async Task TestInProcessModeFailsForNonIgnoredCommandExceptions()
        {
            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                ScriptContent = "Get-Item 'Certify-Definitely-Missing-File'",
                ExecutionMode = PowerShellExecutionMode.InProcess
            });

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.Message, "Error:");
        }

        [TestMethod, Description("In-process mode timeout returns failure")]
        public async Task TestInProcessModeTimeoutReturnsFailure()
        {
            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                ScriptContent = "Start-Sleep -Seconds 7",
                ExecutionMode = PowerShellExecutionMode.InProcess,
                TimeoutMinutes = 0
            });

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.Message, "Script did not complete in the required time");
        }

        [TestMethod, Description("Process mode timeout returns failure")]
        public async Task TestProcessModeTimeoutReturnsFailure()
        {
            var scriptPath = CreateTempScript("ps1", "Start-Sleep -Seconds 2");

            try
            {
                var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
                {
                    PowerShellExecutionPolicy = "Unrestricted",
                    ScriptFile = scriptPath,
                    ExecutionMode = PowerShellExecutionMode.SystemProcess,
                    TimeoutMinutes = 0
                });

                Assert.IsFalse(result.IsSuccess);
                StringAssert.Contains(result.Message, "took too long to exit");
            }
            finally
            {
                DeleteTempScript(scriptPath);
            }
        }

        [TestMethod, Description("Windows credential username formats include optional local domain")]
        public void TestGetWindowsCredentialsUsernameFormatsLocalDomain()
        {
            var credentials = new Dictionary<string, string>
            {
                ["username"] = "certify-test-user",
                ["password"] = "test-password"
            };

            Assert.AreEqual("certify-test-user", PowerShellManager.GetWindowsCredentialsUsername(credentials));
            Assert.AreEqual($"{Environment.MachineName}\\certify-test-user", PowerShellManager.GetWindowsCredentialsUsername(credentials, includeAutoLocalDomain: true));
        }

        [TestMethod, Description("Windows credential username formats explicit domain")]
        public void TestGetWindowsCredentialsUsernameFormatsExplicitDomain()
        {
            var credentials = new Dictionary<string, string>
            {
                ["domain"] = "CERTIFY",
                ["username"] = "certify-test-user",
                ["password"] = "test-password"
            };

            Assert.AreEqual("CERTIFY\\certify-test-user", PowerShellManager.GetWindowsCredentialsUsername(credentials));
        }

        private static async Task<Certify.Models.Config.ActionResult> RunSimpleScript(PowerShellExecutionMode executionMode, Dictionary<string, object> parameters = null)
        {
            CleanupOutput();

            var result = await PowerShellManager.RunScript(new PowerShellScriptSettings
            {
                PowerShellExecutionPolicy = "Unrestricted",
                Result = new CertificateRequestResult(new ManagedCertificate()),
                ScriptFile = GetScriptPath(),
                Parameters = parameters,
                ExecutionMode = executionMode
            });

            Assert.IsTrue(File.Exists(TestOutputPath), "Powershell output file should exist");

            return result;
        }

        private static async Task<string> RunSimpleScriptAndGetPowerShellVersion(PowerShellExecutionMode executionMode)
        {
            var result = await RunSimpleScript(executionMode);

            Assert.IsTrue(result.IsSuccess, result.Message);

            var versionLine = File.ReadLines(TestOutputPath).FirstOrDefault(l => l.StartsWith(PowerShellVersionPrefix, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(string.IsNullOrWhiteSpace(versionLine), $"PowerShell version should be logged for {executionMode}.");

            CleanupOutput();

            return versionLine.Substring(PowerShellVersionPrefix.Length).Trim();
        }

        private static string GetScriptPath()
        {
            return Path.Combine(AppContext.BaseDirectory, ScriptRelativePath);
        }

        private static void AssertOutputContains(string expected)
        {
            var output = File.ReadAllText(TestOutputPath);
            StringAssert.Contains(output, expected);
        }

        private static void CleanupOutput()
        {
            try
            {
                File.Delete(TestOutputPath);
            }
            catch { }
        }

        private static string CreateTempScript(string extension, string content)
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), $"certify-test-powershell-{Guid.NewGuid():N}.{extension}");
            File.WriteAllText(scriptPath, content);
            return scriptPath;
        }

        private static void DeleteTempScript(string scriptPath)
        {
            try
            {
                File.Delete(scriptPath);
            }
            catch { }
        }
    }
}
