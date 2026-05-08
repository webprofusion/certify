using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using Certify.Models.Config;
using Microsoft.Win32.SafeHandles;

namespace Certify.Management
{
    public partial class PowerShellManager
    {
        private const int StartfUseStdHandles = 0x00000100;
        private const int CreateNoWindow = 0x08000000;
        private const int CreateUnicodeEnvironment = 0x00000400;
        private const int LogonWithProfile = 0x00000001;

        private static ActionResult RunProcessWithFullImpersonation(ProcessStartInfo startInfo, Dictionary<string, string> credentials, string logonType, bool loadUserProfile, int timeoutMinutes, StringBuilder log, Action cleanup)
        {
            try
            {
                var targetUsername = GetWindowsCredentialsUsername(credentials, includeAutoLocalDomain: true);
                log.AppendLine($"PowerShell Full Impersonation: service identity is {WindowsIdentity.GetCurrent().Name}.");
                log.AppendLine($"PowerShell Full Impersonation: target identity is {targetUsername}; logon type is {logonType.WithDefault("interactive")}.");

                using var logonToken = LogonUserForFullImpersonation(credentials, logonType);

                if (logonToken.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "LogonUser returned an invalid token.");
                }

                log.AppendLine("PowerShell Full Impersonation: user logon token acquired.");
                return StartProcessWithToken(startInfo, logonToken, credentials, loadUserProfile, timeoutMinutes, log);
            }
            catch (Exception exp)
            {
                log.AppendLine("Error Running Script with Full Impersonation: " + exp);
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = log.ToString()
                };
            }
            finally
            {
                cleanup?.Invoke();
            }
        }

        private static SafeTokenHandle LogonUserForFullImpersonation(Dictionary<string, string> credentials, string logonType)
        {
            var (username, domain, password) = GetWindowsCredentialParts(credentials);
            var logonTypeValue = GetWindowsLogonTypeValue(logonType);

            if (!LogonUser(username, domain, password, logonTypeValue, LogonProvider.Default, out var token))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "LogonUser failed.");
            }

            return token;
        }

        private static (string Username, string Domain, string Password) GetWindowsCredentialParts(Dictionary<string, string> credentials)
        {
            var username = credentials["username"];
            var password = credentials["password"];
            credentials.TryGetValue("domain", out var domain);

            if (domain == null && !username.Contains(".\\") && !username.Contains("@"))
            {
                domain = ".";
            }

            if (username.StartsWith(@".\", StringComparison.Ordinal))
            {
                domain = ".";
                username = username.Substring(2);
            }

            if (username.Contains("@", StringComparison.Ordinal))
            {
                domain = null;
            }

            return (username, domain, password);
        }

        private static int GetWindowsLogonTypeValue(string logonType)
        {
            return logonType?.ToLower() switch
            {
                "network" => WindowsLogonType.Network,
                "batch" => WindowsLogonType.Batch,
                "service" => WindowsLogonType.Service,
                "interactive" => WindowsLogonType.Interactive,
                "newcredentials" => WindowsLogonType.NewCredentials,
                _ => WindowsLogonType.Interactive,
            };
        }

        private static ActionResult StartProcessWithToken(ProcessStartInfo startInfo, SafeTokenHandle token, Dictionary<string, string> credentials, bool loadUserProfile, int timeoutMinutes, StringBuilder log)
        {
            IntPtr environment = IntPtr.Zero;
            var profileInfo = new ProfileInfo();
            var profileLoaded = false;

            try
            {
                if (loadUserProfile)
                {
                    using var identity = new WindowsIdentity(token.DangerousGetHandle());
                    profileInfo.dwSize = Marshal.SizeOf<ProfileInfo>();
                    profileInfo.lpUserName = identity.Name;

                    profileLoaded = LoadUserProfile(token, ref profileInfo);
                    if (!profileLoaded)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadUserProfile failed.");
                    }

                    log.AppendLine($"PowerShell Full Impersonation: loaded user profile for {identity.Name}.");
                }

                environment = CreateFullImpersonationEnvironmentBlock(token, startInfo.Environment, log);

                if (environment == IntPtr.Zero)
                {
                    log.AppendLine("Warning: Could not create user environment block. Using default CreateProcessWithLogonW environment.");
                }

                var commandLine = $"\"{startInfo.FileName}\" {startInfo.Arguments}";
                var outputDirectory = startInfo.Environment.TryGetValue("TEMP", out var tempPath) && !string.IsNullOrWhiteSpace(tempPath) ? tempPath : Path.GetTempPath();
                var stdoutPath = Path.Combine(outputDirectory, $"certify-powershell-stdout-{Guid.NewGuid():N}.log");
                var stderrPath = Path.Combine(outputDirectory, $"certify-powershell-stderr-{Guid.NewGuid():N}.log");

                using var stdinHandle = CreateInheritedInputFile("NUL");
                using var stdoutHandle = CreateInheritedOutputFile(stdoutPath);
                using var stderrHandle = CreateInheritedOutputFile(stderrPath);

                var startupInfo = new StartupInfo
                {
                    cb = Marshal.SizeOf<StartupInfo>(),
                    dwFlags = StartfUseStdHandles,
                    hStdInput = stdinHandle.DangerousGetHandle(),
                    hStdOutput = stdoutHandle.DangerousGetHandle(),
                    hStdError = stderrHandle.DangerousGetHandle()
                };

                var creationFlags = CreateNoWindow;
                if (environment != IntPtr.Zero)
                {
                    creationFlags |= CreateUnicodeEnvironment;
                }

                var (username, domain, password) = GetWindowsCredentialParts(credentials);
                if (!CreateProcessWithLogonW(username, domain, password, loadUserProfile ? LogonWithProfile : 0, startInfo.FileName, commandLine, creationFlags, environment, startInfo.WorkingDirectory, ref startupInfo, out var processInfo))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessWithLogonW failed.");
                }

                log.AppendLine($"PowerShell Full Impersonation: started process id {processInfo.dwProcessId}.");

                using var processHandle = new SafeWaitHandle(processInfo.hProcess, ownsHandle: true);
                using var threadHandle = new SafeWaitHandle(processInfo.hThread, ownsHandle: true);
                stdinHandle.Dispose();
                var waitResult = WaitForSingleObject(processInfo.hProcess, (uint)Math.Max(0, timeoutMinutes * 60 * 1000));

                if (waitResult == WaitResult.Timeout)
                {
                    TerminateProcess(processInfo.hProcess, 1);
                    log.AppendLine("Warning: Script ran but took too long to exit and was terminated.");
                    AppendProcessOutput(log, stdoutPath, stderrPath);
                    return new ActionResult { IsSuccess = false, Message = log.ToString() };
                }

                if (waitResult == WaitResult.Failed)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "WaitForSingleObject failed.");
                }

                stdoutHandle.Dispose();
                stderrHandle.Dispose();
                AppendProcessOutput(log, stdoutPath, stderrPath);

                if (!GetExitCodeProcess(processInfo.hProcess, out var exitCode))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeProcess failed.");
                }

                if (exitCode != 0)
                {
                    log.AppendLine("Warning: Script exited with the following ExitCode: " + exitCode);
                    return new ActionResult { IsSuccess = false, Message = log.ToString() };
                }

                return new ActionResult { IsSuccess = true, Message = log.ToString() };
            }
            finally
            {
                if (environment != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(environment);
                }

                if (profileLoaded)
                {
                    UnloadUserProfile(token, profileInfo.hProfile);
                }
            }
        }

        private static SafeFileHandle CreateInheritedOutputFile(string filePath)
        {
            var securityAttributes = new SecurityAttributes
            {
                nLength = Marshal.SizeOf<SecurityAttributes>(),
                bInheritHandle = true
            };

            var handle = CreateFile(filePath, FileAccessMask.GenericWrite, FileShare.Read, ref securityAttributes, FileCreationDisposition.CreateAlways, FileAttributes.Normal, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateFile failed for '{filePath}'.");
            }

            return handle;
        }

        private static IntPtr CreateFullImpersonationEnvironmentBlock(SafeTokenHandle token, IDictionary<string, string> processEnvironment, StringBuilder log)
        {
            if (!CreateEnvironmentBlock(out var userEnvironment, token, inherit: false))
            {
                log.AppendLine($"Warning: Could not create user environment block. {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                return IntPtr.Zero;
            }

            try
            {
                var environment = ReadEnvironmentBlock(userEnvironment);

                foreach (var item in processEnvironment)
                {
                    environment[item.Key] = item.Value;
                }

                var environmentBlock = BuildEnvironmentBlock(environment);
                log.AppendLine("PowerShell Full Impersonation: created user environment block.");

                return environmentBlock;
            }
            finally
            {
                DestroyEnvironmentBlock(userEnvironment);
            }
        }

        private static Dictionary<string, string> ReadEnvironmentBlock(IntPtr environmentBlock)
        {
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var offset = 0;

            while (true)
            {
                var entry = Marshal.PtrToStringUni(IntPtr.Add(environmentBlock, offset));
                if (string.IsNullOrEmpty(entry))
                {
                    break;
                }

                var separatorIndex = entry.IndexOf('=');
                if (separatorIndex > 0)
                {
                    environment[entry.Substring(0, separatorIndex)] = entry.Substring(separatorIndex + 1);
                }

                offset += (entry.Length + 1) * sizeof(char);
            }

            return environment;
        }

        private static IntPtr BuildEnvironmentBlock(Dictionary<string, string> environment)
        {
            var entries = new List<string>();
            foreach (var item in environment)
            {
                entries.Add($"{item.Key}={item.Value}");
            }

            entries.Sort(StringComparer.OrdinalIgnoreCase);

            var environmentText = string.Join("\0", entries) + "\0\0";
            var environmentBytes = Encoding.Unicode.GetBytes(environmentText);
            var environmentBlock = Marshal.AllocHGlobal(environmentBytes.Length);
            Marshal.Copy(environmentBytes, 0, environmentBlock, environmentBytes.Length);

            return environmentBlock;
        }

        private static SafeFileHandle CreateInheritedInputFile(string filePath)
        {
            var securityAttributes = new SecurityAttributes
            {
                nLength = Marshal.SizeOf<SecurityAttributes>(),
                bInheritHandle = true
            };

            var handle = CreateFile(filePath, FileAccessMask.GenericRead, FileShare.ReadWrite, ref securityAttributes, FileCreationDisposition.OpenExisting, FileAttributes.Normal, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateFile failed for '{filePath}'.");
            }

            return handle;
        }

        private static void AppendProcessOutput(StringBuilder log, string stdoutPath, string stderrPath)
        {
            if (TryReadAndDeleteProcessOutput(stdoutPath, out var output) && !string.IsNullOrEmpty(output))
            {
                log.Append(output);
            }

            if (TryReadAndDeleteProcessOutput(stderrPath, out var error) && !string.IsNullOrEmpty(error))
            {
                foreach (var line in error.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        log.AppendLine($"Error: {line}");
                    }
                }
            }
        }

        private static bool TryReadAndDeleteProcessOutput(string filePath, out string content)
        {
            content = null;

            try
            {
                if (File.Exists(filePath))
                {
                    content = File.ReadAllText(filePath);
                    File.Delete(filePath);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static class WindowsLogonType
        {
            public const int Interactive = 2;
            public const int Network = 3;
            public const int Batch = 4;
            public const int Service = 5;
            public const int NewCredentials = 9;
        }

        private static class LogonProvider
        {
            public const int Default = 0;
        }

        private static class WaitResult
        {
            public const uint Timeout = 0x00000102;
            public const uint Failed = 0xFFFFFFFF;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProfileInfo
        {
            public int dwSize;
            public int dwFlags;
            public string lpUserName;
            public string lpProfilePath;
            public string lpDefaultPath;
            public string lpServerName;
            public string lpPolicyPath;
            public IntPtr hProfile;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bInheritHandle;
        }

        private sealed class SafeTokenHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private SafeTokenHandle() : base(true)
            {
            }

            protected override bool ReleaseHandle()
            {
                return CloseHandle(handle);
            }
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LogonUser(string lpszUsername, string lpszDomain, string lpszPassword, int dwLogonType, int dwLogonProvider, out SafeTokenHandle phToken);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessWithLogonW(string lpUsername, string lpDomain, string lpPassword, int dwLogonFlags, string lpApplicationName, string lpCommandLine, int dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory, ref StartupInfo lpStartupInfo, out ProcessInformation lpProcessInformation);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, SafeTokenHandle hToken, bool inherit);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

        [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LoadUserProfile(SafeTokenHandle hToken, ref ProfileInfo lpProfileInfo);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool UnloadUserProfile(SafeTokenHandle hToken, IntPtr hProfile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr hProcess, out int lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr hProcess, int uExitCode);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string lpFileName, int dwDesiredAccess, FileShare dwShareMode, ref SecurityAttributes lpSecurityAttributes, FileCreationDisposition dwCreationDisposition, FileAttributes dwFlagsAndAttributes, IntPtr hTemplateFile);

        private static class FileAccessMask
        {
            public const int GenericRead = unchecked((int)0x80000000);
            public const int GenericWrite = unchecked((int)0x40000000);
        }

        private enum FileCreationDisposition
        {
            CreateAlways = 2,
            OpenExisting = 3
        }
    }
}
