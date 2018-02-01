using Certify.Locales;
using Certify.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace Certify.UI.Controls
{
    /// <summary>
    /// Interaction logic for ManagedItemSettings.xaml 
    /// </summary>
    public partial class ManagedItemSettingsValidation : UserControl
    {
        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return UI.ViewModel.AppModel.Current;
            }
        }

        public ManagedItemSettingsValidation()
        {
            InitializeComponent();

            ChallengeProviderList.ItemsSource = Models.Config.ChallengeProviders.Providers.Where(p => p.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS);
        }

        private void AddStoredCredential_Click(object sender, RoutedEventArgs e)
        {
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
    }
}