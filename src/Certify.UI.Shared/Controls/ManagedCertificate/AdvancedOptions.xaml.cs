using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using Certify.Locales;
using Certify.Management;
using Certify.UI.ViewModel;
using Microsoft.Win32;

namespace Certify.UI.Controls.ManagedCertificate
{
    public partial class AdvancedOptions : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ItemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;

        public class MaintenanceWindowViewModel
        {
            public string Id { get; set; }
            public string DisplayText { get; set; }
        }

        public AdvancedOptions()
        {
            InitializeComponent();

            // this control stays loaded while the user switches between certificates, so the fields which are populated
            // from the selected item (rather than bound to it) have to be refreshed when the selection changes
            AppViewModel.Current.PropertyChanged -= AppViewModel_PropertyChanged;
            AppViewModel.Current.PropertyChanged += AppViewModel_PropertyChanged;
        }

        private void AppViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ViewModel.AppViewModel.SelectedItem))
            {
                return;
            }

            Dispatcher.Invoke(LoadSelectedItemOptions);
        }

        private void OpenCertificateFile_Click(object sender, RoutedEventArgs e)
        {
            var certPath = ItemViewModel.SelectedItem.CertificatePath;

            //check file exists, if not inform user
            if (!string.IsNullOrEmpty(certPath) && System.IO.File.Exists(certPath))
            {
                //open file, can fail if file is in use TODO: will fail if cert has a pwd
                X509Certificate2 cert = null;
                try
                {
                    cert = CertificateManager.LoadCertificate(certPath, pwd: ItemViewModel.PfxUnlockPassword ?? "");
                }
                catch
                { }

                if (cert != null)
                {
                    X509Certificate2UI.DisplayCertificate(cert);

                    cert?.Dispose();
                }
                else
                {
                    MessageBox.Show("Could not open certificate file, file may be in use or unlock password may be incorrect.");
                }
            }
            else
            {
                MessageBox.Show(SR.ManagedCertificateSettings_CertificateNotReady);
            }
        }

        private async void RevokeCertificate_Click(object sender, RoutedEventArgs e)
        {
            // check cert exists, if not inform user
            var certPath = ItemViewModel.SelectedItem.CertificatePath;
            if (string.IsNullOrEmpty(certPath) || !File.Exists(certPath))
            {
                MessageBox.Show(SR.ManagedCertificateSettings_CertificateNotReady, SR.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (MessageBox.Show(SR.ManagedCertificateSettings_ConfirmRevokeCertificate, SR.Alert, MessageBoxButton.OKCancel, MessageBoxImage.Exclamation) == MessageBoxResult.OK)
            {
                try
                {
                    RevokeCertificateBtn.IsEnabled = false;
                    var result = await ItemViewModel.RevokeSelectedItem();
                    if (result.IsOK)
                    {
                        MessageBox.Show(SR.ManagedCertificateSettings_Certificate_Revoked, SR.Alert, MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(string.Format(SR.ManagedCertificateSettings_RevokeCertificateError, result.Message), SR.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                finally
                {
                    RevokeCertificateBtn.IsEnabled = true;
                }
            }
        }

        private async void ReapplyCertBindings_Click(object sender, RoutedEventArgs e)
        {
            var certPath = ItemViewModel.SelectedItem.CertificatePath;
            if (!string.IsNullOrEmpty(certPath) && System.IO.File.Exists(certPath))
            {
                if (MessageBox.Show("Re-apply certificate to website bindings?", "Confirm Re-Apply?", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                {
                    await ItemViewModel.ReapplyCertificateBindings(ItemViewModel.SelectedItem.Id, false, false);

                    ViewModel.AppViewModel.Current.ShowNotification("Certificate Redeployment Completed");
                }
            }
            else
            {
                MessageBox.Show(SR.ManagedCertificateSettings_CertificateNotReady);
            }
        }

        private void ClearCustomCSR_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you wish to clear the custom CSR?", "Clear Custom CSR", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
            {
                ItemViewModel.SelectedItem.RequestConfig.CustomCSR = null;
            }
        }

        private void SelectCustomCSR_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                var csrContent = File.ReadAllText(openFileDialog.FileName);

                var isInvalid = false;
                if (csrContent.Contains("CERTIFICATE REQUEST"))
                {
                    // PEM encoded CSR

                    // set CustomCSR field, read domain and SAN
                    // user should not be able to add domains from UI or choose Alg etc as CSR already has that

                    try
                    {

                        var domains = Certify.Shared.Core.Utils.PKI.CSRUtils.DecodeCsrSubjects(csrContent);

                        ItemViewModel.SelectedItem.RequestConfig.CustomCSR = csrContent;

                        var domainOptions = new System.Collections.ObjectModel.ObservableCollection<Models.DomainOption>();
                        foreach (var d in domains)
                        {
                            domainOptions.Add(new Models.DomainOption { Domain = d, IsManualEntry = true, IsPrimaryDomain = (d == domains[0]), IsSelected = true });
                        }

                        ItemViewModel.SelectedItem.DomainOptions = domainOptions;
                        ItemViewModel.SelectedItem.RequestConfig.PrimaryDomain = domainOptions.First(o => o.IsPrimaryDomain).Domain;
                        ItemViewModel.SelectedItem.RequestConfig.SubjectAlternativeNames = domainOptions.Select(d => d.Domain).ToArray();
                    }
                    catch (Exception)
                    {
                        isInvalid = true;
                    }
                }
                else
                {
                    isInvalid = true;
                }

                if (isInvalid)
                {
                    MessageBox.Show("The certificate request could not be read. Check request is a PEM format (text) file with a Certificate Request header.");
                }
            }
        }

        private void SelectCustomPrivateKey_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {

                // PEM encoded key
                // TODO: custom key mean alg can't be selected, validate key is compatible
                try
                {
                    var keyContent = File.ReadAllText(openFileDialog.FileName);

                    // if parsing an openssl produced key file with extra ecparams, remove the params so we can parse the key
                    if (keyContent.Contains("EC PARAMETERS"))
                    {
                        keyContent = keyContent.Substring(keyContent.LastIndexOf("-----BEGIN"));
                    }

                    if (keyContent.Contains("PRIVATE KEY") && Certify.Shared.Core.Utils.PKI.CSRUtils.CanParsePrivateKey(keyContent))
                    {
                        ItemViewModel.SelectedItem.RequestConfig.CustomPrivateKey = keyContent;
                    }
                    else
                    {
                        throw new ArgumentException("Unsupported key format");
                    }
                }
                catch (Exception exp)
                {
                    MessageBox.Show("The private key could not be processed. Key should be unencrypted and in PEM format [" + exp.ToString() + "]");
                }
            }
        }

        private void ClearCustomPrivateKey_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you wish to clear the custom private key?", "Clear Custom Private Key", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
            {
                ItemViewModel.SelectedItem.RequestConfig.CustomPrivateKey = null;
            }
        }

        private void CertificateAuthorityList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ItemViewModel.RaisePropertyChangedEvent(nameof(ItemViewModel.CertificateAuthorityDescription));

            if (ItemViewModel.SelectedItem != null && string.IsNullOrEmpty(ItemViewModel.SelectedItem.CertificateAuthorityId) && ItemViewModel.SelectedItem.UseStagingMode == true)
            {
                ItemViewModel.SelectedItem.UseStagingMode = false;
            }
        }

        private void AddStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            var cred = new Windows.EditCredential
            {
                Owner = Window.GetWindow(this)
            };

            cred.Item.ProviderType = Models.StandardAuthTypes.STANDARD_AUTH_PASSWORD;

            cred.ShowDialog();

            //refresh dependent properties including credentials list

            ItemViewModel.RaisePropertyChangedEvent(null);

            var credential = cred.Item;

            if (cred.Item != null && cred.Item.StorageKey != null)
            {
                CertPasswordCredential.SelectedValue = credential.StorageKey;
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            DataContext = ItemViewModel;

            ItemViewModel.RaisePropertyChangedEvent(null);

            Dispatcher.Invoke(LoadSelectedItemOptions);
        }

        private bool _isLoadingSelectedItemOptions;

        /// <summary>
        /// Populate the options which are read from the selected item on load rather than bound to it. The loading flag stops
        /// the resulting control updates from being written back to the item as if the user had made them.
        /// </summary>
        private void LoadSelectedItemOptions()
        {
            _isLoadingSelectedItemOptions = true;

            try
            {
                LoadMaintenanceWindows();
                LoadCustomRenewalInterval();
            }
            finally
            {
                _isLoadingSelectedItemOptions = false;
            }
        }

        private void LoadMaintenanceWindows()
        {
            var windowOptions = new List<MaintenanceWindowViewModel>
            {
                new MaintenanceWindowViewModel { Id = null, DisplayText = "(Use instance default)" }
            };

            if (ViewModel.AppViewModel.Current.Preferences.MaintenanceWindows != null)
            {
                foreach (var window in ViewModel.AppViewModel.Current.Preferences.MaintenanceWindows.Where(w => w.IsEnabled))
                {
                    windowOptions.Add(new MaintenanceWindowViewModel
                    {
                        Id = window.Id,
                        DisplayText = $"{window.Name} - {window.GetScheduleDescription()}"
                    });
                }
            }

            MaintenanceWindowSelector.ItemsSource = windowOptions;

            // Find and select the matching item
            var currentId = ItemViewModel.SelectedItem?.MaintenanceWindowId;
            var selectedOption = windowOptions.FirstOrDefault(w => w.Id == currentId);
            MaintenanceWindowSelector.SelectedItem = selectedOption ?? windowOptions.First();
        }

        private void MaintenanceWindowSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSelectedItemOptions)
            {
                return;
            }

            if (ItemViewModel.SelectedItem != null && MaintenanceWindowSelector.SelectedItem is MaintenanceWindowViewModel selected)
            {
                ItemViewModel.SelectedItem.MaintenanceWindowId = selected.Id;
            }
        }

        /// <summary>
        /// Populate the custom renewal interval controls from the selected item, describing the instance default which
        /// applies when the item has no custom interval of its own
        /// </summary>
        private void LoadCustomRenewalInterval()
        {
            var item = ItemViewModel.SelectedItem;
            var prefs = AppViewModel.Current.Preferences;

            var instanceInterval = prefs != null
                ? Models.RenewalIntervalModes.GetIntervalDescription(prefs.RenewalIntervalMode, prefs.RenewalIntervalDays)
                : "the instance renewal settings";

            RenewalIntervalInstanceDefault.Text = $"This certificate is renewed using the renewal interval configured for this instance ({instanceInterval}). Optionally set a custom renewal target for this certificate only.";

            var hasCustomInterval = item?.CustomRenewalTarget != null;

            // an item which has a target but no mode of its own is evaluated against the instance mode, so that is what
            // decides whether its target is a percentage or a deprecated day count
            var effectiveMode = item?.CustomRenewalIntervalMode ?? prefs?.RenewalIntervalMode;
            var isDeprecatedMode = hasCustomInterval && Models.RenewalIntervalModes.IsDeprecatedMode(effectiveMode);

            UseCustomRenewalInterval.IsChecked = hasCustomInterval;
            CustomRenewalIntervalPanel.Visibility = hasCustomInterval ? Visibility.Visible : Visibility.Collapsed;

            // a deprecated day based target is not a percentage, so the control starts from the default instead
            CustomRenewalPercentage.Value = hasCustomInterval && !isDeprecatedMode
                ? Models.RenewalIntervalModes.ClampPercentageLifetime(item.CustomRenewalTarget.Value)
                : GetDefaultCustomRenewalPercentage();

            DeprecatedRenewalIntervalWarning.Text = isDeprecatedMode
                ? $"This certificate has a deprecated custom renewal setting ({Models.RenewalIntervalModes.GetIntervalDescription(effectiveMode, item.CustomRenewalTarget.Value)}). Set a percentage of elapsed lifetime above to replace it, or clear the custom renewal interval to use the instance default."
                : string.Empty;

            DeprecatedRenewalIntervalWarning.Visibility = isDeprecatedMode ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// The percentage target an item starts with when a custom renewal interval is first enabled, being the instance
        /// default where that is also a percentage
        /// </summary>
        private static int GetDefaultCustomRenewalPercentage()
        {
            var prefs = AppViewModel.Current.Preferences;

            return prefs != null
                ? Models.RenewalIntervalModes.GetDefaultPercentageLifetime(prefs.RenewalIntervalMode, prefs.RenewalIntervalDays)
                : Models.RenewalIntervalModes.DefaultPercentageLifetime;
        }

        private void UseCustomRenewalInterval_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSelectedItemOptions || ItemViewModel.SelectedItem == null)
            {
                return;
            }

            if (UseCustomRenewalInterval.IsChecked == true)
            {
                CustomRenewalIntervalPanel.Visibility = Visibility.Visible;
                SetCustomRenewalPercentage(CustomRenewalPercentage.Value ?? GetDefaultCustomRenewalPercentage());
            }
            else
            {
                ItemViewModel.SelectedItem.CustomRenewalTarget = null;
                ItemViewModel.SelectedItem.CustomRenewalIntervalMode = null;

                CustomRenewalIntervalPanel.Visibility = Visibility.Collapsed;
                DeprecatedRenewalIntervalWarning.Visibility = Visibility.Collapsed;
            }
        }

        private void CustomRenewalPercentage_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
        {
            if (_isLoadingSelectedItemOptions || ItemViewModel.SelectedItem == null || UseCustomRenewalInterval.IsChecked != true)
            {
                return;
            }

            SetCustomRenewalPercentage(e.NewValue ?? GetDefaultCustomRenewalPercentage());
        }

        /// <summary>
        /// Apply a percentage of lifetime renewal target to the selected item. Percentage of lifetime is the only custom
        /// renewal mode offered per item, so applying it also replaces any deprecated day based mode the item had.
        /// </summary>
        private void SetCustomRenewalPercentage(double percentage)
        {
            ItemViewModel.SelectedItem.CustomRenewalIntervalMode = Models.RenewalIntervalModes.PercentageLifetime;
            ItemViewModel.SelectedItem.CustomRenewalTarget = Models.RenewalIntervalModes.ClampPercentageLifetime((float)percentage);

            DeprecatedRenewalIntervalWarning.Visibility = Visibility.Collapsed;
        }

        private async void ResetFailureInfo_Click(object sender, RoutedEventArgs e)
        {
            // clear all items which affect renewal status decisions
            var result = await AppViewModel.Current.ResetManagedCertificateStatus(ItemViewModel.SelectedItem.Id);

            if (result != null)
            {
                ItemViewModel.SelectedItem = result;
                AppViewModel.Current.ShowNotification("Managed Certificate Status Reset");
            }
            else
            {
                AppViewModel.Current.ShowNotification("Managed Certificate Status Reset Failed", Shared.NotificationType.Error);
            }
        }

        private async void ChallengeCleanup_Click(object sender, RoutedEventArgs e)
        {
            var result = await AppViewModel.Current.PerformChallengeCleanup(ItemViewModel.SelectedItem);

            AppViewModel.Current.ShowNotification("Challenge Cleanup Completed");

        }

        private void PFXPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ItemViewModel.PfxUnlockPassword = PFXPassword.Password;
        }
    }
}
