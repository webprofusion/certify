using Certify.Locales;
using Certify.UI.ViewModel;
using System;
using System.Windows;
using System.Windows.Media;

namespace Certify.UI.Windows
{
    public class TaskVm : Models.BindableBase
    {
        public bool TaskConfigured { get; set; } = false;
        public bool UseBackgroundService { get; set; } = true;
    }

    /// <summary>
    /// Interaction logic for ScheduledTaskConfig.xaml 
    /// </summary>
    public partial class ScheduledTaskConfig
    {
        public TaskVm TaskSettings = new TaskVm();

        public ScheduledTaskConfig()
        {
            InitializeComponent();

            //check if scheduled task already configured
            this.TaskSettings.UseBackgroundService = AppModel.Current.Preferences.UseBackgroundServiceAutoRenewal;

            var taskScheduler = new Shared.TaskScheduler();
            this.TaskSettings.TaskConfigured = taskScheduler.IsWindowsScheduledTaskPresent();

            if (this.TaskSettings.TaskConfigured)
            {
                AutoRenewPrompt.Text = SR.ScheduledTaskConfig_AlreadyConfiged;
                AutoRenewPrompt.Foreground = Brushes.DarkGreen;
            }
            this.DataContext = this.TaskSettings;

            if (this.TaskSettings.UseBackgroundService)
            {
                RadioUseBackgroundService.IsChecked = true;
                RadioUseScheduledTask.IsChecked = false;
            }
            else
            {
                RadioUseBackgroundService.IsChecked = false;
                RadioUseScheduledTask.IsChecked = true;
            }
        }

        private async void OK_Click(object sender, RoutedEventArgs e)
        {
            var taskScheduler = new Shared.TaskScheduler();

            if (TaskSettings.UseBackgroundService)
            {
                // let background service do renewals
                var prefs = await AppModel.Current.CertifyClient.GetPreferences();

                prefs.UseBackgroundServiceAutoRenewal = true;

                await AppModel.Current.SetPreferences(prefs);

                //remove any existing scheduled task
                taskScheduler.DeleteWindowsScheduledTask();

                this.Close();
            }
            else
            {
                //create/update scheduled task
                if (!String.IsNullOrEmpty(Username.Text) && (!String.IsNullOrEmpty(Password.Password)))
                {
                    if (taskScheduler.CreateWindowsScheduledTask(Username.Text, Password.Password))
                    {
                        // set pref to use scheduled task for auto renewal let background service do renewals
                        var prefs = await AppModel.Current.CertifyClient.GetPreferences();
                        prefs.UseBackgroundServiceAutoRenewal = false;
                        await AppModel.Current.SetPreferences(prefs);

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
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void UseBackgroundService_Checked(object sender, RoutedEventArgs e)
        {
            // UseBackgroundService = true;
        }

        private void UseScheduledTask_Checked(object sender, RoutedEventArgs e)
        {
            // UseBackgroundService = false;
        }
    }
}