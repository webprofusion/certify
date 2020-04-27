using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Certify.Config;
using Certify.UI.Windows;

namespace Certify.UI.Controls.ManagedCertificate
{

    /// <summary>
    /// Renders a Task List (Deployment Tasks) 
    /// </summary>
    public partial class TaskList : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ItemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;

        public TaskList()
        {
            InitializeComponent();
        }

        private void EditDeploymentTask_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var config = (sender as Button).DataContext as DeploymentTaskConfig;

            var isPostRequestTask = ItemViewModel.SelectedItem.PostRequestTasks?.Any(i => i.Id == config.Id) == true;

            var dialog = new EditDeploymentTask(config, isPostRequestTask)
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
                ItemViewModel.SelectedItem.PreRequestTasks?.Remove(task);
                ItemViewModel.SelectedItem.PostRequestTasks?.Remove(task);
            }
        }


        private void TaskStartDrop(object sender, MouseButtonEventArgs e)
        {
            var task = (sender as DockPanel).DataContext as DeploymentTaskConfig;

            // Package the data.
            DataObject data = new DataObject();
            data.SetData(DataFormats.StringFormat, task.Id);

            // Inititate the drag-and-drop operation.
            DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
        }

        private void TaskCompleteDrop(object sender, DragEventArgs e)
        {
            DeploymentTaskConfig targetTask = null;

            if (e.OriginalSource is TextBlock)
            {
                targetTask = (e.OriginalSource as TextBlock).DataContext as DeploymentTaskConfig;
            }

            if (e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                string context = "PostRequest";

                var droppedTaskId = (string)e.Data.GetData(DataFormats.StringFormat);

                var droppedTask = ItemViewModel.SelectedItem.PostRequestTasks?.FirstOrDefault(t => t.Id == droppedTaskId);

                if (droppedTask == null)
                {
                    context = "PreRequest";
                    droppedTask = ItemViewModel.SelectedItem.PreRequestTasks?.FirstOrDefault(t => t.Id == droppedTaskId);
                }

                if (targetTask != null && droppedTask != null)
                {
                    //get index of target task and insert at that position
                    if (context == "PostRequest")
                    {
                        var targetIndex = ItemViewModel.SelectedItem.PostRequestTasks?.IndexOf(targetTask);
                        if (targetIndex >= 0)
                        {
                            ItemViewModel.SelectedItem.PostRequestTasks.Remove(droppedTask);
                            ItemViewModel.SelectedItem.PostRequestTasks.Insert((int)targetIndex, droppedTask);
                        }
                    }
                    else
                    {
                        var targetIndex = ItemViewModel.SelectedItem.PreRequestTasks?.IndexOf(targetTask);
                        if (targetIndex >= 0)
                        {
                            ItemViewModel.SelectedItem.PreRequestTasks.Remove(droppedTask);
                            ItemViewModel.SelectedItem.PreRequestTasks.Insert((int)targetIndex, droppedTask);
                        }
                    }
                }
                // re-order task list

                e.Effects = DragDropEffects.Move;


            }
            e.Handled = true;
        }
    }
}

