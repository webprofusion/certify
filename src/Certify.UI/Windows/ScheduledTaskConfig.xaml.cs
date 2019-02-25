using System.Windows;
using System.Windows.Media;
using Certify.Locales;
using Certify.UI.ViewModel;

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
            TaskSettings.UseBackgroundService = AppViewModel.Current.Preferences.UseBackgroundServiceAutoRenewal;

            var taskScheduler = new Shared.TaskScheduler();
            TaskSettings.TaskConfigured = taskScheduler.IsWindowsScheduledTaskPresent();

            if (TaskSettings.TaskConfigured)
            {
                AutoRenewPrompt.Text = SR.ScheduledTaskConfig_AlreadyConfiged;
                AutoRenewPrompt.Foreground = Brushes.DarkGreen;
            }
            DataContext = TaskSettings;

            if (TaskSettings.UseBackgroundService)
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
                var prefs = await AppViewModel.Current.CertifyClient.GetPreferences();

                prefs.UseBackgroundServiceAutoRenewal = true;

                await AppViewModel.Current.SetPreferences(prefs);

                //remove any existing scheduled task
                taskScheduler.DeleteWindowsScheduledTask();

                Close();
            }
            else
            {
                //create/update scheduled task
                if (!string.IsNullOrEmpty(Username.Text) && (!string.IsNullOrEmpty(Password.Password)))
                {
                    if (taskScheduler.CreateWindowsScheduledTask(Username.Text, Password.Password))
                    {
                        // set pref to use scheduled task for auto renewal let background service do renewals
                        var prefs = await AppViewModel.Current.CertifyClient.GetPreferences();
                        prefs.UseBackgroundServiceAutoRenewal = false;
                        await AppViewModel.Current.SetPreferences(prefs);

                        MessageBox.Show(SR.ScheduledTaskConfig_TaskCreated);
                        Close();
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

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    }
}
