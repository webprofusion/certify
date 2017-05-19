using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Certify.Models;
using Microsoft.ApplicationInsights;
using System.Net;

namespace Certify.Management
{
    public class Util
    {
        public const string APPDATASUBFOLDER = "Certify";

        public static void SetSupportedTLSVersions()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
        }

        public static string GetAppDataFolder()
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\" + APPDATASUBFOLDER;
            if (!System.IO.Directory.Exists(path))
            {
                System.IO.Directory.CreateDirectory(path);
            }
            return path;
        }

        public TelemetryClient InitTelemetry()
        {
            var tc = new TelemetryClient();
            tc.Context.InstrumentationKey = Certify.Properties.Resources.AIInstrumentationKey;
            tc.InstrumentationKey = Certify.Properties.Resources.AIInstrumentationKey;

            // Set session data:

            tc.Context.Session.Id = Guid.NewGuid().ToString();
            tc.Context.Component.Version = GetAppVersion().ToString();
            tc.Context.Device.OperatingSystem = Environment.OSVersion.ToString();
            return tc;
        }

        public Version GetAppVersion()
        {
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
                var response = await client.GetAsync(Properties.Resources.AppUpdateCheckURI + "?v=" + appVersion);
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
                    checkResult.IsNewerVersion = AppVersion.IsOtherVersionNewer(AppVersion.FromString(appVersion), checkResult.Version);

                    return checkResult;
                }

                return new UpdateCheck { IsNewerVersion = false };
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}