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
using PropertyChanged;

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

        public Dictionary<string, string> TargetTypes
        {
            get
            {


                Dictionary<string, string> list = new Dictionary<string, string>();
                if (this.DeploymentProvider != null)
                {
                    foreach (var t in DeploymentTaskTypes.TargetTypes)
                    {
                        if (t.Key == StandardAuthTypes.STANDARD_AUTH_LOCAL && (DeploymentProvider.SupportedContexts.HasFlag(DeploymentContextType.LocalAsService)))
                        {
                            list.Add(t.Key, t.Value);
                        }

                        if (t.Key == StandardAuthTypes.STANDARD_AUTH_LOCAL_AS_USER && (DeploymentProvider.SupportedContexts.HasFlag(DeploymentContextType.LocalAsUser)))
                        {
                            list.Add(t.Key, t.Value);
                        }

                        if (t.Key == StandardAuthTypes.STANDARD_AUTH_WINDOWS && (DeploymentProvider.SupportedContexts.HasFlag(DeploymentContextType.WindowsNetwork)))
                        {
                            list.Add(t.Key, t.Value);
                        }

                        if (t.Key == StandardAuthTypes.STANDARD_AUTH_SSH && (DeploymentProvider.SupportedContexts.HasFlag(DeploymentContextType.SSH)))
                        {
                            list.Add(t.Key, t.Value);
                        }
                    }
                }
                return list;
            }
        }

        public static Dictionary<TaskTriggerType, string> TriggerTypes => DeploymentTaskTypes.TriggerTypes;

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

        [DependsOn(nameof(DeploymentProvider))]
        public bool UsesCredentials
        {
            get
            {
                if (DeploymentProvider == null)
                {
                    return false;
                }
                else
                {
                    if (SelectedItem.ChallengeProvider != StandardAuthTypes.STANDARD_AUTH_LOCAL)
                    {
                        return DeploymentProvider.SupportedContexts.HasFlag(DeploymentContextType.LocalAsUser)
                         || DeploymentProvider.SupportedContexts.HasFlag(DeploymentContextType.SSH)
                         || DeploymentProvider.SupportedContexts.HasFlag(DeploymentContextType.WindowsNetwork);

                    }
                    else
                    {
                        return false;
                    }
                }
            }
        }


        public bool UsesRemoteOptions
        {
            get
            {
                if (DeploymentProvider == null)
                {
                    return false;
                }
                else
                {
                    if (SelectedItem.ChallengeProvider != StandardAuthTypes.STANDARD_AUTH_LOCAL && SelectedItem.ChallengeProvider != StandardAuthTypes.STANDARD_AUTH_LOCAL_AS_USER)
                    {
                        return DeploymentProvider.SupportedContexts.HasFlag(DeploymentContextType.SSH) || DeploymentProvider.SupportedContexts.HasFlag(DeploymentContextType.WindowsNetwork);
                    }
                    else
                    {
                        return false;
                    }
                }
            }
        }

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
                DeploymentProvider = _appViewModel.DeploymentTaskProviders.FirstOrDefault(d => d.Id == SelectedItem.TaskTypeId);

                if (resetDefaults)
                {
                    SelectedItem.TaskName = "";
                    SelectedItem.TaskTrigger = TaskTriggerType.ANY_STATUS;
                }

                RefreshParameters();
                await RefreshCredentialOptions();


                // pre-populate task title with a default
                if (string.IsNullOrEmpty(SelectedItem.TaskName))
                {
                    SelectedItem.TaskName = DeploymentProvider.DefaultTitle ?? DeploymentProvider.Title;
                }

                RaisePropertyChangedEvent(nameof(SelectedItem));
                RaisePropertyChangedEvent(nameof(EditableParameters));
                RaisePropertyChangedEvent(nameof(SelectedCredentialItem));
                RaisePropertyChangedEvent(nameof(UsesCredentials));
                RaisePropertyChangedEvent(nameof(UsesRemoteOptions));
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
                if (SelectedItem?.Id != null)
                {
                    var cmd = "certify deploy \"" + _appViewModel.SelectedItem.Id + "\" \"" + SelectedItem?.Id + "\"";
                    return cmd;
                }
                else
                {
                    return "[Save this task to generate the deployment command]";
                }
            }
        }

        private void RefreshParameters()
        {
            EditableParameters = new System.Collections.ObjectModel.ObservableCollection<ProviderParameter>();

            var definition = DeploymentProvider;

            if (definition != null)
            {
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
                && SelectedItem.ChallengeProvider != StandardAuthTypes.STANDARD_AUTH_LOCAL_AS_USER
                && string.IsNullOrEmpty(SelectedItem.TargetHost)
                )
            {
                // check task name populated
                return new ActionResult("Target Host name or IP is required if deployment target is not Local.", false);
            }

            // if target type requires a credential selection check that's been provided

            if (UsesCredentials && string.IsNullOrEmpty(SelectedItem.ChallengeCredentialKey))
            {
                return new ActionResult("The selected target type requires specific credentials.", false);
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
