using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using Certify.Config;
using Certify.Models;
using Certify.Models.Config;

namespace Certify.UI.ViewModel
{
    public class DeploymentTaskConfigViewModel : BindableBase
    {

        private AppViewModel _appViewModel => AppViewModel.Current;

        public DeploymentTaskConfig SelectedItem { get; set; }

        public bool EditAsPostRequestTask { get; set; } = false;


        public StoredCredential SelectedCredentialItem
        {
            get
            {
                return _appViewModel.StoredCredentials.FirstOrDefault(c => c.StorageKey == SelectedItem?.ChallengeCredentialKey);

            }
            set
            {
                if (SelectedItem != null && value != null)
                {
                    SelectedItem.ChallengeCredentialKey = value.StorageKey;
                }
            }
        }


        public DeploymentProviderDefinition DeploymentProvider { get; set; }
        public ObservableCollection<ProviderParameter> EditableParameters { get; set; }

        public ManagedCertificate ParentManagedCertificate => _appViewModel.SelectedItem;

        public Dictionary<string, string> TargetTypes { get; set; } = new Dictionary<string, string>
        {
            { StandardAuthTypes.STANDARD_AUTH_LOCAL,"Local"},
            { StandardAuthTypes.STANDARD_AUTH_WINDOWS,"Windows (Network)"},
            { StandardAuthTypes.STANDARD_AUTH_SSH,"SSH (Remote)"}
        };

        public DeploymentTaskConfigViewModel(DeploymentTaskConfig item, bool editAsPostRequestTask)
        {
            EditAsPostRequestTask = editAsPostRequestTask;

            if (item == null)
            {
                item = new DeploymentTaskConfig
                {
                    ChallengeProvider = StandardAuthTypes.STANDARD_AUTH_LOCAL,
                    Parameters = new List<ProviderParameterSetting>()
                };
            }
            SelectedItem = item;


        }

        public bool UsesCredentials { get; set; }

        public ObservableCollection<DeploymentProviderDefinition> DeploymentProviders => 
            new ObservableCollection<DeploymentProviderDefinition>(
                    _appViewModel.DeploymentTaskProviders.Where(p =>
                    p.UsageType == DeploymentProviderUsage.Any
                    || (EditAsPostRequestTask == false && p.UsageType == DeploymentProviderUsage.PreRequest)
                    || (EditAsPostRequestTask == true && p.UsageType == DeploymentProviderUsage.PostRequest)
                    ));

        private ICollectionView _filteredCredentials;
        public ICollectionView FilteredCredentials
        {
            get
            {
                if (_filteredCredentials == null)
                {
                    var source = CollectionViewSource.GetDefaultView(_appViewModel.StoredCredentials);
                    source.Filter = c =>
                         (c as StoredCredential).ProviderType == SelectedItem?.ChallengeProvider;
                    _filteredCredentials = source;
                }

                return _filteredCredentials;
            }
        }

        internal async Task RefreshOptions(bool resetDefaults = false)
        {
            if (SelectedItem.TaskTypeId != null)
            {
                DeploymentProvider = _appViewModel.DeploymentTaskProviders.First(d => d.Id == SelectedItem.TaskTypeId);

                /*if (resetDefaults)
                {
                    SelectedItem.TaskName = "";
                    SelectedItem.Description = "";
                    SelectedItem.IsDeferred = false;
                }*/

                RefreshParameters();
                await RefreshCredentialOptions();

                if (string.IsNullOrEmpty(SelectedItem.TaskName))
                {
                    SelectedItem.TaskName = DeploymentProvider.DefaultTitle ?? DeploymentProvider.Title;
                }

                if (string.IsNullOrEmpty(SelectedItem.Description))
                {
                    SelectedItem.Description = DeploymentProvider.Description;
                }

                RaisePropertyChangedEvent(nameof(SelectedItem));
                RaisePropertyChangedEvent(nameof(EditableParameters));
                RaisePropertyChangedEvent(nameof(SelectedCredentialItem));
            }

        }

        private async Task RefreshCredentialOptions()
        {
            // filter list of matching credentials
            await _appViewModel.RefreshStoredCredentialsList();
            _filteredCredentials.Refresh();


            RaisePropertyChangedEvent(nameof(FilteredCredentials));


        }

        /// <summary>
        /// capture the edited values of parameters and store them in the item config
        /// </summary>
        internal void CaptureEditedParameters()
        {

            SelectedItem.ChallengeCredentialKey = SelectedCredentialItem?.StorageKey;

            if (EditableParameters != null)
            {
                SelectedItem.Parameters = new List<ProviderParameterSetting>();
                foreach (var p in EditableParameters)
                {
                    SelectedItem.Parameters.Add(new ProviderParameterSetting(p.Key, p.Value));
                }
            }
        }

        public string CLICommand
        {
            get
            {
                var cmd = "certify deploy \"" + _appViewModel.SelectedItem.Name + "\" \"" + SelectedItem?.TaskName + "\"";
                return cmd;
            }
        }

