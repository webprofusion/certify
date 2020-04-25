using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Certify.Models;

namespace Certify.UI.Controls.Settings
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class General : UserControl
    {
        public Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;
        public Models.Preferences Prefs => MainViewModel.Preferences;

        private bool settingsInitialised = false;

        public General()
        {
            InitializeComponent();

        }

        private void Prefs_AfterPropertyChanged(object sender, EventArgs e)
        {
            if (settingsInitialised)
            {
                MainViewModel.SavePreferences();
            }
        }

        private void LoadCurrentSettings()
        {

            if (!MainViewModel.IsServiceAvailable)
            {
                return;
            }

            if (Prefs.UseBackgroundServiceAutoRenewal)
            {
                // if scheduled task not in use, remove legacy option to modify
                ConfigureAutoRenew.Visibility = Visibility.Collapsed;
            }

            if (Prefs.CertificateCleanupMode == Models.CertificateCleanupMode.None)
            {
                CertCleanup_None.IsChecked = true;
            }
            else if (Prefs.CertificateCleanupMode == Models.CertificateCleanupMode.AfterExpiry)
            {
                CertCleanup_AfterExpiry.IsChecked = true;
            }
            else if (Prefs.CertificateCleanupMode == Models.CertificateCleanupMode.AfterRenewal)
            {
                CertCleanup_AfterRenewal.IsChecked = true;
            }
            else if (Prefs.CertificateCleanupMode == Models.CertificateCleanupMode.FullCleanup)
            {
                CertCleanup_FullCleanup.IsChecked = true;
            }

            ThemeSelector.SelectedValue = MainViewModel.UISettings?.UITheme ?? "Light";

            DataContext = this;

            // re-add property changed tracking for save
            Prefs.AfterPropertyChanged -= Prefs_AfterPropertyChanged;
            Prefs.AfterPropertyChanged += Prefs_AfterPropertyChanged;


            settingsInitialised = true;
        }

        private void Button_ScheduledTaskConfig(object sender, RoutedEventArgs e)
        {
            //show UI to update auto renewal task
            var d = new Windows.ScheduledTaskConfig { Owner = App.Current.MainWindow };

            d.ShowDialog();
        }
        private async void SettingsUpdated(object sender, RoutedEventArgs e)
        {
            if (settingsInitialised)
            {

                // cert cleanup mode
                if (CertCleanup_None.IsChecked == true)
                {
                    Prefs.CertificateCleanupMode = Models.CertificateCleanupMode.None;
                    Prefs.EnableCertificateCleanup = false;
                }
                else if (CertCleanup_AfterExpiry.IsChecked == true)
                {
                    Prefs.CertificateCleanupMode = Models.CertificateCleanupMode.AfterExpiry;
                    Prefs.EnableCertificateCleanup = true;
                }
                else if (CertCleanup_AfterRenewal.IsChecked == true)
                {
                    Prefs.CertificateCleanupMode = Models.CertificateCleanupMode.AfterRenewal;
                    Prefs.EnableCertificateCleanup = true;
                }
                else if (CertCleanup_FullCleanup.IsChecked == true)
                {
                    Prefs.CertificateCleanupMode = Models.CertificateCleanupMode.FullCleanup;
                    Prefs.EnableCertificateCleanup = true;
                }

                // save settings
                await MainViewModel.SavePreferences();

            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e) => LoadCurrentSettings();

        private void ThemeSelector_Selected(object sender, RoutedEventArgs e)
        {
            var theme = (sender as ComboBox).SelectedValue?.ToString();

            if (theme != null)
            {
                ((Certify.UI.App)App.Current).ToggleTheme(theme);

                if (MainViewModel.UISettings == null)
                {
                    MainViewModel.UISettings = new UI.Settings.UISettings();
                }

                MainViewModel.UISettings.UITheme = theme;
                UI.Settings.UISettings.Save(MainViewModel.UISettings);
            }

        }
    }
}
