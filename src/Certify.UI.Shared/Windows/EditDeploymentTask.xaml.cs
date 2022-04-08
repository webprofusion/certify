using System.Windows;
using Certify.Config;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for EditDeploymentTask.xaml
    /// </summary>
    public partial class EditDeploymentTask
    {

        protected Certify.UI.ViewModel.AppViewModel AppViewModel => UI.ViewModel.AppViewModel.Current;

        public EditDeploymentTask(DeploymentTaskConfig config, bool editAsPostRequestTask)
        {
            InitializeComponent();

            DataContext = AppViewModel;

            Width *= AppViewModel.UIScaleFactor;
            Height *= AppViewModel.UIScaleFactor;

            DeploymentTaskEditor.SetEditItem(config, editAsPostRequestTask);
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (await DeploymentTaskEditor.Save())
            {
                Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
