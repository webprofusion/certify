using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Certify.Config;
using Certify.UI.Windows;

namespace Certify.UI.Controls.ManagedCertificate
{
    public class ListOption
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public object Value { get; set; }
    }

    /// <summary>
    /// Interaction logic for Deployment.xaml 
    /// </summary>
    public partial class Deployment : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ItemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;

        public Deployment()
        {
            InitializeComponent();

            DeploymentSiteOptions.ItemsSource = new List<ListOption>
            {
                  new ListOption{
                    Title ="Auto",
                    Value = Models.DeploymentOption.Auto,
                    Description="Automatic Deployment, Use Defaults"
                },
                new ListOption{
                    Title ="Single Site (selected in Domains tab)",
                    Value = Models.DeploymentOption.SingleSite,
                    Description="Only update bindings for the selected website."
                },
                 new ListOption{
                    Title ="All Sites",
                    Value = Models.DeploymentOption.AllSites,
                    Description="Update bindings for all sites, as applicable."
                },
                  new ListOption{
                    Title ="Certificate Store Only",
                    Value = Models.DeploymentOption.DeploymentStoreOnly,
                    Description="Only store the certificate, no deployment."
                },
                   new ListOption{
                    Title ="No Deployment",
                    Value = Models.DeploymentOption.NoDeployment,
                    Description="Certificate is not deployed."
                }
            };

            DeploymentBindingUpdates.ItemsSource = new List<ListOption> {
                new ListOption
                {
                    Title="Add or Update https bindings as required",
                    Value=Models.DeploymentBindingOption.AddOrUpdate,
                    Description="Existing https bindings will be updated with the new certificate, new bindings will be created as required."
                },
                  new ListOption
                {
                    Title=" Update existing https bindings only",
                    Value=Models.DeploymentBindingOption.UpdateOnly,
                    Description="Existing https bindings will be updated with the new certificate as required."
                }
            };

        }

        private void DeploymentSiteOptions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // if deployment mode changes, apply defaults for the mode
            ItemViewModel.SelectedItem?.RequestConfig?.ApplyDeploymentOptionDefaults();

        }

        private void AddDeploymentTask_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dialog = new EditDeploymentTask(null, true)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.Show();
        }

        private void AddPreRequestTask_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dialog = new EditDeploymentTask(null, false)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.Show();
        }


        private void EditDeploymentTask_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var config = (sender as Button).DataContext as DeploymentTaskConfig;
            var dialog = new EditDeploymentTask(config, true)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.Show();
        }

        private async void RunDeploymentTask_Click(object sender, RoutedEventArgs e)
        {
            var task = (sender as Button).DataContext as DeploymentTaskConfig;

            // save main first
            if (ItemViewModel.SelectedItem.IsChanged)
            {
                (App.Current as App).ShowNotification("You have unsaved changes.", App.NotificationType.Error, true);
                return;
            }

            if (MessageBox.Show("Run task '" + task.TaskName + "' now? The most recent certificate details will be used.", "Run Task?", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                // execute task now
                Mouse.OverrideCursor = Cursors.Wait;
                var results = await UI.ViewModel.AppViewModel.Current.PerformDeployment(ItemViewModel.SelectedItem.Id, task.Id, isPreviewOnly: false);
                Mouse.OverrideCursor = Cursors.Arrow;

                if (results.Any(r => r.HasError))
                {
                    var result = results.First(r => r.HasError == true);
                    MessageBox.Show($"The deployment task failed to complete. {result.Title} :: {result.Description}");
                }
                else
                {
                    (App.Current as App).ShowNotification("The deployment task completed with no reported errors.");

                }
            }
        }

        private void DeleteDeploymentTask_Click(object sender, RoutedEventArgs e)
        {
            var task = (sender as Button).DataContext as DeploymentTaskConfig;
            if (MessageBox.Show("Are you sure you wish to delete task '" + task.TaskName + "'?", "Delete Task?", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                ItemViewModel.SelectedItem.PostRequestTasks.Remove(task);
            }
        }
    }
}

