using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Certify.Models.Config;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for DeploymentTask.xaml
    /// </summary>
    public partial class DeploymentTask : UserControl
    {
        protected Certify.UI.ViewModel.AppViewModel AppViewModel => UI.ViewModel.AppViewModel.Current;
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ManagedCertificateViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;
        protected DeploymentProviderDefinition DeploymentProvider;

        public DeploymentTask()
        {
            InitializeComponent();

            this.TaskProviderList.ItemsSource = AppViewModel.DeploymentTaskProviders;
        }

        private void ParameterInput_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            //EditModel.SelectedItem.IsChanged = true;
        }

        private async void ShowParamLookup_Click(object sender, RoutedEventArgs e)
        {
           // EditModel.ShowZoneLookup = true;

        }

        private void TaskProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TaskProviderList.SelectedValue != null)
            {
                DeploymentProvider = AppViewModel.DeploymentTaskProviders.First(d => d.Id == TaskProviderList.SelectedValue.ToString());
                ProviderDescription.Text = DeploymentProvider.Description;
                DeploymentTaskParams.ItemsSource = DeploymentProvider.ProviderParameters;
            }
        }
    }
}
