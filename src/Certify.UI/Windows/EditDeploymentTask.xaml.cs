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
using System.Windows.Shapes;
using Certify.Config;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for EditDeploymentTask.xaml
    /// </summary>
    public partial class EditDeploymentTask
    {
        public EditDeploymentTask(DeploymentTaskConfig config)
        {
            InitializeComponent();

            if (config != null)
            {
                DeploymentTaskEditor.SetEditItem(config);
            }
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
