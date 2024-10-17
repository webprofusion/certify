﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Certify.Models
{
    public class EnvironmentUtil
    {

        private static void ApplyRestrictedACL(DirectoryInfo dir)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {

                // disable default inheritance for the existing path ACL
                var acl = dir.GetAccessControl();
                acl.SetAccessRuleProtection(true, true);
                dir.SetAccessControl(acl);

                // removing any existing ACL entries for users group
                acl = dir.GetAccessControl();
                var currentRules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier));

                var allUsersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

                var processUserSid = WindowsIdentity.GetCurrent().User;

                if (processUserSid != null)
                {
                    foreach (FileSystemAccessRule rule in currentRules)
                    {
                        if (rule.IdentityReference == allUsersSid || rule.IdentityReference == processUserSid)
                        {
                            acl.RemoveAccessRuleAll(rule);
                        }
                    }

                    // add full control for the current (process) user 
                    var currentUserRights = new FileSystemAccessRule(processUserSid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);
                    acl.AddAccessRule(currentUserRights);
                }

                // add full control for administrators
                var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var adminRights = new FileSystemAccessRule(adminSid, FileSystemRights.FullControl, AccessControlType.Allow);
                acl.AddAccessRule(adminRights);

                // add full control for system
                var systemUserSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                var systemUserRights = new FileSystemAccessRule(systemUserSid, FileSystemRights.FullControl, AccessControlType.Allow);
                acl.AddAccessRule(systemUserRights);

                dir.SetAccessControl(acl);

            }
        }

        /// <summary>
        /// if path already exists no changes are made, otherwise directory is created and ACL applied
        /// </summary>
        /// <param name="path"></param>
        private static void CreateAndApplyRestrictedACL(string path)
        {
            if (!Directory.Exists(path))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // create folder with permissions limited to Administrators and the current process user

                    var dir = Directory.CreateDirectory(path);
                    ApplyRestrictedACL(dir);
                }
                else
                {
                    Directory.CreateDirectory(path);
                }
            }
        }

        /// <summary>
        /// Ensure the app data path exists and has the required permissions
        /// </summary>
        /// <param name="subDirectory">optional subfolder to include</param>
        /// <returns>full app data with with optional subdirectory</returns>
        public static string CreateAppDataPath(string? subDirectory = null)
        {
            var parts = new List<string>()
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Models.SharedConstants.APPDATASUBFOLDER
            };

            if (Environment.GetEnvironmentVariable("CERTIFY_ENABLE_PER_INSTANCE_SETTINGS")?.ToLowerInvariant() == "true")
            {
                parts.Add(Environment.MachineName.ToLowerInvariant().Trim());
            }

            var path = Path.Combine(parts.ToArray());
            CreateAndApplyRestrictedACL(path);

            if (subDirectory != null)
            {
                parts.Add(subDirectory);
                path = Path.Combine(parts.ToArray());

                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }

            return path;
        }

        public static string GetFriendlyOSName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return $"{RuntimeInformation.OSDescription}";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var osName = string.Empty;

                string filePath = "/etc/os-release";

                try
                {
                    using (FileStream fileStream = new FileStream(filePath, FileMode.Open))
                    {
                        using (StreamReader reader = new StreamReader(fileStream))
                        {
                            string line;

                            while ((line = reader.ReadLine()) != null)
                            {

                                if (line.StartsWith("NAME"))
                                {
                                    osName = line.Split('\"')[1]; //split the line string by " and get the second slice
                                    return osName;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    return $"Linux - {RuntimeInformation.OSDescription}";
                }
            }

            return $"{RuntimeInformation.OSDescription}";
        }
    }
}
