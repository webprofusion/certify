using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Certify.Config.Migration;
using Certify.Models;
using Certify.UI.Settings;

namespace Certify.UI.ViewModel
{
    public partial class AppViewModel : BindableBase
    {
        /// <summary>
        /// Controls which UI tab is currently selected
        /// </summary>
        public int MainUITabIndex { get; set; }

        /// <summary>
        /// Current scaling factor for UI. Larger values increase size of text and UI elements
        /// </summary>
        public double UIScaleFactor { get; set; } = 1;

        /// <summary>
        /// Feature toggled items which no longer require a feature flag
        /// </summary>
        public string[] StandardFeatures = {
            FeatureFlags.EXTERNAL_CERT_MANAGERS,
            FeatureFlags.PRIVKEY_PWD,
            FeatureFlags.IMPORT_EXPORT,
#if DEBUG
            FeatureFlags.SERVER_CONNECTIONS,
#endif
            FeatureFlags.CA_FAILOVER,
            FeatureFlags.CA_EDITOR
        };

        /// <summary>
        /// List of available themes
        /// </summary>
        public Dictionary<string, string> UIThemes { get; } = new Dictionary<string, string>
        {
              {"Light","Light Theme"},
              {"Dark","Dark Theme" }
        };

        /// <summary>
        /// Default theme selection if no theme selected
        /// </summary>
        public string DefaultUITheme = "Light";

        /// <summary>
        /// Set of known UI cultures with translations
        /// </summary>
        public Dictionary<string, string> UICultures { get; } = new Dictionary<string, string>
        {
            {"en-US","English" },
            {"ja-JP","Japanese/日本語"},
            {"es-ES","Spanish/Español"},
            {"nb-NO","Norwegian/Bokmål"},
            {"zh-Hans","Chinese (Simplified)"},
            {"tr-TR","Turkish/Türkçe"},
        };

        /// <summary>
        /// Stored UI settings for last known size/position, UI culture, scaling
        /// </summary>
        public UISettings UISettings { get; set; } = new UI.Settings.UISettings();

        /// <summary>
        /// Overall app preferences
        /// </summary>
        public Preferences Preferences { get; set; } = new Preferences();

        /// <summary>
        /// Get preferences via service
        /// </summary>
        /// <returns></returns>
        internal async Task<Preferences> GetPreferences()
        {
            return await _certifyClient.GetPreferences();
        }

        /// <summary>
        /// Store preferences via service
        /// </summary>
        /// <param name="prefs"></param>
        /// <returns></returns>

        internal async Task SetPreferences(Preferences prefs)
        {
            await _certifyClient.SetPreferences(prefs);
            Preferences = prefs;
        }

        /// <summary>
        /// Check if a given feature option  is enabled
        /// </summary>
        /// <param name="featureFlag"></param>
        /// <returns></returns>
        public bool IsFeatureEnabled(string featureFlag)
        {
            if (StandardFeatures.Any(f => f == featureFlag))
            {
                return true;
            }

            if (Preferences?.FeatureFlags?.Contains(featureFlag) == true)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// lock used for saving changes to preferences
        /// </summary>
        private static SemaphoreSlim _prefLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Save preferences via service
        /// </summary>
        /// <returns></returns>
        public virtual async Task SavePreferences()
        {
            // we use a semaphore to lock the save to preferences to stop multiple callers saves prefs at the same time (unlikely)
            await _prefLock.WaitAsync(500);
            try
            {
                await _certifyClient.SetPreferences(Preferences);
            }
            catch
            {
                Debug.WriteLine("Pref wait lock exceeded");
            }
            finally
            {
                try
                {
                    _prefLock.Release();
                }
                catch { }
            }
        }

        /// <summary>
        /// Load initial settings including preferences, list of managed sites, primary contact 
        /// </summary>
        /// <returns></returns>
        public virtual async Task LoadSettingsAsync()
        {
            Preferences = await GetPreferences();

            await RefreshAllDataStoreItems();

            await RefreshChallengeAPIList();
            await RefreshDeploymentTaskProviderList();

        }

        /// <summary>
        /// Perform full export of app configuration
        /// </summary>
        /// <param name="filter"></param>
        /// <param name="settings"></param>
        /// <param name="isPreview">If true, export is a preview only</param>
        /// <returns></returns>
        public async Task<ImportExportPackage> GetSettingsExport(ManagedCertificateFilter filter, ExportSettings settings, bool isPreview)
        {
            var pkg = await _certifyClient.PerformExport(new ExportRequest { Filter = filter, Settings = settings, IsPreviewMode = isPreview });
            return pkg;
        }

        /// <summary>
        /// Perform import of app configuration
        /// </summary>
        /// <param name="package"></param>
        /// <param name="settings"></param>
        /// <param name="isPreviewMode">If true, import is a preview only</param>
        /// <returns></returns>
        public async Task<List<ActionStep>> PerformSettingsImport(ImportExportPackage package, ImportSettings settings, bool isPreviewMode)
        {
            var results = await _certifyClient.PerformImport(new ImportRequest { Package = package, Settings = settings, IsPreviewMode = isPreviewMode });
            return results.ToList();
        }
    }
}
