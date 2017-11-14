using System;
using System.Linq;

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
            this.LegacySettingsUpgraded = false;
            this.VaultPath = @"C:\ProgramData\ACMESharp";
        }

        public static CoreAppSettings Current
        {
            get
            {
                if (instance == null)
                {
                    lock (syncRoot)
                    {
                        if (instance == null)
                            instance = new CoreAppSettings();
                    }
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

        public bool LegacySettingsUpgraded { get; set; }

        public string VaultPath { get; set; }
    }

    public class SettingsManager
    {
        private const string COREAPPSETTINGSFILE = "appsettings.json";

        public static bool FromPreferences(Models.Preferences prefs)
        {
            CoreAppSettings.Current.CheckForUpdatesAtStartup = prefs.CheckForUpdatesAtStartup;
            CoreAppSettings.Current.EnableAppTelematics = prefs.EnableAppTelematics;
            CoreAppSettings.Current.EnableDNSValidationChecks = prefs.EnableDNSValidationChecks;
            CoreAppSettings.Current.EnableValidationProxyAPI = prefs.EnableValidationProxyAPI;
            CoreAppSettings.Current.IgnoreStoppedSites = prefs.IgnoreStoppedSites;
            CoreAppSettings.Current.MaxRenewalRequests = prefs.MaxRenewalRequests;
            CoreAppSettings.Current.RenewalIntervalDays = prefs.RenewalIntervalDays;
            CoreAppSettings.Current.EnableEFS = prefs.EnableEFS;

            return true;
        }

        public static Models.Preferences ToPreferences()
        {
            LoadAppSettings();
            Models.Preferences prefs = new Models.Preferences();

            prefs.CheckForUpdatesAtStartup = CoreAppSettings.Current.CheckForUpdatesAtStartup;
            prefs.EnableAppTelematics = CoreAppSettings.Current.EnableAppTelematics;
            prefs.EnableDNSValidationChecks = CoreAppSettings.Current.EnableDNSValidationChecks;
            prefs.EnableValidationProxyAPI = CoreAppSettings.Current.EnableValidationProxyAPI;
            prefs.IgnoreStoppedSites = CoreAppSettings.Current.IgnoreStoppedSites;
            prefs.MaxRenewalRequests = CoreAppSettings.Current.MaxRenewalRequests;
            prefs.RenewalIntervalDays = CoreAppSettings.Current.RenewalIntervalDays;
            prefs.EnableEFS = CoreAppSettings.Current.EnableEFS;

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
                lock (COREAPPSETTINGSFILE)
                {
                    string configData = System.IO.File.ReadAllText(path);
                    CoreAppSettings.Current = Newtonsoft.Json.JsonConvert.DeserializeObject<CoreAppSettings>(configData);
                }
            }
            else
            {
                // no core app settings yet, migrate from old settings
                Certify.Properties.Settings.Default.Reload();
                var oldProps = Certify.Properties.Settings.Default;
                CoreAppSettings.Current.CheckForUpdatesAtStartup = oldProps.CheckForUpdatesAtStartup;
                CoreAppSettings.Current.EnableAppTelematics = oldProps.EnableAppTelematics;
                CoreAppSettings.Current.EnableDNSValidationChecks = oldProps.EnableDNSValidationChecks;
                CoreAppSettings.Current.EnableEFS = oldProps.EnableEFS;
                CoreAppSettings.Current.EnableValidationProxyAPI = oldProps.EnableValidationProxyAPI;
                CoreAppSettings.Current.IgnoreStoppedSites = oldProps.ShowOnlyStartedWebsites;
                CoreAppSettings.Current.RenewalIntervalDays = oldProps.RenewalIntervalDays;
                CoreAppSettings.Current.MaxRenewalRequests = oldProps.MaxRenewalRequests;
                CoreAppSettings.Current.VaultPath = oldProps.VaultPath;

                CoreAppSettings.Current.LegacySettingsUpgraded = true;
                SaveAppSettings();
            }
        }
    }
}