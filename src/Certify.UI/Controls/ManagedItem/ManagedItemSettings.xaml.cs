using Certify.Locales;
using Certify.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Certify.UI.Controls.ManagedItem
{
    /// <summary>
    /// Interaction logic for ManagedItemSettings.xaml 
    /// </summary>
    public partial class Settings : UserControl
    {
        protected Certify.UI.ViewModel.AppModel AppViewModel => UI.ViewModel.AppModel.Current;

        protected Certify.UI.ViewModel.ManagedItemModel ItemViewModel => UI.ViewModel.ManagedItemModel.Current;

        public Settings()
        {
            InitializeComponent();
            this.AppViewModel.PropertyChanged += MainViewModel_PropertyChanged;

            ToggleAdvancedView();
        }

        private void MainViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SelectedItem")
            {
                this.SettingsTab.SelectedIndex = 0;

                if (ItemViewModel.SelectedItem?.Health == ManagedItemHealth.Error
                    ||
                    ItemViewModel.SelectedItem?.Health == ManagedItemHealth.Warning
                    )
                {
                    this.TabStatusInfo.Visibility = Visibility.Visible;
                    this.SettingsTab.SelectedItem = this.TabStatusInfo;
                }
                else
                {
                    this.TabStatusInfo.Visibility = Visibility.Collapsed;
                    this.SettingsTab.SelectedItem = this.TabDomains;
                }

                AppViewModel.IsChanged = false;
            }
        }

        private async Task<bool> ValidateAndSave(ManagedSite item)
        {
            /*if (item.Id == null && MainViewModel.SelectedWebSite == null)
            {
                MessageBox.Show(SR.ManagedItemSettings_SelectWebsiteOrCert, SR.SaveError, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }*/

            if (item.Id == null && item.RequestConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI)
            {
                MessageBox.Show("Sorry, the tls-sni-01 challenge type is not longer supported by Let's Encrypt for new certificates.", SR.SaveError, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (String.IsNullOrEmpty(item.Name))
            {
                MessageBox.Show(SR.ManagedItemSettings_NameRequired, SR.SaveError, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // check primary domain is also checked
            if (ItemViewModel.PrimarySubjectDomain != null && ItemViewModel.SelectedItem.DomainOptions.Any())
            {
                var primaryDomain = ItemViewModel.SelectedItem.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain);
                if (primaryDomain != null && !primaryDomain.IsSelected)
                {
                    primaryDomain.IsSelected = true;
                }
            }

            if (ItemViewModel.PrimarySubjectDomain == null)
            {
                // if we still can't decide on the primary domain ask user to define it
                MessageBox.Show(SR.ManagedItemSettings_NeedPrimaryDomain, SR.SaveError, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // if title set to the default, use the primary domain
            if (item.Name == SR.ManagedItemSettings_DefaultTitle)
            {
                item.Name = ItemViewModel.PrimarySubjectDomain.Domain;
            }

            if (item.RequestConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI &&
                AppViewModel.IISVersion.Major < 8)
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

                // if user has chosen to bind SNI with a specific IP, warn and confirm save
                if (item.RequestConfig.BindingUseSNI == true && !String.IsNullOrEmpty(item.RequestConfig.BindingIPAddress) && item.RequestConfig.BindingIPAddress != "*")
                {
                    if (MessageBox.Show(SR.ManagedItemSettings_InvalidSNI, SR.SaveError, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    {
                        // opted not to save
                        return false;
                    };
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

            // if DNS etc provider in use, store the selected provider
            ///TODO
            /*if (ChallengeProviderList.SelectedItem != null)
            {
                item.RequestConfig.ChallengeProvider = ((Models.Config.ChallengeProvider)ChallengeProviderList.SelectedItem).Id;
            }

            //if stored credential required, store selection
            if (StoredCredentialList.SelectedItem != null)
            {
                item.RequestConfig.ChallengeCredentialKey = ((Models.Config.StoredCredential)StoredCredentialList.SelectedItem).StorageKey;
            }*/

            //save changes

            //creating new managed item
            return await ItemViewModel.SaveManagedItemChanges();
        }

        private async void Button_Save(object sender, RoutedEventArgs e)
        {
            if (ItemViewModel.SelectedItem.IsChanged)
            {
                var item = ItemViewModel.SelectedItem;
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
            if (ItemViewModel.SelectedItem.Id == null)
            {
                ReturnToDefaultManagedItemView();
            }
            else
            {
                //reload settings for managed sites, discard changes
                await ItemViewModel.DiscardChanges();

                ReturnToDefaultManagedItemView();
            }
        }

        private void ReturnToDefaultManagedItemView()
        {
            ItemViewModel.SelectedItem = null;
        }

        private async void Button_RequestCertificate(object sender, RoutedEventArgs e)
        {
            if (ItemViewModel.SelectedItem != null)
            {
                if (ItemViewModel.SelectedItem.IsChanged)
                {
                    var savedOK = await ValidateAndSave(ItemViewModel.SelectedItem);
                    if (!savedOK) return;
                }

                //begin request
                AppViewModel.MainUITabIndex = (int)MainWindow.PrimaryUITabs.CurrentProgress;

                await AppViewModel.BeginCertificateRequest(ItemViewModel.SelectedItem.Id);
            }
        }

        private async void Button_Delete(object sender, RoutedEventArgs e)
        {
            await AppViewModel.DeleteManagedSite(ItemViewModel.SelectedItem);
            if (ItemViewModel.SelectedItem?.Id == null)
            {
                AppViewModel.SelectedItem = AppViewModel.ManagedSites.FirstOrDefault();
            }
        }

        private async void TestChallenge_Click(object sender, EventArgs e)
        {
            ItemViewModel.IsTestInProgress = true;

            if (!AppViewModel.IsIISAvailable)
            {
                MessageBox.Show(SR.ManagedItemSettings_CannotChallengeWithoutIIS, SR.ChallengeError, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else if (ItemViewModel.SelectedItem.RequestConfig.ChallengeType != null)
            {
                Button_TestChallenge.IsEnabled = false;

                try
                {
                    ItemViewModel.UpdateManagedSiteSettings();
                }
                catch (Exception exp)
                {
                    // usual failure is that primary domain is not set
                    Button_TestChallenge.IsEnabled = true;
                    ItemViewModel.IsTestInProgress = false;

                    MessageBox.Show(exp.Message);
                    return;
                }

                var result = await ItemViewModel.TestChallengeResponse(ItemViewModel.SelectedItem);
                if (result.IsOK)
                {
                    MessageBox.Show(SR.ManagedItemSettings_ConfigurationCheckOk, SR.Challenge, MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(string.Format(SR.ManagedItemSettings_ConfigurationCheckFailed, String.Join("\r\n", result.FailedItemSummary)), SR.ManagedItemSettings_ChallengeTestFailed, MessageBoxButton.OK, MessageBoxImage.Error);
                }

                //TODO: just use viewmodel to determine if test button should be enabled
                Button_TestChallenge.IsEnabled = true;
                ItemViewModel.IsTestInProgress = false;
            }
        }

        private void Dismiss_Click(object sender, RoutedEventArgs e)
        {
            AppViewModel.SelectedItem = null;
        }

        private void CheckAdvancedView_Checked(object sender, RoutedEventArgs e)
        {
            ToggleAdvancedView();
        }

        private void ToggleAdvancedView()
        {
            if (CheckAdvancedView.IsChecked == false)
            {
                this.TabDeployment.Visibility = Visibility.Collapsed;
                this.TabScripting.Visibility = Visibility.Collapsed;
                this.TabOptions.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.TabDeployment.Visibility = Visibility.Visible;
                this.TabScripting.Visibility = Visibility.Visible;
                this.TabOptions.Visibility = Visibility.Visible;
            }
        }
    }
}