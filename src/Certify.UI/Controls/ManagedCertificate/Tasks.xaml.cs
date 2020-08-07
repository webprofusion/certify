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

    }
}
