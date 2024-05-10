using System;
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

        public AdvancedOptions()
        {
            InitializeComponent();
        }

        private void OpenCertificateFile_Click(object sender, RoutedEventArgs e)
        {
            var certPath = ItemViewModel.SelectedItem.CertificatePath;

            //check file exists, if not inform user
            if (!string.IsNullOrEmpty(certPath) && System.IO.File.Exists(certPath))
            {
                //open file, can fail if file is in use TODO: will fail if cert has a pwd
                try
                {

                    var cert = CertificateManager.LoadCertificate(certPath);

                    if (cert != null)
                    {
                        X509Certificate2UI.DisplayCertificate(cert);
                    }
                }
                catch { }
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
        }

        private void ResetFailureInfo_Click(object sender, RoutedEventArgs e)
        {
            // clear all items which affect renewal status decisions
            ItemViewModel.SelectedItem.RenewalFailureCount = 0;
            ItemViewModel.SelectedItem.RenewalFailureMessage = null;
            ItemViewModel.SelectedItem.LastAttemptedCA = null;
            ItemViewModel.SelectedItem.CurrentOrderUri = null;
            ItemViewModel.SelectedItem.ARICertificateId = null;
            ItemViewModel.SelectedItem.DateNextScheduledRenewalAttempt = null;
            ItemViewModel.SelectedItem.DateLastOcspCheck = null;
            ItemViewModel.SelectedItem.DateLastRenewalInfoCheck = null;
            ItemViewModel.SelectedItem.DateLastRenewalAttempt = null;
            ItemViewModel.SelectedItem.LastRenewalStatus = Models.RequestState.Success;

        }

        private async void ChallengeCleanup_Click(object sender, RoutedEventArgs e)
        {
            var result = await AppViewModel.Current.PerformChallengeCleanup(ItemViewModel.SelectedItem);

            AppViewModel.Current.ShowNotification(result.FirstOrDefault().Message);
        }
    }
}
