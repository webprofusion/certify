using System;
using System.Collections.Generic;
using System.IO;
using Certify.Models;

namespace Certify.Management
{
    public sealed class CoreAppSettings
    {
        private static volatile CoreAppSettings instance;
        private static object syncRoot = new object();

        private CoreAppSettings()
        {
            // defaults
            SettingsSchemaVersion = 1;
            CheckForUpdatesAtStartup = true;
            EnableAppTelematics = true;
            IgnoreStoppedSites = true;
            EnableValidationProxyAPI = true;
            EnableAppTelematics = true;
            EnableEFS = false;
            EnableDNSValidationChecks = false;
            RenewalIntervalDays = 30;
            MaxRenewalRequests = 0;
            EnableHttpChallengeServer = true;
            LegacySettingsUpgraded = false;
            EnableCertificateCleanup = true;
            EnableStatusReporting = true;
            InstanceId = null;
            CertificateAuthorityFallback = null;
            DefaultCertificateAuthority = "letsencrypt.org";
            EnableAutomaticCAFailover = false;
            IncludeExternalCertManagers = true;
        }

        public static CoreAppSettings Current
        {
            get
            {
                if (instance != null)
                {
                    return instance;
                }

                lock (syncRoot)
                {
                    if (instance == null)
                    {
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

        public bool EnableHttpChallengeServer { get; set; }

        public bool LegacySettingsUpgraded { get; set; }

        /// <summary>
        /// If true, this instance has been added to server dashboard
        /// </summary>
        public bool IsInstanceRegistered { get; set; }

        /// <summary>
        /// If user opts for renewal failure reporting, generated instance id is used to group results
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// If set, specifies the UI language preference
        /// </summary>
        public string Language { get; set; }

        /// <summary>
        /// If true, daily task performs cleanup of expired certificates created by the app
        /// </summary>
        public bool EnableCertificateCleanup { get; set; }

        /// <summary>
        /// If true, app sends renewal status reports and other user prompts (manual dns steps etc)
        /// to the dashboard service
        /// </summary>
        public bool EnableStatusReporting { get; set; }

        public CertificateCleanupMode? CertificateCleanupMode { get; set; }

        /// <summary>
        /// ID of default CA
        /// </summary>
        public string DefaultCertificateAuthority { get; set; }

        /// <summary>
        /// Id of alternative CA if renewal order fails (none, auto, etc)
        /// </summary>
        public string CertificateAuthorityFallback { get; set; }

        /// <summary>
        /// Id of default credentials (password) to use for private keys etc
        /// </summary>
        public string DefaultKeyCredentials { get; set; }

        /// <summary>
        /// If true, the app will decide which Certificate Authority to choose from the list of supported providers.
        /// The preferred provider will be chosen first, with fallback to any other supported (and configured) providers if a failure occurs.
        /// </summary>
        public bool EnableAutomaticCAFailover { get; set; }


        /// <summary>
        /// if true, will load plugins for external cert managers
        /// </summary>
        public bool IncludeExternalCertManagers { get; set; }


        public string[] FeatureFlags { get; set; }
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
            CoreAppSettings.Current.EnableHttpChallengeServer = prefs.EnableHttpChallengeServer;
            CoreAppSettings.Current.EnableCertificateCleanup = prefs.EnableCertificateCleanup;

            CoreAppSettings.Current.DefaultCertificateAuthority = prefs.DefaultCertificateAuthority;
            CoreAppSettings.Current.EnableAutomaticCAFailover = prefs.EnableAutomaticCAFailover;

            CoreAppSettings.Current.DefaultKeyCredentials = prefs.DefaultKeyCredentials;

            if (prefs.CertificateCleanupMode == null)
            {
                CoreAppSettings.Current.CertificateCleanupMode = CertificateCleanupMode.AfterExpiry;
            }
            else
            {
                CoreAppSettings.Current.CertificateCleanupMode = (CertificateCleanupMode)prefs.CertificateCleanupMode;
            }

            CoreAppSettings.Current.EnableStatusReporting = prefs.EnableStatusReporting;

            CoreAppSettings.Current.IncludeExternalCertManagers = prefs.IncludeExternalCertManagers;
            
            CoreAppSettings.Current.FeatureFlags = prefs.FeatureFlags;

            return true;
        }

        public static Models.Preferences ToPreferences()
        {
            LoadAppSettings();
            var prefs = new Models.Preferences
            {
                EnableAppTelematics = CoreAppSettings.Current.EnableAppTelematics,
                EnableDNSValidationChecks = CoreAppSettings.Current.EnableDNSValidationChecks,
                EnableValidationProxyAPI = CoreAppSettings.Current.EnableValidationProxyAPI,
                IgnoreStoppedSites = CoreAppSettings.Current.IgnoreStoppedSites,
                MaxRenewalRequests = CoreAppSettings.Current.MaxRenewalRequests,
                RenewalIntervalDays = CoreAppSettings.Current.RenewalIntervalDays,
                EnableEFS = CoreAppSettings.Current.EnableEFS,
                InstanceId = CoreAppSettings.Current.InstanceId,
                IsInstanceRegistered = CoreAppSettings.Current.IsInstanceRegistered,
                Language = CoreAppSettings.Current.Language,
                EnableHttpChallengeServer = CoreAppSettings.Current.EnableHttpChallengeServer,
                EnableCertificateCleanup = CoreAppSettings.Current.EnableCertificateCleanup,
                EnableStatusReporting = CoreAppSettings.Current.EnableStatusReporting,
                CertificateCleanupMode = CoreAppSettings.Current.CertificateCleanupMode,
                DefaultCertificateAuthority = CoreAppSettings.Current.DefaultCertificateAuthority,
                DefaultKeyCredentials = CoreAppSettings.Current.DefaultKeyCredentials,
                EnableAutomaticCAFailover = CoreAppSettings.Current.EnableAutomaticCAFailover,
                IncludeExternalCertManagers = CoreAppSettings.Current.IncludeExternalCertManagers,
                FeatureFlags = CoreAppSettings.Current.FeatureFlags
            };

            return prefs;
        }

        public static void SaveAppSettings()
        {
            var appDataPath = Util.GetAppDataFolder();
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(CoreAppSettings.Current, Newtonsoft.Json.Formatting.Indented);

            lock (COREAPPSETTINGSFILE)
            {
                System.IO.File.WriteAllText(Path.Combine(appDataPath, COREAPPSETTINGSFILE), json);
            }
        }

        public static List<CertificateAuthority> GetCustomCertificateAuthorities()
        {
            var caList = new List<CertificateAuthority>();
            var appDataPath = Util.GetAppDataFolder();
            var path = Path.Combine(appDataPath, "ca.json");

            if (System.IO.File.Exists(path))
            {
                var configData = System.IO.File.ReadAllText(path);
                try
                {
                    caList = Newtonsoft.Json.JsonConvert.DeserializeObject<List<CertificateAuthority>>(configData);

                }
                catch (Exception exp)
                {
                    throw new Exception($"Failed to load custom certificate authorities:: {path} {exp}");
                }

            }

            return caList;
        }

        public static bool SaveCustomCertificateAuthorities(List<CertificateAuthority> caList)
        {

            var appDataPath = Util.GetAppDataFolder();
            var path = Path.Combine(appDataPath, "ca.json");

            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(caList, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(path, json);
                return true;
            }
            catch (Exception exp)
            {
                // Failed to save custom certificate authorities
                return false;
            }
        }

        public static void LoadAppSettings()
        {
            try
            {
                var appDataPath = Util.GetAppDataFolder();
                var path = Path.Combine(appDataPath, COREAPPSETTINGSFILE);

                if (System.IO.File.Exists(path))
                {
                    //ensure permissions

                    //load content
                    lock (COREAPPSETTINGSFILE)
                    {
                        var configData = System.IO.File.ReadAllText(path);
                        CoreAppSettings.Current = Newtonsoft.Json.JsonConvert.DeserializeObject<CoreAppSettings>(configData);

                        // init new settings if not set
                        if (CoreAppSettings.Current.CertificateCleanupMode == null)
                        {
                            CoreAppSettings.Current.CertificateCleanupMode = CertificateCleanupMode.AfterExpiry;
                        }

                    }
                }
                else
                {
                    // no core app settings yet

                    ApplyDefaults();
                    SaveAppSettings();
                }

                // if instance id not yet set, create it now and save
                if (string.IsNullOrEmpty(CoreAppSettings.Current.InstanceId))
                {
                    CoreAppSettings.Current.InstanceId = Guid.NewGuid().ToString();
                    SaveAppSettings();
                }
            }
            catch (Exception)
            {
                // failed to load app settings, settings may be corrupt or user may not have permission to read or write
                // use defaults, but don't save

                ApplyDefaults();
            }
        }

        private static void ApplyDefaults()
        {
            CoreAppSettings.Current.LegacySettingsUpgraded = true;
            CoreAppSettings.Current.IsInstanceRegistered = false;
            CoreAppSettings.Current.Language = null;
            CoreAppSettings.Current.CertificateCleanupMode = CertificateCleanupMode.AfterExpiry;

            CoreAppSettings.Current.InstanceId = Guid.NewGuid().ToString();
        }
    }
}
