using System;

namespace Certify.Management
{
    public sealed class CoreAppSettings
    {
        private static volatile CoreAppSettings instance;
        private static object syncRoot = new Object();

        private CoreAppSettings()
        {
            // defaults
            this.SettingsSchemaVersion = 1;
            this.CheckForUpdatesAtStartup = true;
            this.EnableAppTelematics = true;
            this.IgnoreStoppedSites = true;
            this.EnableValidationProxyAPI = true;
            this.EnableAppTelematics = true;
            this.EnableEFS = false;
            this.RenewalIntervalDays = 14;
            this.MaxRenewalRequests = 0;
            this.EnableHttpChallengeServer = true;
            this.LegacySettingsUpgraded = false;
            this.VaultPath = @"C:\ProgramData\ACMESharp";
            this.InstanceId = null;
        }

        public static CoreAppSettings Current
        {
            get
            {
                if (instance != null) return instance;
                lock (syncRoot)
                {
                    if (instance == null)
                        instance = new CoreAppSettings();
                }

                return instance;
            }
            set
            {
                lock (syncRoot)
                {
                    instance = value;
                }
            }
        }

        public int SettingsSchemaVersion { get; set; }

        public bool CheckForUpdatesAtStartup { get; set; }

        public bool EnableAppTelematics { get; set; }

        public bool IgnoreStoppedSites { get; set; }

        public bool EnableValidationProxyAPI { get; set; }

        public bool EnableEFS { get; set; }

        public bool EnableDNSValidationChecks { get; set; }

        public int RenewalIntervalDays { get; set; }

        public int MaxRenewalRequests { get; set; }

        public bool EnableHttpChallengeServer { get; set; }

        public bool LegacySettingsUpgraded { get; set; }

        /// <summary>
        /// If true, this instance has been added to server dashboard 
        /// </summary>
        public bool IsInstanceRegistered { get; set; }

        public string VaultPath { get; set; }

        /// <summary>
        /// If user opts for renewal failure reporting, generated instance id is used to group results 
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// If set, specifies the UI language preference 
        /// </summary>
        public string Language { get; set; }

        /// <summary>
        /// If true the background service will periodically perform auto renewals, otherwise auto
        /// renewal requires a scheduled task
        /// </summary>
        public bool UseBackgroundServiceAutoRenewal { get; set; } = true;
    }

    public class SettingsManager
    {
        private const string COREAPPSETTINGSFILE = "appsettings.json";

        public static bool FromPreferences(Models.Preferences prefs)
        {
            CoreAppSettings.Current.EnableAppTelematics = prefs.EnableAppTelematics;
            CoreAppSettings.Current.EnableDNSValidationChecks = prefs.EnableDNSValidationChecks;
            CoreAppSettings.Current.EnableValidationProxyAPI = prefs.EnableValidationProxyAPI;
            CoreAppSettings.Current.IgnoreStoppedSites = prefs.IgnoreStoppedSites;
            CoreAppSettings.Current.MaxRenewalRequests = prefs.MaxRenewalRequests;
            CoreAppSettings.Current.RenewalIntervalDays = prefs.RenewalIntervalDays;
            CoreAppSettings.Current.EnableEFS = prefs.EnableEFS;
            CoreAppSettings.Current.IsInstanceRegistered = prefs.IsInstanceRegistered;
            CoreAppSettings.Current.Language = prefs.Language;
            CoreAppSettings.Current.UseBackgroundServiceAutoRenewal = prefs.UseBackgroundServiceAutoRenewal;
            CoreAppSettings.Current.EnableHttpChallengeServer = prefs.EnableHttpChallengeServer;
            return true;
        }

