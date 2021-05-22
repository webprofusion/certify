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

            public bool SettingsInitialised { get; set; } = false;

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

            if (EditModel.Prefs.CertificateCleanupMode == Models.CertificateCleanupMode.None)
            {
                CertCleanup_None.IsChecked = true;
            }
            else if (EditModel.Prefs.CertificateCleanupMode == Models.CertificateCleanupMode.AfterExpiry)
            {
                CertCleanup_AfterExpiry.IsChecked = true;
            }
            else if (EditModel.Prefs.CertificateCleanupMode == Models.CertificateCleanupMode.AfterRenewal)
            {
                CertCleanup_AfterRenewal.IsChecked = true;
            }
            else if (EditModel.Prefs.CertificateCleanupMode == Models.CertificateCleanupMode.FullCleanup)
            {
                CertCleanup_FullCleanup.IsChecked = true;
            }

            ThemeSelector.SelectedValue = EditModel.MainViewModel.UISettings?.UITheme ?? EditModel.MainViewModel.DefaultUITheme;
            CultureSelector.SelectedValue = EditModel.MainViewModel.UISettings?.PreferredUICulture ?? "en-US";


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
                    EditModel.Prefs.CertificateCleanupMode = Models.CertificateCleanupMode.None;
                    EditModel.Prefs.EnableCertificateCleanup = false;
                }
                else if (CertCleanup_AfterExpiry.IsChecked == true)
                {
                    EditModel.Prefs.CertificateCleanupMode = Models.CertificateCleanupMode.AfterExpiry;
                    EditModel.Prefs.EnableCertificateCleanup = true;
                }
                else if (CertCleanup_AfterRenewal.IsChecked == true)
                {
                    EditModel.Prefs.CertificateCleanupMode = Models.CertificateCleanupMode.AfterRenewal;
                    EditModel.Prefs.EnableCertificateCleanup = true;
                }
                else if (CertCleanup_FullCleanup.IsChecked == true)
                {
                    EditModel.Prefs.CertificateCleanupMode = Models.CertificateCleanupMode.FullCleanup;
                    EditModel.Prefs.EnableCertificateCleanup = true;
                }

                // save settings
                await EditModel.MainViewModel.SavePreferences();

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
    }
}
