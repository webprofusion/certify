using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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
                    Parameters = new Dictionary<string, string>()
                };
            }
            SelectedItem = item;
        }

        public bool UsesCredentials { get; set; }

        public ObservableCollection<DeploymentProviderDefinition> DeploymentProviders =>
                    _appViewModel.DeploymentTaskProviders;

        public ObservableCollection<StoredCredential> FilteredCredentials
        {
            get
            {

                if (SelectedItem != null)
                {
                    return new ObservableCollection<StoredCredential>(
                  _appViewModel.StoredCredentials.Where(s => s.ProviderType == SelectedItem.ChallengeProvider)
                  );
                }
                else
                {
                    return _appViewModel.StoredCredentials;
                }
            }
        }

        internal async Task RefreshOptions()
        {
            DeploymentProvider = _appViewModel.DeploymentTaskProviders.First(d => d.Id == SelectedItem.TaskTypeId);

            RefreshParameters();
            await RefreshCredentialOptions();

            RaisePropertyChangedEvent(nameof(EditableParameters));
            RaisePropertyChangedEvent(nameof(FilteredCredentials));
        }

        private async Task RefreshCredentialOptions()
        {
            // filter list of matching credentials
            await _appViewModel.RefreshStoredCredentialsList();
            var credentials = _appViewModel.StoredCredentials.Where(s => s.ProviderType == SelectedItem.ChallengeProvider);
            var currentSelectedValue = SelectedItem.ChallengeCredentialKey;

            // updating item source also clears selected value, so this workaround sets it back
            // this is only an issue when you have two or more credentials for one provider
            //StoredCredentialList.ItemsSource = credentials;

            if (currentSelectedValue != null)
            {
                SelectedItem.ChallengeCredentialKey = currentSelectedValue;
            }

            //select first credential by default
            if (credentials.Count() > 0)
            {
                var selectedCredential = credentials.FirstOrDefault(c => c.StorageKey == SelectedItem.ChallengeCredentialKey);
                if (selectedCredential != null)
                {
                    // ItemViewModel.PrimaryChallengeConfig.ChallengeCredentialKey = credentials.First().StorageKey;
                }
                else
                {
                    SelectedItem.ChallengeCredentialKey = credentials.First().StorageKey;
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
                    if (SelectedItem?.Parameters?.ContainsKey(pa.Key) == true)
                    {
                        pa.Value = SelectedItem.Parameters[pa.Key];
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
                    SelectedItem.Parameters.Remove(r.Key);
                }
            }
        }

    }
}
