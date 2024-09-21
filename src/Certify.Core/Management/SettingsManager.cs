﻿using System;
using System.Collections.Generic;
using System.IO;
using Certify.Models;

namespace Certify.Management
{
    public sealed class CoreAppSettings
    {
        private static volatile CoreAppSettings instance;
        private static readonly Lock syncRoot = LockFactory.Create();

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
            RenewalIntervalDays = 75;
            RenewalIntervalMode = RenewalIntervalModes.PercentageLifetime;
            DefaultKeyType = StandardKeyTypes.RSA256;
            MaxRenewalRequests = 0;
            EnableHttpChallengeServer = true;
            LegacySettingsUpgraded = false;
            EnableCertificateCleanup = true;
            EnableStatusReporting = true;
            InstanceId = null;
            CertificateAuthorityFallback = null;
            DefaultCertificateAuthority = "letsencrypt.org";
            EnableAutomaticCAFailover = true;
            EnableExternalCertManagers = true;
            UseModernPFXAlgs = false;
            NtpServer = "pool.ntp.org";
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

        public string ConfigDataStoreConnectionId { get; set; }
        public string DefaultKeyType { get; set; }

        /// <summary>
        /// If true, renewal tasks in batch will run simultaneously
        /// </summary>
        public bool EnableParallelRenewals { get; set; }

        /// <summary>
        /// If set, customizes the ACME retry interval for operations such as polling order status where Retry After not supported by CA
        /// </summary>
        public int DefaultACMERetryInterval { get; set; }

        public bool EnableIssuerCache { get; set; }

        /// <summary>
        /// If true, challenge cleanup will only happen after all auth challenges in an order have been processed
        /// </summary>
        public bool PerformChallengeCleanupsLast { get; set; }
        public string CurrentServiceVersion { get; set; }
    }

    public class SettingsManager
    {
        private const string COREAPPSETTINGSFILE = "appsettings.json";
        private static readonly Lock settingsLocker = LockFactory.Create();