        public static Models.Preferences ToPreferences()
        {
            LoadAppSettings();
            Models.Preferences prefs = new Models.Preferences();

            prefs.EnableAppTelematics = CoreAppSettings.Current.EnableAppTelematics;
            prefs.EnableDNSValidationChecks = CoreAppSettings.Current.EnableDNSValidationChecks;
            prefs.EnableValidationProxyAPI = CoreAppSettings.Current.EnableValidationProxyAPI;
            prefs.IgnoreStoppedSites = CoreAppSettings.Current.IgnoreStoppedSites;
            prefs.MaxRenewalRequests = CoreAppSettings.Current.MaxRenewalRequests;
            prefs.RenewalIntervalDays = CoreAppSettings.Current.RenewalIntervalDays;
            prefs.EnableEFS = CoreAppSettings.Current.EnableEFS;
            prefs.InstanceId = CoreAppSettings.Current.InstanceId;
            prefs.IsInstanceRegistered = CoreAppSettings.Current.IsInstanceRegistered;
            prefs.Language = CoreAppSettings.Current.Language;
            prefs.UseBackgroundServiceAutoRenewal = CoreAppSettings.Current.UseBackgroundServiceAutoRenewal;
            prefs.EnableHttpChallengeServer = CoreAppSettings.Current.EnableHttpChallengeServer;
            return prefs;
        }

        public static void SaveAppSettings()
        {
            string appDataPath = Util.GetAppDataFolder();
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(CoreAppSettings.Current, Newtonsoft.Json.Formatting.Indented);

            lock (COREAPPSETTINGSFILE)
            {
                System.IO.File.WriteAllText(appDataPath + "\\" + COREAPPSETTINGSFILE, json);
            }
        }

        public static void LoadAppSettings()
        {
            string appDataPath = Util.GetAppDataFolder();
            var path = appDataPath + "\\" + COREAPPSETTINGSFILE;
            if (System.IO.File.Exists(path))
            {
                //ensure permissions

                //load content
                lock (COREAPPSETTINGSFILE)
                {
                    string configData = System.IO.File.ReadAllText(path);
                    CoreAppSettings.Current = Newtonsoft.Json.JsonConvert.DeserializeObject<CoreAppSettings>(configData);
                }
            }
            else
            {
                // no core app settings yet, migrate from old settings
                //Certify.Properties.Settings.Default.Reload();
                //var oldProps = Certify.Properties.Settings.Default;
                /* CoreAppSettings.Current.CheckForUpdatesAtStartup = oldProps.CheckForUpdatesAtStartup;
                 CoreAppSettings.Current.EnableAppTelematics = oldProps.EnableAppTelematics;
                 CoreAppSettings.Current.EnableDNSValidationChecks = oldProps.EnableDNSValidationChecks;
                 CoreAppSettings.Current.EnableEFS = oldProps.EnableEFS;
                 CoreAppSettings.Current.EnableValidationProxyAPI = oldProps.EnableValidationProxyAPI;
                 CoreAppSettings.Current.IgnoreStoppedSites = oldProps.ShowOnlyStartedWebsites;
                 CoreAppSettings.Current.RenewalIntervalDays = oldProps.RenewalIntervalDays;
                 CoreAppSettings.Current.MaxRenewalRequests = oldProps.MaxRenewalRequests;
                 CoreAppSettings.Current.VaultPath = oldProps.VaultPath;*/

                CoreAppSettings.Current.LegacySettingsUpgraded = true;
                CoreAppSettings.Current.IsInstanceRegistered = false;
                CoreAppSettings.Current.Language = null;
                CoreAppSettings.Current.UseBackgroundServiceAutoRenewal = true;
                CoreAppSettings.Current.EnableHttpChallengeServer = true;

                CoreAppSettings.Current.InstanceId = Guid.NewGuid().ToString();
                SaveAppSettings();
            }

            // if instance id not yet set, create it now and save
            if (String.IsNullOrEmpty(CoreAppSettings.Current.InstanceId))
            {
                CoreAppSettings.Current.InstanceId = Guid.NewGuid().ToString();
                SaveAppSettings();
            }
        }
    }
}
