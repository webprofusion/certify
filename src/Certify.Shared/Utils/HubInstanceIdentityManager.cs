using System;
using System.IO;
using Newtonsoft.Json;

namespace Certify.SharedUtils
{
    public class HubInstanceIdentity
    {
        public string HubAssignedInstanceId { get; set; } = string.Empty;
        public DateTimeOffset DateCreated { get; set; } = DateTimeOffset.UtcNow;
    }

    public static class HubInstanceIdentityManager
    {
        private static string GetIdentityFilePath()
        {
            string appDataPath;

            try
            {
                // use the same app data path resolution as the rest of the app, which honours CERTIFY_APPDATA_PATH
                appDataPath = Certify.Models.EnvironmentUtil.EnsuredAppDataPath();
            }
            catch (Exception)
            {
                appDataPath = Path.Combine(AppContext.BaseDirectory, Certify.Models.SharedConstants.APPDATASUBFOLDER);
                Directory.CreateDirectory(appDataPath);
            }

            var file = Path.Combine(appDataPath, "hubinstance.identity.json");
#if DEBUG
            file = Path.Combine(appDataPath, "hubinstance.identity.debug.json");
#endif
            return file;
        }

        public static string GetHubAssignedInstanceId(string? fallbackHubAssignedInstanceId = null)
        {
            var identity = LoadIdentity();
            if (!string.IsNullOrWhiteSpace(identity?.HubAssignedInstanceId))
            {
                return identity.HubAssignedInstanceId;
            }

            if (!string.IsNullOrWhiteSpace(fallbackHubAssignedInstanceId))
            {
                TrySetHubAssignedInstanceId(fallbackHubAssignedInstanceId, overwriteExisting: false);
                return fallbackHubAssignedInstanceId;
            }

            return string.Empty;
        }

        public static bool TrySetHubAssignedInstanceId(string hubAssignedInstanceId, bool overwriteExisting = false)
        {
            if (string.IsNullOrWhiteSpace(hubAssignedInstanceId))
            {
                return false;
            }

            var identity = LoadIdentity();

            if (!string.IsNullOrWhiteSpace(identity?.HubAssignedInstanceId)
                && !string.Equals(identity.HubAssignedInstanceId, hubAssignedInstanceId, StringComparison.OrdinalIgnoreCase)
                && !overwriteExisting)
            {
                return false;
            }

            identity ??= new HubInstanceIdentity();
            identity.HubAssignedInstanceId = hubAssignedInstanceId;
            if (identity.DateCreated == default)
            {
                identity.DateCreated = DateTimeOffset.UtcNow;
            }

            var path = GetIdentityFilePath();
            File.WriteAllText(path, JsonConvert.SerializeObject(identity, Formatting.Indented));
            return true;
        }

        private static HubInstanceIdentity? LoadIdentity()
        {
            var path = GetIdentityFilePath();
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<HubInstanceIdentity>(json);
        }
    }
}