        public static bool FromPreferences(Models.Preferences prefs)
        {
            CoreAppSettings.Current.EnableAppTelematics = prefs.EnableAppTelematics;
            CoreAppSettings.Current.EnableDNSValidationChecks = prefs.EnableDNSValidationChecks;
            CoreAppSettings.Current.EnableValidationProxyAPI = prefs.EnableValidationProxyAPI;
            CoreAppSettings.Current.IgnoreStoppedSites = prefs.IgnoreStoppedSites;
            CoreAppSettings.Current.MaxRenewalRequests = prefs.MaxRenewalRequests;
            CoreAppSettings.Current.RenewalIntervalMode = prefs.RenewalIntervalMode;
            CoreAppSettings.Current.RenewalIntervalDays = prefs.RenewalIntervalDays;
            CoreAppSettings.Current.EnableEFS = prefs.EnableEFS;
            CoreAppSettings.Current.IsInstanceRegistered = prefs.IsInstanceRegistered;
            CoreAppSettings.Current.Language = prefs.Language;
            CoreAppSettings.Current.EnableHttpChallengeServer = prefs.EnableHttpChallengeServer;
            CoreAppSettings.Current.EnableCertificateCleanup = prefs.EnableCertificateCleanup;
            CoreAppSettings.Current.DefaultCertificateStore = prefs.DefaultCertificateStore;

            CoreAppSettings.Current.DefaultCertificateAuthority = prefs.DefaultCertificateAuthority;
            CoreAppSettings.Current.EnableAutomaticCAFailover = prefs.EnableAutomaticCAFailover;

            CoreAppSettings.Current.DefaultKeyCredentials = prefs.DefaultKeyCredentials;
            CoreAppSettings.Current.UseModernPFXAlgs = prefs.UseModernPFXAlgs;

            if (prefs.CertificateCleanupMode == null)
            {
                CoreAppSettings.Current.CertificateCleanupMode = CertificateCleanupMode.AfterExpiry;
            }
            else
            {
                CoreAppSettings.Current.CertificateCleanupMode = (CertificateCleanupMode)prefs.CertificateCleanupMode;
            }

            CoreAppSettings.Current.EnableStatusReporting = prefs.EnableStatusReporting;

            CoreAppSettings.Current.IncludeExternalPlugins = prefs.IncludeExternalPlugins;

            CoreAppSettings.Current.FeatureFlags = prefs.FeatureFlags;

            CoreAppSettings.Current.NtpServer = prefs.NtpServer;

            CoreAppSettings.Current.EnableExternalCertManagers = prefs.EnableExternalCertManagers;

            CoreAppSettings.Current.ConfigDataStoreConnectionId = prefs.ConfigDataStoreConnectionId;

            CoreAppSettings.Current.DefaultKeyType = prefs.DefaultKeyType;

            CoreAppSettings.Current.EnableParallelRenewals = prefs.EnableParallelRenewals;

            CoreAppSettings.Current.DefaultACMERetryInterval = prefs.DefaultACMERetryInterval;

            CoreAppSettings.Current.EnableIssuerCache = prefs.EnableIssuerCache;
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
                RenewalIntervalMode = CoreAppSettings.Current.RenewalIntervalMode,
                RenewalIntervalDays = CoreAppSettings.Current.RenewalIntervalDays,
                EnableEFS = CoreAppSettings.Current.EnableEFS,
                InstanceId = CoreAppSettings.Current.InstanceId,
                IsInstanceRegistered = CoreAppSettings.Current.IsInstanceRegistered,
                Language = CoreAppSettings.Current.Language,
                EnableHttpChallengeServer = CoreAppSettings.Current.EnableHttpChallengeServer,
                EnableCertificateCleanup = CoreAppSettings.Current.EnableCertificateCleanup,
                DefaultCertificateStore = CoreAppSettings.Current.DefaultCertificateStore,
                EnableStatusReporting = CoreAppSettings.Current.EnableStatusReporting,
                CertificateCleanupMode = CoreAppSettings.Current.CertificateCleanupMode,
                DefaultCertificateAuthority = CoreAppSettings.Current.DefaultCertificateAuthority,
                DefaultKeyCredentials = CoreAppSettings.Current.DefaultKeyCredentials,
                EnableAutomaticCAFailover = CoreAppSettings.Current.EnableAutomaticCAFailover,
                UseModernPFXAlgs = CoreAppSettings.Current.UseModernPFXAlgs,
                IncludeExternalPlugins = CoreAppSettings.Current.IncludeExternalPlugins,
                FeatureFlags = CoreAppSettings.Current.FeatureFlags,
                NtpServer = CoreAppSettings.Current.NtpServer,
                EnableExternalCertManagers = CoreAppSettings.Current.EnableExternalCertManagers,
                ConfigDataStoreConnectionId = CoreAppSettings.Current.ConfigDataStoreConnectionId,
                DefaultKeyType = CoreAppSettings.Current.DefaultKeyType,
                EnableParallelRenewals = CoreAppSettings.Current.EnableParallelRenewals,
                DefaultACMERetryInterval = CoreAppSettings.Current.DefaultACMERetryInterval
            };

            return prefs;
        }

        public static void SaveAppSettings()
        {
            var appDataPath = EnvironmentUtil.CreateAppDataPath();
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(CoreAppSettings.Current, Newtonsoft.Json.Formatting.Indented);

            lock (settingsLocker)
            {
                System.IO.File.WriteAllText(Path.Combine(appDataPath, COREAPPSETTINGSFILE), json);
            }
        }

        public static List<CertificateAuthority> GetCustomCertificateAuthorities()
        {
            var caList = new List<CertificateAuthority>();
            var appDataPath = EnvironmentUtil.CreateAppDataPath();
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

            var appDataPath = EnvironmentUtil.CreateAppDataPath();
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
                var appDataPath = EnvironmentUtil.CreateAppDataPath();
                var path = Path.Combine(appDataPath, COREAPPSETTINGSFILE);

                if (System.IO.File.Exists(path))
                {
                    //ensure permissions

                    //load content
                    lock (settingsLocker)
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
