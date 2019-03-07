using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

        public ManagedCertificate ParentManagedCertificate => _appViewModel.SelectedItem;

        public DeploymentTaskConfigViewModel(DeploymentTaskConfig item)
        {
            if (item == null)
            {
                item = new DeploymentTaskConfig
                {
                    Description = "A description for this task"
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
                  _appViewModel.StoredCredentials.Where(s => s.ProviderType.StartsWith("Certify.StandardChallenges"))
                  );
                }
                else
                {
                    return new ObservableCollection<StoredCredential>();
                }
            }
        }
    }
}
