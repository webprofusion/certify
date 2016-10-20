using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.Management
{
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