        private void RefreshParameters()
        {
            EditableParameters = new System.Collections.ObjectModel.ObservableCollection<ProviderParameter>();

            var definition = DeploymentProvider;

            if (definition != null)
            {
                if (definition.ProviderParameters.Any(p => p.IsCredential))
                {
                    UsesCredentials = true;
                }
                else
                {
                    UsesCredentials = false;
                }

                // our provider parameters are stored in config as a key value pair, but edited as an intermediate provider parameter with full metadata

                var providerParams = definition.ProviderParameters.Where(p => p.IsCredential == false).ToList();

                foreach (var pa in providerParams)
                {
                    if (SelectedItem?.Parameters.Exists(p => p.Key == pa.Key) == true)
                    {
                        pa.Value = SelectedItem.Parameters.FirstOrDefault(p => p.Key == pa.Key)?.Value;
                        EditableParameters.Add(pa);
                    }
                    else
                    {
                        EditableParameters.Add(pa);
                    }
                }

                var toRemove = SelectedItem.Parameters.ToList().Where(p => !providerParams.Any(pp => pp.Key == p.Key));

                foreach (var r in toRemove)
                {
                    SelectedItem.Parameters.Remove(r);
                }
            }
        }

        public async Task<ActionResult> Save()
        {
            CaptureEditedParameters();

            // validate task configuration using the selected provider

            if (SelectedItem.TaskTypeId == null)
            {
                return new ActionResult("Please select the required Task Type.", false);
            }

            if (string.IsNullOrEmpty(SelectedItem.TaskName))
            {
                if (!string.IsNullOrEmpty(DeploymentProvider?.DefaultTitle))
                {
                    // use default title and continue
                    SelectedItem.TaskName = DeploymentProvider.DefaultTitle;
                }
            }

            if (string.IsNullOrEmpty(SelectedItem.TaskName))
            {

                // check task name populated
                return new ActionResult("A unique Task Name is required, this may be used later to run the task manually.", false);

            }
            else
            {
                // check task name is unique for this managed cert
                if (
                    _appViewModel.SelectedItem.PostRequestTasks?.Any(t => t.Id != SelectedItem.Id && t.TaskName.ToLower().Trim() == SelectedItem.TaskName.ToLower().Trim()) == true
                    || _appViewModel.SelectedItem.PreRequestTasks?.Any(t => t.Id != SelectedItem.Id && t.TaskName.ToLower().Trim() == SelectedItem.TaskName.ToLower().Trim()) == true
                 )
                {
                    return new ActionResult("A unique Task Name is required, this task name is already in use for this managed certificate.", false);

                }
            }


            // if remote target, check target specified. TODO: Could also check host resolves.
            if (!string.IsNullOrEmpty(SelectedItem.ChallengeProvider)
                && SelectedItem.ChallengeProvider != StandardAuthTypes.STANDARD_AUTH_LOCAL
                && string.IsNullOrEmpty(SelectedItem.TargetHost)
                )
            {
                // check task name populated
                return new ActionResult("Target Host name or IP is required if deployment target is not Local.", false);
            }

            // validate task provider specific config
            var results = await _appViewModel.ValidateDeploymentTask(
                new Models.Utils.DeploymentTaskValidationInfo { ManagedCertificate = _appViewModel.SelectedItem, TaskConfig = SelectedItem }
                );

            if (results.Any(r => r.IsSuccess == false))
            {
                var firstFailure = results.FirstOrDefault(r => r.IsSuccess == false);
                return new ActionResult(firstFailure.Message, false);
            }

            if (EditAsPostRequestTask)
            {
                if (_appViewModel.SelectedItem.PostRequestTasks == null)
                {
                    _appViewModel.SelectedItem.PostRequestTasks = new System.Collections.ObjectModel.ObservableCollection<DeploymentTaskConfig>();
                }

                // add/update edited deployment task in selectedItem config
                if (SelectedItem.Id == null)
                {
                    //add new
                    SelectedItem.Id = Guid.NewGuid().ToString();
                    _appViewModel.SelectedItem.PostRequestTasks.Add(SelectedItem);
                }
                else
                {
                    var original = _appViewModel.SelectedItem.PostRequestTasks.First(f => f.Id == SelectedItem.Id);
                    _appViewModel.SelectedItem.PostRequestTasks[_appViewModel.SelectedItem.PostRequestTasks.IndexOf(original)] = SelectedItem;
                }
            }
            else
            {
                if (_appViewModel.SelectedItem.PreRequestTasks == null)
                {
                    _appViewModel.SelectedItem.PreRequestTasks = new System.Collections.ObjectModel.ObservableCollection<DeploymentTaskConfig>();
                }

                // add/update edited deployment task in selectedItem config
                if (SelectedItem.Id == null)
                {
                    //add new
                    SelectedItem.Id = Guid.NewGuid().ToString();
                    _appViewModel.SelectedItem.PreRequestTasks.Add(SelectedItem);
                }
                else
                {
                    var original = _appViewModel.SelectedItem.PreRequestTasks.First(f => f.Id == SelectedItem.Id);
                    _appViewModel.SelectedItem.PreRequestTasks[_appViewModel.SelectedItem.PreRequestTasks.IndexOf(original)] = SelectedItem;
                }
            }


            return new ActionResult("OK", true);
        }

    }
}
