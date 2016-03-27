using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Management
{
    public class AppVersion
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Patch { get; set; }

        public static AppVersion FromString(string version)
        {
            string[] versionComponents = version.Split('.');

            AppVersion current = new AppVersion
            {
                Major = int.Parse(versionComponents[0]),
                Minor = int.Parse(versionComponents[1]),
                Patch = int.Parse(versionComponents[2])
            };
            return current;
        }

        public static bool IsOtherVersionNewer(AppVersion currentVersion, AppVersion otherVersion)
        {
            if (currentVersion.Major >= otherVersion.Major)
            {
                if (currentVersion.Major > otherVersion.Major)
                {
                    return false;
                }

                //current major version is same, check minor
                if (currentVersion.Minor >= otherVersion.Minor)
                {
                    if (currentVersion.Patch < otherVersion.Patch)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }

                //current minor version is less
                if (currentVersion.Minor < otherVersion.Minor)
                {
                    return true;
                }
            }
            else
            {
                //other Major version is newer
                return true;
            }

            return false; ;
        }
    }

    public class UpdateMessage
    {
        public string Body { get; set; }
        public string DownloadPageURL { get; set; }
        public string ReleaseNotesURL { get; set; }
    }

    public class UpdateCheck
    {
        public AppVersion Version { get; set; }

        public UpdateMessage Message { get; set; }

        public bool IsNewerVersion { get; set; }
    }

    public class Util
    {
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