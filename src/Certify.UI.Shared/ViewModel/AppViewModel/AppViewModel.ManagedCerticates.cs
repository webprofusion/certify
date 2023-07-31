using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Certify.Locales;
using Certify.Models;
using Certify.UI.Shared;
using PropertyChanged;

namespace Certify.UI.ViewModel
{
    public partial class AppViewModel : BindableBase
    {

        /// <summary>
        /// If set, there are one or more vault items available to be imported as managed sites 
        /// </summary>
        public ObservableCollection<ManagedCertificate> ImportedManagedCertificates { get; set; }

        /// <summary>
        /// If true, import from vault/iis scan will merge multi domain sites into one managed site 
        /// </summary>
        public bool IsImportSANMergeMode { get; set; }

        /// <summary>
        /// If true, one or more items uses a deprecated or unsupported challenge type
        /// </summary>
        public bool HasDeprecatedChallengeTypes { get; set; }

        private ManagedCertificate _selectedItem;

        /// <summary>
        /// Model for currently selected managed certificate details
        /// </summary>
        public ManagedCertificate SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (value?.Id != null && !ManagedCertificates.Contains(value))
                {
                    value = ManagedCertificates.FirstOrDefault(s => s.Id == value.Id);
                }

                _selectedItem = value;
            }
        }

        private object _managedCertificatesLock = new object();
        private ObservableCollection<ManagedCertificate> _managedCertificates;

        /// <summary>
        /// Cached list of all the sites we currently manage 
        /// </summary>
        public ObservableCollection<ManagedCertificate> ManagedCertificates
        {
            get
            {
                lock (_managedCertificatesLock)
                {
                    return _managedCertificates;
                }
            }

            set
            {
                _managedCertificates = value;

                System.Windows.Data.BindingOperations.EnableCollectionSynchronization(_managedCertificates, _managedCertificatesLock);

                if (SelectedItem != null)
                {
                    SelectedItem = SelectedItem;
                    RaisePropertyChangedEvent(nameof(SelectedItem));
                }
            }
        }

        /// <summary>
        /// Cached count of the number of managed certificate (not counting external certificate managers)
        /// </summary>
        [DependsOn(nameof(ManagedCertificates))]
        public int NumManagedCerts
        {
            get
            {
                return ManagedCertificates?.Where(c => string.IsNullOrEmpty(c.SourceId)).Count() ?? 0;
            }
        }

        /// <summary>
        /// Refresh the cached list of managed certs via the connected service
        /// </summary>
        /// <returns></returns>
        public virtual async Task RefreshManagedCertificates()
        {
            var filter = new ManagedCertificateFilter();

            // include external managed certs if enabled
            filter.IncludeExternal = IsFeatureEnabled(FeatureFlags.EXTERNAL_CERT_MANAGERS) && Preferences.EnableExternalCertManagers;

            var list = await _certifyClient.GetManagedCertificates(filter);

            foreach (var i in list)
            {
                i.IsChanged = false;

                if (!HasDeprecatedChallengeTypes && i.RequestConfig.Challenges.Any(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI))
                {
                    HasDeprecatedChallengeTypes = true;
                }
            }

            ManagedCertificates = new ObservableCollection<ManagedCertificate>(list);
        }

        /// <summary>
        /// Add/Update a managed certificate via service
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public async Task<bool> AddOrUpdateManagedCertificate(ManagedCertificate item)
        {

            // get existing

            var existing = await _certifyClient.GetManagedCertificate(item.Id);
            if (existing != null && existing.CertificateAuthorityId != item.CertificateAuthorityId)
            {
                // invalidate current order uri if CA has changed
                item.CurrentOrderUri = null;
            }

            var updatedManagedCertificate = await _certifyClient.UpdateManagedCertificate(item);
            updatedManagedCertificate.IsChanged = false;

            // add/update site in our local cache
            await UpdatedCachedManagedCertificate(updatedManagedCertificate);

            RaisePropertyChangedEvent(nameof(ManagedCertificates));
            return true;
        }

        /// <summary>
        /// Delete given managed cert via service
        /// </summary>
        /// <param name="selectedItem"></param>
        /// <returns></returns>
        public async Task<bool> DeleteManagedCertificate(ManagedCertificate selectedItem)
        {
            var existing = ManagedCertificates.FirstOrDefault(s => s.Id == selectedItem.Id);
            if (existing != null)
            {
                if (existing.ItemType == ManagedCertificateType.SSL_ExternallyManaged)
                {
                    MessageBox.Show("This item is externally managed and cannot be deleted by this app.");

                    return false;
                }

                if (MessageBox.Show(SR.ManagedCertificateSettings_ConfirmDelete, SR.ConfirmDelete, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
                {
                    existing.Deleted = true;
                    var deletedOK = await _certifyClient.DeleteManagedCertificate(selectedItem.Id);
                    if (deletedOK)
                    {
                        await _managedCertCacheSemaphore.WaitAsync();

                        try
                        {
                            ManagedCertificates.Remove(existing);
                            RaisePropertyChangedEvent(nameof(ManagedCertificates));
                        }
                        finally
                        {
                            _managedCertCacheSemaphore.Release();
                        }
                    }

                    return deletedOK;
                }
            }

            return false;
        }

        /// <summary>
        /// Begin Certificate order process for the given managed certificate
        /// </summary>
        /// <param name="managedItemId"></param>
        /// <param name="resumePaused"></param>
        /// <returns></returns>
        public async Task<CertificateRequestResult> BeginCertificateRequest(string managedItemId, bool resumePaused = true)
        {
            //begin request process
            var managedCertificate = ManagedCertificates.FirstOrDefault(s => s.Id == managedItemId);

            if (managedCertificate != null)
            {
                MainUITabIndex = (int)PrimaryUITabs.CurrentProgress;

                ClearRequestProgressResults();

                TrackProgress(managedCertificate);

                // start request (interactive)
                return await _certifyClient.BeginCertificateRequest(managedCertificate.Id, resumePaused, true);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// If true, one or more requests are currently in progress
        /// </summary>
        [DependsOn(nameof(ProgressResults))]
        public bool HasRequestsInProgress => (ProgressResults != null && ProgressResults.Any());

        /// <summary>
        /// Cached list of current request operations in progress
        /// </summary>
        public ObservableCollection<RequestProgressState> ProgressResults { get; set; }

        /// <summary>
        /// Begin tracking progress info for a given managed certificate
        /// </summary>
        /// <param name="managedCertificate"></param>
        public void TrackProgress(ManagedCertificate managedCertificate)
        {
            //add request to observable list of progress state
            var progressState = new RequestProgressState(RequestState.Running, "Starting..", managedCertificate);

            //begin monitoring progress
            UpdateRequestTrackingProgress(progressState);
        }

        /// <summary>
        /// Action performed when a managed certificate update is received from the service
        /// </summary>
        /// <param name="obj"></param>
        private async void CertifyClient_OnManagedCertificateUpdated(ManagedCertificate obj) => await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            // a managed site has been updated, update it in our view
            await UpdatedCachedManagedCertificate(obj);
        });

        private static SemaphoreSlim _managedCertCacheSemaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Update the memory cached version of a given managed certificate, replacing the current SelectedItem if it matches
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <param name="reload"></param>
        /// <returns></returns>
        public async Task<ManagedCertificate> UpdatedCachedManagedCertificate(ManagedCertificate managedCertificate, bool reload = false)
        {

            await _managedCertCacheSemaphore.WaitAsync();

            try
            {
                var existing = ManagedCertificates.FirstOrDefault(i => i.Id == managedCertificate.Id);
                var newItem = managedCertificate;

                // optional reload managed site details (for refresh)
                if (reload)
                {
                    newItem = await _certifyClient.GetManagedCertificate(managedCertificate.Id);
                }

                if (newItem != null)
                {
                    newItem.IsChanged = false;

                    // update our cached copy of the managed site details
                    if (existing != null)
                    {
                        var index = ManagedCertificates.IndexOf(existing);
                        if (index > -1)
                        {
                            ManagedCertificates[index] = newItem;
                        }
                        else
                        {
                            ManagedCertificates.Add(newItem);
                        }
                    }
                    else
                    {
                        ManagedCertificates.Add(newItem);
                    }
                }

                // refresh SelectedItem value if it matches our updated item
                if (SelectedItem != null && newItem != null && SelectedItem.Id == newItem.Id)
                {
                    SelectedItem = newItem;
                }

                RaisePropertyChangedEvent(nameof(ManagedCertificates));

                return newItem;
            }
            finally
            {
                _managedCertCacheSemaphore.Release();
            }
        }

        /// <summary>
        /// Perform batch renewal of all matching certificates for given renewal settings
        /// </summary>
        /// <param name="settings"></param>
        public async void RenewAll(RenewalSettings settings)
        {
            if (_certifyClient == null)
            {
                return;
            }

            try
            {
                ClearRequestProgressResults();

                settings.AwaitResults = false;

                _ = await _certifyClient.BeginAutoRenewal(settings);
            }
            catch (TaskCanceledException exp)
            {
                // very long running renewal may timeout on task await
                Log?.Warning("Auto Renewal UI task cancelled (timeout) " + exp.ToString());
            }
        }

        /// <summary>
        /// Process progress state message from service
        /// </summary>
        /// <param name="state"></param>
        private void UpdateRequestTrackingProgress(RequestProgressState state)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(delegate
            {
                var existing = ProgressResults.FirstOrDefault(p => p.ManagedCertificate.Id == state.ManagedCertificate.Id);

                if (existing != null)
                {
                    //replace state of progress request
                    var index = ProgressResults.IndexOf(existing);
                    ProgressResults[index] = state;
                }
                else
                {
                    ProgressResults.Add(state);
                }

                RaisePropertyChangedEvent(nameof(HasRequestsInProgress));
                RaisePropertyChangedEvent(nameof(ProgressResults));
            });
        }

        /// <summary>
        ///  Clear previous progress results
        /// </summary>
        public void ClearRequestProgressResults()
        {
            ProgressResults = new ObservableCollection<RequestProgressState>();
            RaisePropertyChangedEvent(nameof(HasRequestsInProgress));
            RaisePropertyChangedEvent(nameof(ProgressResults));
        }

        /// <summary>
        /// For a given managed certificate get preview of the actions to be performed on next renewal
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public async Task<List<ActionStep>> GetPreviewActions(ManagedCertificate item) => await _certifyClient.PreviewActions(item);
        /// <summary>
        /// Perform re-download last certificate for a given managed certificate
        /// </summary>
        /// <param name="managedItemId"></param>
        /// <returns></returns>
        internal async Task<CertificateRequestResult> RefetchCertificate(string managedItemId)
        {
            return await _certifyClient.RefetchCertificate(managedItemId);
        }

        /// <summary>
        /// Perform set of challenge response tests for the given managed certificate
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <returns></returns>
        internal async Task<List<StatusMessage>> TestChallengeConfiguration(ManagedCertificate managedCertificate)
        {
            try
            {
                return await _certifyClient.TestChallengeConfiguration(managedCertificate);
            }
            catch (TaskCanceledException)
            {
                return new List<StatusMessage> { new StatusMessage { IsOK = false, Message = "The test took too long to complete and has timed out. Please check and try again." } };
            }
        }

        /// <summary>
        /// Perform revoke for given managed certificate
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        internal async Task<StatusMessage> RevokeManageSiteCertificate(string id)
        {
            var result = await _certifyClient.RevokeManageSiteCertificate(id);

            if (result.IsOK)
            {
                // refresh managed cert in UI
                var updatedManagedCertificate = await _certifyClient.GetManagedCertificate(id);
                updatedManagedCertificate.IsChanged = false;

                // add/update site in our local cache
                await UpdatedCachedManagedCertificate(updatedManagedCertificate);
            }

            return result;
        }

        /// <summary>
        /// Re-deploy all managed certificates to any applicable bindings (re-store etc as applicable), optionally including Tasks
        /// </summary>

        /// <param name="isPreviewOnly"></param>
        /// <returns></returns>
        internal async Task<List<CertificateRequestResult>> RedeployManagedCertificatess(bool isPreviewOnly, bool includeDeploymentTasks)
        {
            return await _certifyClient.RedeployManagedCertificates(isPreviewOnly, includeDeploymentTasks);
        }

        /// <summary>
        /// Re-apply the current certificate to any applicable bindings (re-store etc as applicable)
        /// </summary>
        /// <param name="managedItemId"></param>
        /// <param name="isPreviewOnly"></param>
        /// <returns></returns>
        internal async Task<CertificateRequestResult> ReapplyCertificateBindings(string managedItemId, bool isPreviewOnly, bool includeDeploymentTasks)
        {
            return await _certifyClient.ReapplyCertificateBindings(managedItemId, isPreviewOnly, includeDeploymentTasks);
        }

        /// <summary>
        /// Perform a single deployment task for the given managed certificate
        /// </summary>
        /// <param name="managedCertificateId"></param>
        /// <param name="taskId"></param>
        /// <param name="isPreviewOnly"></param>
        /// <param name="forceTaskExecute"></param>
        /// <returns></returns>
        public async Task<List<ActionStep>> PerformDeployment(string managedCertificateId, string taskId, bool isPreviewOnly, bool forceTaskExecute) => await _certifyClient.PerformDeployment(managedCertificateId, taskId, isPreviewOnly, forceTaskExecute);

        /// <summary>
        /// Get log file per-line for given managed certificate
        /// </summary>
        /// <param name="id"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        public async Task<string[]> GetItemLog(string id, int limit)
        {
            var result = await _certifyClient.GetItemLog(id, limit);
            return result;
        }

        /// <summary>
        /// UI command for Renew All button
        /// </summary>
        public ICommand RenewAllCommand => new RelayCommand<RenewalSettings>(RenewAll);
    }
}
