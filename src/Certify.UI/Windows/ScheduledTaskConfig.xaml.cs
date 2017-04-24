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

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for ScheduledTaskConfig.xaml
    /// </summary>
    public partial class ScheduledTaskConfig
    {
        public ScheduledTaskConfig()
        {
            InitializeComponent();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            //create/update scheduled task
            if (!String.IsNullOrEmpty(Username.Text) && (!String.IsNullOrEmpty(Password.Password)))
            {
                var certifyManager = new Certify.Management.CertifyManager();
                if (certifyManager.CreateWindowsScheduledTask(Username.Text, Password.Password))
                {
                    MessageBox.Show("Scheduled task created");
                    this.Close();
                }
                else
                {
                    MessageBox.Show("Failed to create scheduled task with given credentials");
                }
            }
            else
            {
                MessageBox.Show("Please provide the username and password for an admin level user.");
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}