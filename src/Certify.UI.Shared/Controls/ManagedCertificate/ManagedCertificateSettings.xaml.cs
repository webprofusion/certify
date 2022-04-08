using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Certify.Locales;
using Certify.Models;
using Certify.UI.Shared;
using MahApps.Metro.Controls;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for ManagedCertificateSettings.xaml 
    /// </summary>
    public partial class ManagedCertificateSettings : UserControl
    {
        protected Certify.UI.ViewModel.AppViewModel AppViewModel => UI.ViewModel.AppViewModel.Current;

        protected Certify.UI.ViewModel.ManagedCertificateViewModel ItemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;

        protected Models.Providers.ILog Log => AppViewModel.Log;

        private string _lastSelectedItemId;

        public ManagedCertificateSettings()
        {
            InitializeComponent();

            AppViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
            AppViewModel.PropertyChanged += MainViewModel_PropertyChanged;
        }

        private void MainViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SelectedItem")
            {
                // show status tab for existing managed certs
                var showStatus = ItemViewModel.SelectedItem?.Id != null && ItemViewModel.SelectedItem.DateLastRenewalAttempt != null;

                if (showStatus)
                {
                    TabStatusInfo.Visibility = Visibility.Visible;
                }
                else
                {
                    TabStatusInfo.Visibility = Visibility.Collapsed;
                }

                if (_lastSelectedItemId != ItemViewModel.SelectedItem?.Id)
                {
                    // switch tab to default if the selected item has changed

                    _lastSelectedItemId = ItemViewModel.SelectedItem?.Id;

                    if (showStatus)
                    {
                        SettingsTab.SelectedItem = TabStatusInfo;
                    }
                    else
                    {
                        SettingsTab.SelectedItem = TabDomains;
                    }
                }

                ItemViewModel.RaiseSelectedItemChanges();

                if (!ItemViewModel.IsEditable)
                {
                    this.TabDeployment.Visibility = Visibility.Collapsed;
                    this.TabDomains.Visibility = Visibility.Collapsed;
                    this.TabAuthorization.Visibility = Visibility.Collapsed;
                    this.TabTasks.Visibility = Visibility.Collapsed;
                    this.TabPreview.Visibility = Visibility.Collapsed;

                }
                else
                {
                    this.TabDeployment.Visibility = Visibility.Visible;
                    this.TabDomains.Visibility = Visibility.Visible;
                    this.TabAuthorization.Visibility = Visibility.Visible;
                    this.TabTasks.Visibility = Visibility.Visible;
                    this.TabPreview.Visibility = Visibility.Visible;
                }

                if (ItemViewModel.SelectedItem?.Id == null)
                {
                    // show name in edit mode when starting a new item
                    ItemViewModel.IsNameEditMode = true;
                    EditName.Focus();
                }

                AppViewModel.IsChanged = false;

            }
        }

        private void ShowValidationError(string msg)
        {
            AppViewModel.ShowNotification(msg, NotificationType.Error, true);
        }

        private async Task<bool> ValidateAndSave()
        {

            var validationResult = ItemViewModel.Validate(applyAutoConfiguration: true);

            if (!validationResult.IsValid)
            {
                ShowValidationError(validationResult.Message);
                return false;
            }
            else
            {
                // if user has chosen to bind SNI with a specific IP, warn and confirm save
                if (ItemViewModel.SelectedItem.RequestConfig.BindingUseSNI == true && !string.IsNullOrEmpty(ItemViewModel.SelectedItem.RequestConfig.BindingIPAddress) && ItemViewModel.SelectedItem.RequestConfig.BindingIPAddress != "*")
                {
                    if (MessageBox.Show(SR.ManagedCertificateSettings_InvalidSNI, SR.SaveError, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    {
                        // opted not to save
                        return false;
                    };
                }

                //creating new managed item
                return await ItemViewModel.SaveManagedCertificateChanges();
            }
        }

        private async void Button_Save(object sender, RoutedEventArgs e)
        {
            if (ItemViewModel.SelectedItem.IsChanged)
            {
                await ValidateAndSave();
            }
            else
            {
                MessageBox.Show(SR.ManagedCertificateSettings_NoChanges);
            }
        }

        private async void Button_DiscardChanges(object sender, RoutedEventArgs e)
        {
            //if new item, discard and select first item in managed sites
            if (ItemViewModel.SelectedItem.Id == null)
            {
                ReturnToDefaultManagedCertificateView();
            }
            else
            {
                //reload settings for managed sites, discard changes
                await ItemViewModel.DiscardChanges();

                ReturnToDefaultManagedCertificateView();
            }
        }

        private void ReturnToDefaultManagedCertificateView() => ItemViewModel.SelectedItem = null;

        private async void Button_RequestCertificate(object sender, RoutedEventArgs e)
        {
            if (ItemViewModel.SelectedItem != null)
            {

                var savedOK = await ValidateAndSave();
                if (!savedOK)
                {
                    return;
                }

                //begin request
                var renewalCheckWindow = ItemViewModel.SelectedItem.DateRenewed?.AddDays(2);
                if (ItemViewModel.SelectedItem.LastRenewalStatus == RequestState.Success && renewalCheckWindow > DateTime.Now)
                {
                    // cert was recently renewed. confirm user intent
                    var msg = "This managed certificate was recently renewed. Are you sure you wish to request it again now? \r\n\r\nThe Certificate Authority may impose rate limits on the number of duplicate certificates which can be issued, so requesting duplicate certificates should be avoided. ";
                    if (MessageBox.Show(msg, "Request certificate again?", MessageBoxButton.OKCancel) == MessageBoxResult.Cancel)
                    {
                        return;
                    }
                }

                var result = await AppViewModel.BeginCertificateRequest(ItemViewModel.SelectedItem.Id);
                if (result != null)
                {
                    if (result.IsSuccess == false && result.Result is Exception)
                    {
                        var msg = ((Exception)result.Result)?.ToString();
                        Log?.Error($"RequestCertificate: {msg}");
                    }
                }
            }
        }

        private async void Button_Delete(object sender, RoutedEventArgs e)
        {
            await AppViewModel.DeleteManagedCertificate(ItemViewModel.SelectedItem);
            if (ItemViewModel.SelectedItem?.Id == null)
            {
                AppViewModel.SelectedItem = AppViewModel.ManagedCertificates.FirstOrDefault();
            }
        }

        private void ShowTestResultsUI()
        {
            var parentWindow = Window.GetWindow(this);
            var obj = parentWindow.FindName("MainFlyout");
            var flyout = (Flyout)obj;
            flyout.Header = "Test Progress";
            flyout.Content = new TestProgress();
            flyout.IsOpen = !flyout.IsOpen;
        }

        private async void TestChallenge_Click(object sender, EventArgs e)
        {
            if (ItemViewModel.IsTestInProgress)
            {
                ShowTestResultsUI();
                return;
            }

            // validate and save before test
            if (!await ValidateAndSave())
            {
                return;
            }

            var challengeConfig = ItemViewModel.SelectedItem.GetChallengeConfig(null);

            if (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP && !string.IsNullOrEmpty(ItemViewModel.SelectedItem.ServerSiteId) && !AppViewModel.IsIISAvailable)
            {
                MessageBox.Show(SR.ManagedCertificateSettings_CannotChallengeWithoutIIS, SR.ChallengeError, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else if (challengeConfig.ChallengeType != null)
            {
                ItemViewModel.IsTestInProgress = true;
                Button_TestChallenge.IsEnabled = false;

                ItemViewModel.ConfigCheckResults = new System.Collections.ObjectModel.ObservableCollection<StatusMessage> {
                    new StatusMessage{IsOK=true, Message="Testing in progress.."}
                };

                AppViewModel.ClearRequestProgressResults();

                ShowTestResultsUI();

                // begin listening for progress info
                AppViewModel.TrackProgress(ItemViewModel.SelectedItem);

                var results = await ItemViewModel.TestChallengeResponse(ItemViewModel.SelectedItem);
                ItemViewModel.ConfigCheckResults =
                    new System.Collections.ObjectModel.ObservableCollection<StatusMessage>(results);

                ItemViewModel.RaisePropertyChangedEvent(nameof(ItemViewModel.ConfigCheckResults));

                //TODO: just use viewmodel to determine if test button should be enabled
                Button_TestChallenge.IsEnabled = true;
                ItemViewModel.IsTestInProgress = false;
            }
        }

        private void Dismiss_Click(object sender, RoutedEventArgs e) => AppViewModel.SelectedItem = null;

        private void EditName_Click(object sender, RoutedEventArgs e)
        {
            ItemViewModel.IsNameEditMode = true;
            EditName.Focus();
        }
        private void FinishedEditName_Click(object sender, RoutedEventArgs e)
        {
            ItemViewModel.IsNameEditMode = false;
        }

        private void DisplayName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Return)
            {
                ItemViewModel.IsNameEditMode = false;
            }
        }
    }
}
