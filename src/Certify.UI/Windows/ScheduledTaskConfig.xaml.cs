using Certify.Locales;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

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

            var taskScheduler = new Shared.TaskScheduler();
            TaskConfigured = taskScheduler.IsWindowsScheduledTaskPresent();

            if (TaskConfigured)
            {
                AutoRenewPrompt.Text = SR.ScheduledTaskConfig_AlreadyConfiged;
                AutoRenewPrompt.Foreground = Brushes.DarkGreen;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            //create/update scheduled task
            if (!String.IsNullOrEmpty(Username.Text) && (!String.IsNullOrEmpty(Password.Password)))
            {
                var taskScheduler = new Shared.TaskScheduler();
                if (taskScheduler.CreateWindowsScheduledTask(Username.Text, Password.Password))
                {
                    MessageBox.Show(SR.ScheduledTaskConfig_TaskCreated);
                    this.Close();
                }
                else
                {
                    MessageBox.Show(SR.ScheduledTaskConfig_FailedToCreateTask);
                }
            }
            else
            {
                MessageBox.Show(SR.ScheduledTaskConfig_PleaseProvideCredential);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}