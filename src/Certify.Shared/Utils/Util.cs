using Certify.Locales;
using Certify.Models;
using Microsoft.ApplicationInsights;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Certify.Management
{
    public class Util
    {
        public const string APPDATASUBFOLDER = "Certify";

        public static void SetSupportedTLSVersions()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
        }

        public static string GetAppDataFolder(string subFolder = null)
        {
            var parts = new List<string>()
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                APPDATASUBFOLDER
            };

            if (subFolder != null) parts.Add(subFolder);

            var path = Path.Combine(parts.ToArray());

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return path;
        }

        public TelemetryClient InitTelemetry()
        {
            var tc = new TelemetryClient();

            tc.Context.InstrumentationKey = ConfigResources.AIInstrumentationKey;
            tc.InstrumentationKey = ConfigResources.AIInstrumentationKey;

            // Set session data:

            tc.Context.Session.Id = Guid.NewGuid().ToString();
            tc.Context.Component.Version = GetAppVersion().ToString();
            tc.Context.Device.OperatingSystem = Environment.OSVersion.ToString();

            return tc;
        }

        public Version GetAppVersion()
        {
            // returns the version of Certify.Shared
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();

            var v = assembly.GetName().Version;
            return v;
        }

        public async Task<UpdateCheck> CheckForUpdates()
        {
            var v = GetAppVersion();
            return await this.CheckForUpdates(v);
        }

        public async Task<UpdateCheck> CheckForUpdates(Version appVersion)
        {
            return await this.CheckForUpdates(appVersion.ToString());
        }

        public async Task<UpdateCheck> CheckForUpdates(string appVersion)
        {
            //get app version
            try
            {
                HttpClient client = new HttpClient();
                var response = await client.GetAsync(ConfigResources.AppUpdateCheckURI + "?v=" + appVersion);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    /*json = @"{
                         'version': {
                             'major': 2,
                             'minor': 0,
                             'patch': 3
                                                 },
                           'message': {
                                                     'body': 'There is an awesome update available.',
                             'downloadPageURL': 'https://certify.webprofusion.com',
                             'releaseNotesURL': 'https://certify.webprofusion.com/home/changelog',
                             'isMandatory': true
                           }
                     }";*/

                    UpdateCheck checkResult = Newtonsoft.Json.JsonConvert.DeserializeObject<UpdateCheck>(json);
                    return CompareVersions(appVersion, checkResult);
                }

                return new UpdateCheck { IsNewerVersion = false };
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static UpdateCheck CompareVersions(string appVersion, UpdateCheck checkResult)
        {
            checkResult.IsNewerVersion = AppVersion.IsOtherVersionNewer(AppVersion.FromString(appVersion), checkResult.Version);

            // check for mandatory updates
            if (checkResult.Message != null && checkResult.Message.MandatoryBelowVersion != null)
            {
                checkResult.MustUpdate = AppVersion.IsOtherVersionNewer(AppVersion.FromString(appVersion), checkResult.Message.MandatoryBelowVersion);
            }

            return checkResult;
        }

        /// <summary>
        /// From https://docs.microsoft.com/en-us/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed#net_d 
        /// </summary>
        /// <returns></returns>
        public static string GetDotNetVersion()
        {
            const string subkey = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\";

            using (RegistryKey ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(subkey))
            {
                if (ndpKey != null && ndpKey.GetValue("Release") != null)
                {
                    return GetDotNetVersion((int)ndpKey.GetValue("Release"));
                }
                else
                {
                    return ".NET Version not detected.";
                }
            }
        }

        private static string GetDotNetVersion(int releaseKey)
        {
            if (releaseKey >= 460798) return "4.7 or later";
            if (releaseKey >= 394802) return "4.6.2";
            if (releaseKey >= 394254) return "4.6.1";
            if (releaseKey >= 393295) return "4.6";
            if (releaseKey >= 379893) return "4.5.2";
            if (releaseKey >= 378675) return "4.5.1";
            if (releaseKey >= 378389) return "4.5";

            // This code should never execute. A non-null release key should mean that 4.5 or later
            // is installed.
            return "No 4.5 or later version detected";
        }
    }
}