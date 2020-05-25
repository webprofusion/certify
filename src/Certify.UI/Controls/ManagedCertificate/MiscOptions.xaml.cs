using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Certify.Locales;
using Certify.Management;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for Deployment.xaml 
    /// </summary>
    public partial class MiscOptions : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ItemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;

        public MiscOptions()
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
                        //var test = cert.PrivateKey.KeyExchangeAlgorithm;
                        // System.Diagnostics.Debug.WriteLine(test.ToString());

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

        private async void RevokeCertificateBtn_Click(object sender, RoutedEventArgs e)
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
                    await ItemViewModel.ReapplyCertificateBindings(ItemViewModel.SelectedItem.Id, false);
                }
            }
            else
            {
                MessageBox.Show(SR.ManagedCertificateSettings_CertificateNotReady);
            }

        }

        private async void RefetchCertificate_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Re-fetch certificate from Certificate Authority?", "Confirm Re-Fetch?", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                await ItemViewModel.RefetchCertificate(ItemViewModel.SelectedItem.Id);
            }
        }


    }
}
