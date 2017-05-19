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
        public bool TaskConfigured { get; set; } = false;

        public ScheduledTaskConfig()
        {
            InitializeComponent();
            //check if scheduled task already configured

            var certifyManager = new Certify.Management.CertifyManager();
            TaskConfigured = certifyManager.IsWindowsScheduledTaskPresent();

            if (TaskConfigured)
            {
                AutoRenewPrompt.Text = "The auto renewal task is already configured. If required you can change the admin user account used to execute the task.";
                AutoRenewPrompt.Foreground = Brushes.DarkGreen;
            }
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