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

        public DeploymentTaskConfig SelectedItem
        {
            get; set;
        }

        public DeploymentProviderDefinition DeploymentProvider { get; set; }
        public ObservableCollection<ProviderParameter> EditableParameters { get; set; }

        public ManagedCertificate ParentManagedCertificate => _appViewModel.SelectedItem;

        public Dictionary<string, string> TargetTypes { get; set; } = new Dictionary<string, string>
        {
            { "Certify.StandardChallenges.Local","Local"},
            { "Certify.StandardChallenges.Windows","Windows (Network)"},
            { "Certify.StandardChallenges.SSH","SSH (Remote)"}
        };

        public DeploymentTaskConfigViewModel(DeploymentTaskConfig item)
        {
            if (item == null)
            {
                item = new DeploymentTaskConfig
                {
                    Description = "A description for this task",
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

        internal async Task RefreshOptions()
        {
            DeploymentProvider = _appViewModel.DeploymentTaskProviders.First(d => d.Id == SelectedItem.TaskTypeId);

            RefreshParameters();
            await RefreshCredentialOptions();

            RaisePropertyChangedEvent(nameof(EditableParameters));
            
        }

        private async Task RefreshCredentialOptions()
        {
            // filter list of matching credentials
        //    await _appViewModel.RefreshStoredCredentialsList();
        
//RaisePropertyChangedEvent(nameof(FilteredCredentials));
        }

        /// <summary>
        /// capture the edited values of parameters and store them in the item config
        /// </summary>
        internal void CaptureEditedParameters()
        {
           if (EditableParameters!=null)
            {
                SelectedItem.Parameters = new List<ProviderParameterSetting>();
                foreach(var p in EditableParameters)
                {
                    SelectedItem.Parameters.Add(new ProviderParameterSetting(p.Key, p.Value));
                }
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
                    if (SelectedItem?.Parameters.Exists(p=>p.Key==pa.Key)==true)
                    {
                        pa.Value = SelectedItem.Parameters.FirstOrDefault(p=>p.Key==pa.Key)?.Value;
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
