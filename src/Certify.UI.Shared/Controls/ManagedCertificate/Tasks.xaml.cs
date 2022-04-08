using System.Windows;
using System.Windows.Controls;
using Certify.UI.Windows;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for Tasks.xaml
    /// </summary>
    public partial class Tasks : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ItemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;
        public Tasks()
        {
            InitializeComponent();
        }
        private void AddDeploymentTask_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dialog = new EditDeploymentTask(null, true)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }

        private void AddPreRequestTask_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dialog = new EditDeploymentTask(null, false)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }
    }
}
