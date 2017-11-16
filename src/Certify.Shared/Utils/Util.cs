using Certify.Locales;
using Certify.Models;
using Microsoft.ApplicationInsights;
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
            /* AppVersion v1 = new AppVersion { Major = 1, Minor = 0, Patch = 1 };
             AppVersion v2 = new AppVersion { Major = 1, Minor = 0, Patch = 2 };
             bool isNewer = AppVersion.IsOtherVersionNewer(v1, v2);

             v2.Patch = 1;
             isNewer = AppVersion.IsOtherVersionNewer(v1, v2);
             v2.Major = 2;
             isNewer = AppVersion.IsOtherVersionNewer(v1, v2);
             v2.Major = 1;
             v2.Minor = 1;
             isNewer = AppVersion.IsOtherVersionNewer(v1, v2);*/

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
    }
}