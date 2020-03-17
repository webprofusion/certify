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

        public DeploymentTaskConfigViewModel(DeploymentTaskConfig item)
        {
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
                    _appViewModel.DeploymentTaskProviders;

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

                if (resetDefaults)
                {
                    SelectedItem.TaskName = "";
                    SelectedItem.Description = "";
                    SelectedItem.IsDeferred = false;
                }

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

            this.SelectedItem.ChallengeCredentialKey = SelectedCredentialItem?.StorageKey;

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

                UsesCredentials = true;

                // our provider parameters are stored in config a s key value apir, but edited as an intermediate providerparameter with full metadata

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

    }
}
