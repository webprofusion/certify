using Certify.Locales;
using Certify.Management;
using Certify.Models;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace Certify.UI.Controls
{
    /// <summary>
    /// Interaction logic for ManagedItemSettings.xaml 
    /// </summary>
    public partial class ManagedItemSettings : UserControl
    {
        public ObservableCollection<SiteBindingItem> WebSiteList { get; set; }

        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return UI.ViewModel.AppModel.AppViewModel;
            }
        }

        public ManagedItemSettings()
        {
            InitializeComponent();
            this.MainViewModel.PropertyChanged += MainViewModel_PropertyChanged;

            WebSiteList = new ObservableCollection<SiteBindingItem>();
        }

        private async void MainViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SelectedItem")
            {
                this.SettingsTab.SelectedIndex = 0;

                if (MainViewModel.SelectedItem != null)
                {
                    //get list of sites from IIS. FIXME: this is async and we should gather this at startup (or on refresh) instead
                    WebSiteList = new ObservableCollection<SiteBindingItem>(await MainViewModel.CertifyClient.GetServerSiteList(StandardServerTypes.IIS));
                    WebsiteDropdown.ItemsSource = WebSiteList;
                }
            }
        }

        private async Task<bool> ValidateAndSave(ManagedSite item)
        {
            if (item.Id == null && MainViewModel.SelectedWebSite == null)
            {
                MessageBox.Show(SR.ManagedItemSettings_SelectWebsiteOrCert, SR.SaveError, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (String.IsNullOrEmpty(item.Name))
            {
                MessageBox.Show(SR.ManagedItemSettings_NameRequired, SR.SaveError, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (MainViewModel.PrimarySubjectDomain == null)
            {
                MessageBox.Show(SR.ManagedItemSettings_NeedPrimaryDomain, SR.SaveError, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (item.RequestConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI &&
                MainViewModel.IISVersion.Major < 8)
            {
                MessageBox.Show(string.Format(SR.ManagedItemSettings_ChallengeNotAvailable, SupportedChallengeTypes.CHALLENGE_TYPE_SNI), SR.SaveError, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (item.RequestConfig.PerformAutomatedCertBinding)
            {
                item.RequestConfig.BindingIPAddress = null;
                item.RequestConfig.BindingPort = null;
                item.RequestConfig.BindingUseSNI = null;
            }
            else
            {
                //always select Use SNI unless it's specifically set to false
                if (item.RequestConfig.BindingUseSNI == null)
                {
                    item.RequestConfig.BindingUseSNI = true;
                }
            }

            if (!string.IsNullOrEmpty(item.RequestConfig.WebhookTrigger) &&
                item.RequestConfig.WebhookTrigger != Webhook.ON_NONE)
            {
                if (string.IsNullOrEmpty(item.RequestConfig.WebhookUrl) ||
                    !Uri.TryCreate(item.RequestConfig.WebhookUrl, UriKind.Absolute, out var uri))
                {
                    MessageBox.Show(SR.ManagedItemSettings_HookMustBeValidUrl, SR.SaveError, MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                if (string.IsNullOrEmpty(item.RequestConfig.WebhookMethod))
                {
                    MessageBox.Show(SR.ManagedItemSettings_HookMethodMustBeSet, SR.SaveError, MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }
            else
            {
                // clear out saved values if setting webhook to NONE
                item.RequestConfig.WebhookUrl = null;
                item.RequestConfig.WebhookMethod = null;
                item.RequestConfig.WebhookContentType = null;
                item.RequestConfig.WebhookContentBody = null;
            }

            //save changes

            //creating new managed item
            return await MainViewModel.SaveManagedItemChanges();
        }

        private async void Button_Save(object sender, RoutedEventArgs e)
        {
            if (MainViewModel.SelectedItem.IsChanged)
            {
                var item = MainViewModel.SelectedItem;
                await ValidateAndSave(item);
            }
            else
            {
                MessageBox.Show(SR.ManagedItemSettings_NoChanges);
            }
        }

        private async void Button_DiscardChanges(object sender, RoutedEventArgs e)
        {
            //if new item, discard and select first item in managed sites
            if (MainViewModel.SelectedItem.Id == null)
            {
                ReturnToDefaultManagedItemView();
            }
            else
            {
                //reload settings for managed sites, discard changes
                await MainViewModel.DiscardChanges();
            }
        }

        private void ReturnToDefaultManagedItemView()
        {
            MainViewModel.SelectedItem = MainViewModel.ManagedSites.FirstOrDefault();
        }

        private async void Button_RequestCertificate(object sender, RoutedEventArgs e)
        {
            if (MainViewModel.SelectedItem != null)
            {
                if (MainViewModel.SelectedItem.IsChanged)
                {
                    var savedOK = await ValidateAndSave(MainViewModel.SelectedItem);
                    if (!savedOK) return;
                }

                //begin request
                MainViewModel.MainUITabIndex = (int)MainWindow.PrimaryUITabs.CurrentProgress;

                await MainViewModel.BeginCertificateRequest(MainViewModel.SelectedItem.Id);
            }
        }

        private async void Button_Delete(object sender, RoutedEventArgs e)
        {
            await MainViewModel.DeleteManagedSite(MainViewModel.SelectedItem);
            if (MainViewModel.SelectedItem?.Id == null)
            {
                MainViewModel.SelectedItem = MainViewModel.ManagedSites.FirstOrDefault();
            }
        }

        private async void Website_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainViewModel.SelectedWebSite != null)
            {
                string siteId = MainViewModel.SelectedWebSite.SiteId;

                SiteQueryInProgress.Visibility = Visibility.Visible;
                await MainViewModel.PopulateManagedSiteSettings(siteId);
                SiteQueryInProgress.Visibility = Visibility.Hidden;
            }
        }

        private void OpenLogFile_Click(object sender, RoutedEventArgs e)
        {
            if (this.MainViewModel?.SelectedItem?.Id == null) return;

            // get file path for log
            var logPath = Models.ManagedSiteLog.GetLogPath(this.MainViewModel.SelectedItem.Id);

            //check file exists, if not inform user
            if (System.IO.File.Exists(logPath))
            {
                //open file
                System.Diagnostics.Process.Start(logPath);
            }
            else
            {
                MessageBox.Show(SR.ManagedItemSettings_LogNotCreated);
            }
        }

        private void OpenCertificateFile_Click(object sender, RoutedEventArgs e)
        {
            // get file path for log
            var certPath = this.MainViewModel.SelectedItem.CertificatePath;

            //check file exists, if not inform user
            if (!String.IsNullOrEmpty(certPath) && System.IO.File.Exists(certPath))
            {
                //open file
                var cert = CertificateManager.LoadCertificate(certPath);

                if (cert != null)
                {
                    //var test = cert.PrivateKey.KeyExchangeAlgorithm;
                    // System.Diagnostics.Debug.WriteLine(test.ToString());

                    X509Certificate2UI.DisplayCertificate(cert);
                }

                //MessageBox.Show(Newtonsoft.Json.JsonConvert.SerializeObject(cert.PrivateKey, Newtonsoft.Json.Formatting.Indented));
            }
            else
            {
                MessageBox.Show(SR.ManagedItemSettings_CertificateNotReady);
            }
        }

        private void DirectoryBrowse_Click(object sender, EventArgs e)
        {
            var config = MainViewModel.SelectedItem.RequestConfig;
            var dialog = new WinForms.FolderBrowserDialog()
            {
                SelectedPath = config.WebsiteRootPath
            };
            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                config.WebsiteRootPath = dialog.SelectedPath;
            }
        }

        private void FileBrowse_Click(object sender, EventArgs e)
        {
            var button = (Button)sender;
            var config = MainViewModel.SelectedItem.RequestConfig;
            var dialog = new OpenFileDialog()
            {
                Filter = "Powershell Scripts (*.ps1)| *.ps1;"
            };
            Action saveAction = null;
            string filename = "";
            if (button.Name == "Button_PreRequest")
            {
                filename = config.PreRequestPowerShellScript;
                saveAction = () => config.PreRequestPowerShellScript = dialog.FileName;
            }
            else if (button.Name == "Button_PostRequest")
            {
                filename = config.PostRequestPowerShellScript;
                saveAction = () => config.PostRequestPowerShellScript = dialog.FileName;
            }
            try
            {
                var fileInfo = new FileInfo(filename);
                if (fileInfo.Directory.Exists)
                {
                    dialog.InitialDirectory = fileInfo.Directory.FullName;
                    dialog.FileName = fileInfo.Name;
                }
            }
            catch (ArgumentException)
            {
                // invalid file passed in, open dialog with default options
            }
            if (dialog.ShowDialog() == true)
            {
                saveAction();
            }
        }

        private async void TestScript_Click(object sender, EventArgs e)
        {
            var button = (Button)sender;
            string scriptFile = null;
            var result = new CertificateRequestResult { ManagedItem = MainViewModel.SelectedItem, IsSuccess = true, Message = "Script Testing Message" };
            if (button.Name == "Button_TestPreRequest")
            {
                scriptFile = MainViewModel.SelectedItem.RequestConfig.PreRequestPowerShellScript;
                result.IsSuccess = false; // pre-request messages will always have IsSuccess = false
            }
            else if (button.Name == "Button_TestPostRequest")
            {
                scriptFile = MainViewModel.SelectedItem.RequestConfig.PostRequestPowerShellScript;
            }
            if (string.IsNullOrEmpty(scriptFile)) return; // don't try to run empty script
            try
            {
                string scriptOutput = await PowerShellManager.RunScript(result, scriptFile);
                MessageBox.Show(scriptOutput, "Powershell Output", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message, "Script Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void TestChallenge_Click(object sender, EventArgs e)
        {
            if (!MainViewModel.IsIISAvailable)
            {
                MessageBox.Show(SR.ManagedItemSettings_CannotChallengeWithoutIIS, SR.ChallengeError, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else if (MainViewModel.SelectedItem.RequestConfig.ChallengeType != null)
            {
                Button_TestChallenge.IsEnabled = false;
                TestInProgress.Visibility = Visibility.Visible;

                try
                {
                    MainViewModel.UpdateManagedSiteSettings();
                }
                catch (Exception exp)
                {
                    // usual failure is that primary domain is not set
                    MessageBox.Show(exp.Message);
                    return;
                }

                var result = await MainViewModel.TestChallengeResponse(MainViewModel.SelectedItem);
                if (result.IsOK)
                {
                    MessageBox.Show(SR.ManagedItemSettings_ConfigurationCheckOk, SR.Challenge, MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(string.Format(SR.ManagedItemSettings_ConfigurationCheckFailed, String.Join("\r\n", result.FailedItemSummary)), SR.ManagedItemSettings_ChallengeTestFailed, MessageBoxButton.OK, MessageBoxImage.Error);
                }

                Button_TestChallenge.IsEnabled = true;
                TestInProgress.Visibility = Visibility.Hidden;
            }
        }

        private async void TestWebhook_Click(object sender, EventArgs e)
        {
            try
            {
                Button_TestWebhook.IsEnabled = false;

                var config = MainViewModel.SelectedItem.RequestConfig;
                if (!Uri.TryCreate(config.WebhookUrl, UriKind.Absolute, out var result))
                {
                    MessageBox.Show($"The webhook url must be a valid url.", SR.ManagedItemSettings_WebhookTestFailed, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (string.IsNullOrEmpty(config.WebhookMethod))
                {
                    MessageBox.Show($"The webhook method must be selected.", SR.ManagedItemSettings_WebhookTestFailed, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                bool forSuccess = config.WebhookTrigger == Webhook.ON_SUCCESS;
                var (success, status) = await Webhook.SendRequest(config, forSuccess);
                string completed = success ? SR.succeed : SR.failed;
                MessageBox.Show(string.Format(SR.ManagedItemSettings_WebhookRequestResult, completed, status), SR.ManagedItemSettings_WebhookTest, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(SR.ManagedItemSettings_WebhookRequestError, ex.Message), SR.ManagedItemSettings_WebhookTestFailed, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                Button_TestWebhook.IsEnabled = true;
            }
        }

        private async void RevokeCertificateBtn_Click(object sender, RoutedEventArgs e)
        {
            // check cert exists, if not inform user
            var certPath = this.MainViewModel.SelectedItem.CertificatePath;
            if (String.IsNullOrEmpty(certPath) || !File.Exists(certPath))
            {
                MessageBox.Show(SR.ManagedItemSettings_CertificateNotReady, SR.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (MessageBox.Show(SR.ManagedItemSettings_ConfirmRevokeCertificate, SR.Alert, MessageBoxButton.OKCancel, MessageBoxImage.Exclamation) == MessageBoxResult.OK)
            {
                try
                {
                    RevokeCertificateBtn.IsEnabled = false;
                    var result = await MainViewModel.RevokeSelectedItem();
                    if (result.IsOK)
                    {
                        MessageBox.Show(SR.ManagedItemSettings_Certificate_Revoked, SR.Alert, MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(string.Format(SR.ManagedItemSettings_RevokeCertificateError, result.Message), SR.Error, MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (!String.IsNullOrEmpty(MainViewModel.SelectedItem.CertificatePath))
            {
                if (MessageBox.Show("Re-apply certificate to website bindings?", "Confirm Re-Apply?", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                {
                    await MainViewModel.ReapplyCertificateBindings(MainViewModel.SelectedItem.Id);
                }
            }
        }
    }
}