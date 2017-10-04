using Certify.Management;
using Certify.Models;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
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
        }

        private void MainViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            this.SettingsTab.SelectedIndex = 0;
        }

        private void Button_Save(object sender, RoutedEventArgs e)
        {
            if (this.MainViewModel.SelectedItemHasChanges)
            {
                if (MainViewModel.SelectedItem.Id == null && MainViewModel.SelectedWebSite == null)
                {
                    MessageBox.Show("Select the website to create a certificate for.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (String.IsNullOrEmpty(MainViewModel.SelectedItem.Name))
                {
                    MessageBox.Show("A name is required for this item.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (MainViewModel.PrimarySubjectDomain == null)
                {
                    MessageBox.Show("A Primary Domain must be included", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (MainViewModel.SelectedItem.RequestConfig.ChallengeType==ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_SNI &&
                    MainViewModel.IISVersion.Major < 8)
                {
                    MessageBox.Show($"The {ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_SNI} challenge is only available for IIS versions 8+.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (MainViewModel.SelectedItem.RequestConfig.PerformAutomatedCertBinding)
                {
                    MainViewModel.SelectedItem.RequestConfig.BindingIPAddress = null;
                    MainViewModel.SelectedItem.RequestConfig.BindingPort = null;
                    MainViewModel.SelectedItem.RequestConfig.BindingUseSNI = null;
                }
                //save changes

                //creating new managed item
                MainViewModel.SaveManagedItemChanges();
            }
            else
            {
                MessageBox.Show("No changes were made, skipping save");
            }
        }

        private void Button_DiscardChanges(object sender, RoutedEventArgs e)
        {
            //if new item, discard and select first item in managed sites
            if (MainViewModel.SelectedItem.Id == null)
            {
                ReturnToDefaultManagedItemView();
            }
            else
            {
                //reload settings for managed sites, discard changes
                var currentSiteId = MainViewModel.SelectedItem.Id;
                MainViewModel.LoadSettings();
                MainViewModel.SelectedItem = MainViewModel.ManagedSites.FirstOrDefault(m => m.Id == currentSiteId);
            }

            MainViewModel.MarkAllChangesCompleted();
        }

        private void ReturnToDefaultManagedItemView()
        {
            MainViewModel.SelectFirstOrDefaultItem();
        }

        private void Button_RequestCertificate(object sender, RoutedEventArgs e)
        {
            if (MainViewModel.SelectedItem != null)
            {
                if (MainViewModel.SelectedItem.IsChanged)
                {
                    //save changes
                    MainViewModel.SaveManagedItemChanges();
                }

                //begin request
                MainViewModel.MainUITabIndex = (int)MainWindow.PrimaryUITabs.CurrentProgress;

                if (MainViewModel.BeginCertificateRequestCommand.CanExecute((MainViewModel.SelectedItem.Id)))
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(
    () =>
    {
        MainViewModel.BeginCertificateRequestCommand.Execute(MainViewModel.SelectedItem.Id);
    }));
                }
            }
        }

        private void Button_Delete(object sender, RoutedEventArgs e)
        {
            if (this.MainViewModel.SelectedItem.Id == null)
            {
                //item not saved, discard
                ReturnToDefaultManagedItemView();
            }
            else
            {
                if (MessageBox.Show("Are you sure you want to delete this item? Deleting the item will not affect IIS settings etc.", "Confirm Delete", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
                {
                    this.MainViewModel.DeleteManagedSite(this.MainViewModel.SelectedItem);
                    ReturnToDefaultManagedItemView();
                }
            }
        }

        private void Website_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainViewModel.SelectedWebSite != null)
            {
                string siteId = MainViewModel.SelectedWebSite.SiteId;
                if (MainViewModel.PopulateManagedSiteSettingsCommand.CanExecute(siteId))
                {
                    MainViewModel.PopulateManagedSiteSettingsCommand.Execute(siteId);
                }
            }
        }

        private void SANDomain_Toggled(object sender, RoutedEventArgs e)
        {
            this.MainViewModel.SelectedItem.IsChanged = true;
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
                MessageBox.Show("The log file for this item has not been created yet.");
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
                    X509Certificate2UI.DisplayCertificate(cert);
                }
            }
            else
            {
                MessageBox.Show("The certificate file for this item has not been created yet.");
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
                MessageBox.Show("Cannot check challenges if IIS is not available.", "Challenge Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else if (MainViewModel.SelectedItem.RequestConfig.ChallengeType != null)
            {
                Button_TestChallenge.IsEnabled = false;
                MainViewModel.UpdateManagedSiteSettings();

                var result = await MainViewModel.TestChallengeResponse(MainViewModel.SelectedItem);
                if (result.IsOK)
                {
                    MessageBox.Show("Check Success", "Challenge", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Check Failed:\n{result.Message}", "Challenge Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                Button_TestChallenge.IsEnabled = true;
            }
        }
    }
}