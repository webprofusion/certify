using System;
using System.Windows;
using System.Windows.Controls;
using Certify.Models;
using Certify.UI.Shared;

namespace Certify.UI.Controls.Settings
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class General : UserControl
    {
        public class Model : BindableBase
        {
            public Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;
            public Models.Preferences Prefs => MainViewModel.Preferences;

            public bool SettingsInitialised { get; set; }
        }
        public Model EditModel { get; set; } = new Model();

        public General()
        {
            InitializeComponent();

            DataContext = EditModel;
        }

        private void Prefs_AfterPropertyChanged(object sender, EventArgs e)
        {
            if (EditModel.SettingsInitialised)
            {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                EditModel.MainViewModel.SavePreferences();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }
        }

        private void LoadCurrentSettings()
        {

            if (!EditModel.MainViewModel.IsServiceAvailable)
            {
                return;
            }

            if (EditModel.Prefs.CertificateCleanupMode == CertificateCleanupMode.None)
            {
                CertCleanup_None.IsChecked = true;
            }
            else if (EditModel.Prefs.CertificateCleanupMode == CertificateCleanupMode.AfterExpiry)
            {
                CertCleanup_AfterExpiry.IsChecked = true;
            }
            else if (EditModel.Prefs.CertificateCleanupMode == CertificateCleanupMode.AfterRenewal)
            {
                CertCleanup_AfterRenewal.IsChecked = true;
            }
            else if (EditModel.Prefs.CertificateCleanupMode == CertificateCleanupMode.FullCleanup)
            {
                CertCleanup_FullCleanup.IsChecked = true;
            }

            if (EditModel.Prefs.DefaultCertificateStore == "WebHosting")
            {
                CertStoreSelector.SelectedIndex = 1;
            }
            else
            {
                CertStoreSelector.SelectedIndex = 0;
            }

            if (EditModel.Prefs.RenewalIntervalMode == RenewalIntervalModes.DaysBeforeExpiry)
            {
                RenewalIntervalMode_DaysBeforeExpiry.IsChecked = true;
            }
            else
            {
                RenewalIntervalMode_DaysAfterLastRenewal.IsChecked = true;
            }

            ThemeSelector.SelectedValue = EditModel.MainViewModel.UISettings?.UITheme ?? EditModel.MainViewModel.DefaultUITheme;
            CultureSelector.SelectedValue = EditModel.MainViewModel.UISettings?.PreferredUICulture ?? "en-US";

            RefreshRewalIntervalLimits();

            // re-add property changed tracking for save
            EditModel.Prefs.AfterPropertyChanged -= Prefs_AfterPropertyChanged;
            EditModel.Prefs.AfterPropertyChanged += Prefs_AfterPropertyChanged;

            EditModel.SettingsInitialised = true;

            EditModel.RaisePropertyChangedEvent(null);

        }

        private async void SettingsUpdated(object sender, RoutedEventArgs e)
        {
            if (EditModel.SettingsInitialised)
            {

                // cert cleanup mode
                if (CertCleanup_None.IsChecked == true)
                {
                    EditModel.Prefs.CertificateCleanupMode = CertificateCleanupMode.None;
                    EditModel.Prefs.EnableCertificateCleanup = false;
                }
                else if (CertCleanup_AfterExpiry.IsChecked == true)
                {
                    EditModel.Prefs.CertificateCleanupMode = CertificateCleanupMode.AfterExpiry;
                    EditModel.Prefs.EnableCertificateCleanup = true;
                }
                else if (CertCleanup_AfterRenewal.IsChecked == true)
                {
                    EditModel.Prefs.CertificateCleanupMode = CertificateCleanupMode.AfterRenewal;
                    EditModel.Prefs.EnableCertificateCleanup = true;
                }
                else if (CertCleanup_FullCleanup.IsChecked == true)
                {
                    EditModel.Prefs.CertificateCleanupMode = CertificateCleanupMode.FullCleanup;
                    EditModel.Prefs.EnableCertificateCleanup = true;
                }

                // cert store
                if (CertStoreSelector.SelectedIndex == 0)
                {
                    EditModel.Prefs.DefaultCertificateStore = null;
                }
                else
                {
                    EditModel.Prefs.DefaultCertificateStore = "WebHosting";
                }

                // renewal mode
                if (RenewalIntervalMode_DaysAfterLastRenewal.IsChecked == true)
                {
                    EditModel.Prefs.RenewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;
                    RefreshRewalIntervalLimits();
                }
                else
                {
                    EditModel.Prefs.RenewalIntervalMode = RenewalIntervalModes.DaysBeforeExpiry;
                    RefreshRewalIntervalLimits();
                }

                // save settings
                await EditModel.MainViewModel.SavePreferences();

            }
        }

        /// <summary>
        /// Apply min/max setting to interval days input for the given mode
        /// </summary>
        private void RefreshRewalIntervalLimits()
        {
            if (EditModel.Prefs.RenewalIntervalMode == RenewalIntervalModes.DaysAfterLastRenewal)
            {
                RenewalIntervalDays.Minimum = 1;
                RenewalIntervalDays.Maximum = 60;
            }
            else if (EditModel.Prefs.RenewalIntervalMode == RenewalIntervalModes.DaysBeforeExpiry)
            {
                RenewalIntervalDays.Minimum = 14;
                RenewalIntervalDays.Maximum = 180;
            }

            if (EditModel.Prefs.RenewalIntervalDays < RenewalIntervalDays.Minimum)
            {
                EditModel.Prefs.RenewalIntervalDays = (int)RenewalIntervalDays.Minimum;
            }
            else if (EditModel.Prefs.RenewalIntervalDays > RenewalIntervalDays.Maximum)
            {
                EditModel.Prefs.RenewalIntervalDays = (int)RenewalIntervalDays.Maximum;
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e) => LoadCurrentSettings();

        private void ThemeSelector_Selected(object sender, RoutedEventArgs e)
        {
            var theme = (sender as ComboBox).SelectedValue?.ToString();

            if (theme != null)
            {
                ((ICertifyApp)EditModel.MainViewModel.GetApplication()).ToggleTheme(theme);

                if (EditModel.MainViewModel.UISettings == null)
                {
                    EditModel.MainViewModel.UISettings = new UI.Settings.UISettings();
                }

                EditModel.MainViewModel.UISettings.UITheme = theme;
                UI.Settings.UISettings.Save(EditModel.MainViewModel.UISettings);
            }
        }

        private void ImportExport_Click(object sender, RoutedEventArgs e)
        {
            //present dialog
            var d = new Windows.ImportExport
            {
                Owner = Window.GetWindow(this)
            };
            d.ShowDialog();
        }

        private void CultureSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CultureSelector.SelectedValue != null)
            {
                try
                {
                    var cultureID = CultureSelector.SelectedValue?.ToString();
                    if (!string.IsNullOrEmpty(cultureID))
                    {
                        ((ICertifyApp)Application.Current).ChangeCulture(cultureID, true);
                        EditModel.MainViewModel.UISettings.PreferredUICulture = cultureID;
                    }
                }
                catch
                {
                    // some locales may have trouble selecting the new culture
                    MessageBox.Show("Language selection could not be completed.");
                }
            }
        }

        private async void RedeployCertificates_Click(object sender, RoutedEventArgs e)
        {

            var msg = "This action will automatically re-apply all managed certificates as per the Deployment option of each item. Additional Deployment Tasks will not be performed. Redeployment can take up to 1hr and progress will be shown on the Progress tab.";
            if (IncludeDeploymentTasks.IsChecked == true)
            {
                msg = "This action will automatically re-apply all managed certificates as per the Deployment option of each item. Additional Deployment Tasks will also be performed where applicable. Redeployment can take up to 1hr and progress will be shown on the Progress tab.";
            }

            if (MessageBox.Show(msg, "Perform Certificate Redeployment?", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
            {
                await ViewModel.AppViewModel.Current.RedeployManagedCertificatess(isPreviewOnly: false, IncludeDeploymentTasks.IsChecked == true);
            }
        }

        private void EnableExternalPlugins_Click(object sender, RoutedEventArgs e)
        {
            if (EditModel.SettingsInitialised && EnableExternalPlugins.IsChecked == true)
            {
                MessageBox.Show("Enabling custom plugins is a significant security risk. Do not use plugins from unknown third parties.");
            }
        }
    }
}
