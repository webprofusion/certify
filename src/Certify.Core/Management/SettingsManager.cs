using System;
using System.Collections.Generic;
using System.IO;
using Certify.Models;
using Certify.Models.Config;

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
            EnableDNSValidationChecks = false;
            RenewalIntervalDays = RenewalIntervalModes.DefaultPercentageLifetime;
            RenewalIntervalMode = RenewalIntervalModes.PercentageLifetime;
            DefaultKeyType = StandardKeyTypes.RSA256;
            MaxRenewalRequests = 0;
            EnableHttpChallengeServer = true;
            LegacySettingsUpgraded = false;
            EnableCertificateCleanup = true;
            EnableStatusReporting = true;
            NotificationEmail = null;
            InstanceId = null;
            CertificateAuthorityFallback = null;
            DefaultCertificateAuthority = "letsencrypt.org";
            EnableAutomaticCAFailover = true;
            EnableExternalCertManagers = true;
            UseModernPFXAlgs = false;
            WindowsKeyStorageProvider = null;
            CsrCommonNameMode = CsrCommonNameModes.IncludeInCsr;
            NtpServer = "pool.ntp.org";
            CertificateManagers = new List<CertificateManagerPreference>();
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

        public bool EnableDNSValidationChecks { get; set; }

        /// <summary>
        /// Number of days between renewals
        /// </summary>
        public int RenewalIntervalDays { get; set; }

        /// <summary>
        /// Renewal interval mode DaysAfterLastRenewal (default), DaysBeforeExpiry
        /// </summary>
        public string RenewalIntervalMode { get; set; }
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

        /// <summary>
        /// Optional per-instance notification email address used for status reports and dashboard notifications.
        /// If blank, the service falls back to the ACME account email where applicable.
        /// </summary>
        public string? NotificationEmail { get; set; }

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
        /// If true, will allow plugins to load from appdata
        /// </summary>
        public bool IncludeExternalPlugins { get; set; }

        public string[] FeatureFlags { get; set; }

        /// <summary>
        /// Server to use for Ntp time diagnostics
        /// </summary>
        public string NtpServer { get; set; }
        public string DefaultCertificateStore { get; set; }
        public bool EnableExternalCertManagers { get; set; }

        /// <summary>
        /// If true, use modern key and cert algorithms
        /// </summary>
        public bool UseModernPFXAlgs { get; set; }

        /// <summary>
        /// Windows only: CSP or CNG key storage provider to import certificate private keys into. Null/empty uses the
        /// system default (CNG).
        /// </summary>
        public string WindowsKeyStorageProvider { get; set; }

        public string ConfigDataStoreConnectionId { get; set; }
        public string DefaultKeyType { get; set; }

        /// <summary>
        /// Defines how Common Name (CN) should be handled for generated CSRs.
        /// </summary>
        public string CsrCommonNameMode { get; set; }

        /// <summary>
        /// If true, renewal tasks in batch will run simultaneously
        /// </summary>
        public bool EnableParallelRenewals { get; set; }

        /// <summary>
        /// If set, customizes the ACME retry interval for operations such as polling order status where Retry After not supported by CA
        /// </summary>
        public int DefaultACMERetryInterval { get; set; }

        /// <summary>
        /// If true, Windows certificate store deployment will also add included intermediate certificates to the Local Machine CA store
        /// </summary>
        public bool StoreCertificateIntermediates { get; set; }

        public bool EnableIssuerCache { get; set; }

        /// <summary>
        /// If true, challenge cleanup will only happen after all auth challenges in an order have been processed
        /// </summary>
        public bool PerformChallengeCleanupsLast { get; set; }
        public string CurrentServiceVersion { get; set; }

        /// <summary>
        /// If true, ARI checks will not be performed during periodic maintenance
        /// </summary>
        public bool DisableARIChecks { get; set; }

        /// <summary>
        /// If true, identifiers the CA adds to an ACME order which were not requested (e.g. www) are not included in the CSR
        /// </summary>
        public bool StrictAcmeOrderIdentifiers { get; set; }

        public List<CertificateManagerPreference> CertificateManagers { get; set; } = new List<CertificateManagerPreference>();

        /// <summary>
        /// Collection of named maintenance windows defining when renewals can occur
        /// </summary>
        public List<MaintenanceWindow> MaintenanceWindows { get; set; } = new List<MaintenanceWindow>();

        /// <summary>
        /// If set, the ID of the default maintenance window to use for certificates that don't specify their own
        /// </summary>
        public string? DefaultMaintenanceWindowId { get; set; }

        /// <summary>
        /// Restricts changes made locally, only the management hub can change this setting
        /// </summary>
        public LocalChangeRestriction LocalChangeRestriction { get; set; }

        /// <summary>
        /// True when these are defaults standing in for a settings file which exists but could not be read. Not
        /// serialized: it describes this copy in memory, not the instance's settings
        /// </summary>
        internal bool IsFallbackDefaults { get; set; }
    }

    public class SettingsManager
    {
        private const string COREAPPSETTINGSFILE = "appsettings.json";
        private static Object settingsLocker = new Object();

        public static bool FromPreferences(Models.Preferences prefs)
        {
            CoreAppSettings.Current.EnableAppTelematics = prefs.EnableAppTelematics;
            CoreAppSettings.Current.EnableDNSValidationChecks = prefs.EnableDNSValidationChecks;
            CoreAppSettings.Current.EnableValidationProxyAPI = prefs.EnableValidationProxyAPI;
            CoreAppSettings.Current.IgnoreStoppedSites = prefs.IgnoreStoppedSites;
            CoreAppSettings.Current.MaxRenewalRequests = prefs.MaxRenewalRequests;
            CoreAppSettings.Current.RenewalIntervalMode = prefs.RenewalIntervalMode;
            CoreAppSettings.Current.RenewalIntervalDays = prefs.RenewalIntervalDays;
            CoreAppSettings.Current.IsInstanceRegistered = prefs.IsInstanceRegistered;
            CoreAppSettings.Current.Language = prefs.Language;
            CoreAppSettings.Current.EnableHttpChallengeServer = prefs.EnableHttpChallengeServer;
            CoreAppSettings.Current.DefaultCertificateStore = prefs.DefaultCertificateStore;

            CoreAppSettings.Current.DefaultCertificateAuthority = prefs.DefaultCertificateAuthority;
            CoreAppSettings.Current.EnableAutomaticCAFailover = prefs.EnableAutomaticCAFailover;

            CoreAppSettings.Current.DefaultKeyCredentials = prefs.DefaultKeyCredentials;
            CoreAppSettings.Current.UseModernPFXAlgs = prefs.UseModernPFXAlgs;
            CoreAppSettings.Current.WindowsKeyStorageProvider = prefs.WindowsKeyStorageProvider;

            // the mode is the setting users choose, the enabled flag is what cleanup is gated on, so keep the flag in step
            var cleanupMode = prefs.CertificateCleanupMode ?? GetLegacyCertificateCleanupMode(prefs.EnableCertificateCleanup);
            CoreAppSettings.Current.CertificateCleanupMode = cleanupMode;
            CoreAppSettings.Current.EnableCertificateCleanup = cleanupMode != CertificateCleanupMode.None;

            CoreAppSettings.Current.EnableStatusReporting = prefs.EnableStatusReporting;
            CoreAppSettings.Current.NotificationEmail = prefs.NotificationEmail;

            CoreAppSettings.Current.IncludeExternalPlugins = prefs.IncludeExternalPlugins;

            CoreAppSettings.Current.FeatureFlags = prefs.FeatureFlags;

            CoreAppSettings.Current.NtpServer = prefs.NtpServer;

            CoreAppSettings.Current.EnableExternalCertManagers = prefs.EnableExternalCertManagers;

            CoreAppSettings.Current.ConfigDataStoreConnectionId = prefs.ConfigDataStoreConnectionId;

            CoreAppSettings.Current.DefaultKeyType = prefs.DefaultKeyType;
            CoreAppSettings.Current.CsrCommonNameMode = prefs.CsrCommonNameMode ?? CsrCommonNameModes.IncludeInCsr;

            CoreAppSettings.Current.EnableParallelRenewals = prefs.EnableParallelRenewals;

            CoreAppSettings.Current.DefaultACMERetryInterval = prefs.DefaultACMERetryInterval;

            CoreAppSettings.Current.StoreCertificateIntermediates = prefs.StoreCertificateIntermediates;

            CoreAppSettings.Current.DisableARIChecks = prefs.DisableARIChecks;

            CoreAppSettings.Current.StrictAcmeOrderIdentifiers = prefs.StrictAcmeOrderIdentifiers;

            CoreAppSettings.Current.EnableIssuerCache = prefs.EnableIssuerCache;

            CoreAppSettings.Current.CertificateManagers = prefs.CertificateManagers;

            CoreAppSettings.Current.MaintenanceWindows = prefs.MaintenanceWindows;
            CoreAppSettings.Current.DefaultMaintenanceWindowId = prefs.DefaultMaintenanceWindowId;

            CoreAppSettings.Current.LocalChangeRestriction = prefs.LocalChangeRestriction;

            return true;
        }

        public static Models.Preferences ToPreferences()
        {
            LoadAppSettings();

            return GetCurrentPreferences();
        }

        internal static Models.Preferences GetCurrentPreferences()
        {
            var prefs = new Models.Preferences
            {
                EnableAppTelematics = CoreAppSettings.Current.EnableAppTelematics,
                EnableDNSValidationChecks = CoreAppSettings.Current.EnableDNSValidationChecks,
                EnableValidationProxyAPI = CoreAppSettings.Current.EnableValidationProxyAPI,
                IgnoreStoppedSites = CoreAppSettings.Current.IgnoreStoppedSites,
                MaxRenewalRequests = CoreAppSettings.Current.MaxRenewalRequests,
                RenewalIntervalMode = CoreAppSettings.Current.RenewalIntervalMode,
                RenewalIntervalDays = CoreAppSettings.Current.RenewalIntervalDays,
                InstanceId = CoreAppSettings.Current.InstanceId,
                IsInstanceRegistered = CoreAppSettings.Current.IsInstanceRegistered,
                Language = CoreAppSettings.Current.Language,
                EnableHttpChallengeServer = CoreAppSettings.Current.EnableHttpChallengeServer,
                EnableCertificateCleanup = CoreAppSettings.Current.EnableCertificateCleanup,
                DefaultCertificateStore = CoreAppSettings.Current.DefaultCertificateStore,
                EnableStatusReporting = CoreAppSettings.Current.EnableStatusReporting,
                NotificationEmail = CoreAppSettings.Current.NotificationEmail,
                CertificateCleanupMode = CoreAppSettings.Current.CertificateCleanupMode,
                DefaultCertificateAuthority = CoreAppSettings.Current.DefaultCertificateAuthority,
                DefaultKeyCredentials = CoreAppSettings.Current.DefaultKeyCredentials,
                EnableAutomaticCAFailover = CoreAppSettings.Current.EnableAutomaticCAFailover,
                UseModernPFXAlgs = CoreAppSettings.Current.UseModernPFXAlgs,
                WindowsKeyStorageProvider = CoreAppSettings.Current.WindowsKeyStorageProvider,
                IncludeExternalPlugins = CoreAppSettings.Current.IncludeExternalPlugins,
                FeatureFlags = CoreAppSettings.Current.FeatureFlags ?? [],
                NtpServer = CoreAppSettings.Current.NtpServer,
                EnableExternalCertManagers = CoreAppSettings.Current.EnableExternalCertManagers,
                ConfigDataStoreConnectionId = CoreAppSettings.Current.ConfigDataStoreConnectionId ?? "(default)",
                DefaultKeyType = CoreAppSettings.Current.DefaultKeyType,
                CsrCommonNameMode = CoreAppSettings.Current.CsrCommonNameMode,
                EnableParallelRenewals = CoreAppSettings.Current.EnableParallelRenewals,
                DisableARIChecks = CoreAppSettings.Current.DisableARIChecks,
                StrictAcmeOrderIdentifiers = CoreAppSettings.Current.StrictAcmeOrderIdentifiers,
                DefaultACMERetryInterval = CoreAppSettings.Current.DefaultACMERetryInterval,
                StoreCertificateIntermediates = CoreAppSettings.Current.StoreCertificateIntermediates,
                CertificateManagers = CoreAppSettings.Current.CertificateManagers,
                MaintenanceWindows = CoreAppSettings.Current.MaintenanceWindows,
                DefaultMaintenanceWindowId = CoreAppSettings.Current.DefaultMaintenanceWindowId,
                LocalChangeRestriction = CoreAppSettings.Current.LocalChangeRestriction
            };

            return prefs;
        }

        /// <summary>
        /// Apply a core settings update sent from the management hub to this instance's current preferences. Only the
        /// settings the hub manages are taken from the update, the rest are local to the instance, and a hub on a
        /// different version may not know about them at all.
        /// </summary>
        public static void ApplyHubSettingsUpdate(Models.Preferences prefs, Models.Preferences update)
        {
            prefs.CertificateCleanupMode = update.CertificateCleanupMode;
            prefs.EnableCertificateCleanup = update.EnableCertificateCleanup;
            prefs.DefaultACMERetryInterval = update.DefaultACMERetryInterval;
            prefs.DefaultCertificateAuthority = update.DefaultCertificateAuthority;
            prefs.DefaultCertificateStore = update.DefaultCertificateStore;
            prefs.DefaultKeyType = update.DefaultKeyType;
            prefs.CsrCommonNameMode = update.CsrCommonNameMode;
            prefs.DisableARIChecks = update.DisableARIChecks;
            prefs.StrictAcmeOrderIdentifiers = update.StrictAcmeOrderIdentifiers;

            prefs.EnableAppTelematics = update.EnableAppTelematics;
            prefs.EnableAutomaticCAFailover = update.EnableAutomaticCAFailover;
            prefs.EnableExternalCertManagers = update.EnableExternalCertManagers;
            prefs.EnableStatusReporting = update.EnableStatusReporting;
            prefs.NotificationEmail = update.NotificationEmail;
            prefs.EnableValidationProxyAPI = update.EnableValidationProxyAPI;
            prefs.EnableHttpChallengeServer = update.EnableHttpChallengeServer;
            prefs.IsInstanceRegistered = update.IsInstanceRegistered;

            prefs.NtpServer = update.NtpServer;
            prefs.RenewalIntervalDays = update.RenewalIntervalDays;
            prefs.RenewalIntervalMode = update.RenewalIntervalMode;
            prefs.StoreCertificateIntermediates = update.StoreCertificateIntermediates;
            prefs.UseModernPFXAlgs = update.UseModernPFXAlgs;
            prefs.WindowsKeyStorageProvider = update.WindowsKeyStorageProvider;

            prefs.CertificateManagers = update.CertificateManagers;

            prefs.MaintenanceWindows = update.MaintenanceWindows;
            prefs.DefaultMaintenanceWindowId = update.DefaultMaintenanceWindowId;

            prefs.LocalChangeRestriction = update.LocalChangeRestriction;
        }

        /// <summary>
        /// Apply a preferences update made locally (desktop app, CLI, local service API). The local change restriction
        /// can only be changed by the management hub. While local changes are restricted, only the maintenance windows
        /// and the UI language are taken from the update, and an update which only changes other settings is refused.
        /// </summary>
        public static ActionResult FromLocalPreferences(Models.Preferences prefs)
        {
            var current = GetCurrentPreferences();

            if (current.LocalChangeRestriction != LocalChangeRestriction.None)
            {
                var currentJson = Newtonsoft.Json.JsonConvert.SerializeObject(current);

                var allowed = Newtonsoft.Json.JsonConvert.DeserializeObject<Models.Preferences>(currentJson);
                allowed.MaintenanceWindows = prefs.MaintenanceWindows;
                allowed.DefaultMaintenanceWindowId = prefs.DefaultMaintenanceWindowId;
                allowed.Language = prefs.Language;

                // other settings in an update which changes these may just be stale (the app loaded them before the hub changed them), so are not kept
                if (Newtonsoft.Json.JsonConvert.SerializeObject(allowed) == currentJson && Newtonsoft.Json.JsonConvert.SerializeObject(prefs) != currentJson)
                {
                    return new ActionResult(LocalChangePolicy.GetRefusalMessage(current.LocalChangeRestriction), false);
                }

                prefs = allowed;
            }

            prefs.LocalChangeRestriction = current.LocalChangeRestriction;

            FromPreferences(prefs);

            return new ActionResult("Preferences updated", true);
        }

        /// <summary>
        /// Cleanup mode for settings which predate it and only have the enabled flag
        /// </summary>
        private static CertificateCleanupMode GetLegacyCertificateCleanupMode(bool isCleanupEnabled)
        {
            return isCleanupEnabled ? CertificateCleanupMode.AfterExpiry : CertificateCleanupMode.None;
        }

        public static void SaveAppSettings() => SaveAppSettings(Path.Combine(EnvironmentUtil.EnsuredAppDataPath(), COREAPPSETTINGSFILE));

        internal static void SaveAppSettings(string path)
        {
            lock (settingsLocker)
            {
                var settings = CoreAppSettings.Current;

                // saving defaults over settings which could not be read would replace them, including the instance id
                // the license file is encrypted with and the hub knows the instance by
                if (settings.IsFallbackDefaults && System.IO.File.Exists(path))
                {
                    throw new InvalidOperationException($"Settings file {path} could not be read, so settings cannot be saved over it.");
                }

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented);

                // write then swap in, so a reader (or an interrupted write) never sees a partially written file
                var tempPath = path + ".tmp";
                System.IO.File.WriteAllText(tempPath, json);
                System.IO.File.Move(tempPath, path, overwrite: true);

                settings.IsFallbackDefaults = false;
            }
        }

        public static List<CertificateAuthority> GetCustomCertificateAuthorities()
        {
            var caList = new List<CertificateAuthority>();
            var appDataPath = EnvironmentUtil.EnsuredAppDataPath();
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

            var appDataPath = EnvironmentUtil.EnsuredAppDataPath();
            var path = Path.Combine(appDataPath, "ca.json");

            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(caList, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(path, json);
                return true;
            }
            catch (Exception)
            {
                // Failed to save custom certificate authorities
                return false;
            }
        }

        public static void LoadAppSettings()
        {
            try
            {
                LoadAppSettings(Path.Combine(EnvironmentUtil.EnsuredAppDataPath(), COREAPPSETTINGSFILE));
            }
            catch (Exception)
            {
                // the app data path could not be resolved or created, handled the same as a settings file which could not be read
                KeepOrApplyFallbackDefaults();
            }
        }

        internal static void LoadAppSettings(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    lock (settingsLocker)
                    {
                        var configData = System.IO.File.ReadAllText(path);

                        // an empty file deserializes to null rather than failing, and must not be taken as a new install
                        var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<CoreAppSettings>(configData)
                            ?? throw new InvalidDataException($"Settings file {path} is empty.");

                        // init new settings if not set
                        settings.CertificateCleanupMode ??= GetLegacyCertificateCleanupMode(settings.EnableCertificateCleanup);

                        CoreAppSettings.Current = settings;
                    }
                }
                else
                {
                    // no core app settings yet. Settings already in use (the file has since been removed) are written back as they are
                    if (string.IsNullOrEmpty(CoreAppSettings.Current.InstanceId))
                    {
                        ApplyDefaults();
                    }

                    SaveAppSettings(path);
                }

                // if instance id not yet set, create it now and save
                if (string.IsNullOrEmpty(CoreAppSettings.Current.InstanceId))
                {
                    CoreAppSettings.Current.InstanceId = Guid.NewGuid().ToString();
                    SaveAppSettings(path);
                }
            }
            catch (Exception)
            {
                // failed to load app settings, settings may be corrupt or user may not have permission to read or write
                KeepOrApplyFallbackDefaults();
            }
        }

        /// <summary>
        /// Settings already in use are kept when the settings file cannot be read, since defaults would replace the
        /// instance id the license and hub identity are bound to. With none in use yet, defaults are used but not saved.
        /// </summary>
        private static void KeepOrApplyFallbackDefaults()
        {
            if (string.IsNullOrEmpty(CoreAppSettings.Current.InstanceId))
            {
                ApplyDefaults();
                CoreAppSettings.Current.IsFallbackDefaults = true;
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
