using System;
using System.Windows;
using System.Windows.Input;
using Certify.UI.ViewModel;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for AddToDashboard.xaml 
    /// </summary>
    public partial class AddToDashboard
    {
        protected Certify.UI.ViewModel.AppViewModel AppViewModel => UI.ViewModel.AppViewModel.Current;
        public bool IsRemovalMode { get; set; }

        public AddToDashboard()
        {
            InitializeComponent();

            DataContext = AppViewModel;

            Width *= AppViewModel.UIScaleFactor;
            Height *= AppViewModel.UIScaleFactor;

            Loaded += AddToDashboard_Loaded;
        }

        private void AddToDashboard_Loaded(object sender, RoutedEventArgs e)
        {

            var introText = FindName("IntroText") as System.Windows.Controls.TextBlock;

            if (IsRemovalMode)
            {
                Title = "Remove from Dashboard";
                if (introText != null)
                {
                    introText.Text = "To remove this server from your dashboard, provide your https://certifytheweb.com/profile sign in details:";
                }

                ValidateKey.Content = "Remove";
            }
            else
            {
                Title = Certify.Locales.SR.GettingStarted_AddToDashboard;

                if (introText != null)
                {
                    introText.Text = Certify.Locales.SR.Dashboard_AddIntro;
                }

                ValidateKey.Content = Certify.Locales.SR.OK;
            }
        }

        private async void ValidateKey_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailAddress.Text?.Trim().ToLower();
            var pwd = Password.Password.Trim();

            if (string.IsNullOrEmpty(email))
            {
                MessageBox.Show(Certify.Locales.SR.Registration_NeedEmail);
                return;
            }

            if (string.IsNullOrEmpty(pwd))
            {
                // MessageBox.Show(Certify.Locales.SR.Registration_NeedKey);
                return;
            }

            ValidateKey.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;

            var dashboardClient = ViewModel.AppViewModel.Current.DashboardClient;

            if (dashboardClient != null)
            {
                try
                {
                    var instance = new Models.Shared.RegisteredInstance
                    {
                        InstanceId = ViewModel.AppViewModel.Current.Preferences.InstanceId,
                        AppVersion = Management.Util.GetAppVersion().ToString(),
                        OS = Environment.OSVersion.ToString(),
                        MachineName = Environment.MachineName
                    };

                    var resultOK = IsRemovalMode
                        ? await dashboardClient.RemoveInstance(instance, email, pwd)
                        : await dashboardClient.RegisterInstance(instance, email, pwd, createAccount: false);
                    Mouse.OverrideCursor = Cursors.Arrow;

                    if (resultOK)
                    {
                        if (IsRemovalMode)
                        {
                            await ViewModel.AppViewModel.Current.SetInstanceRegisteredOnDashboard(false);
                            MessageBox.Show("Server removed from dashboard.");
                        }
                        else
                        {
                            await ViewModel.AppViewModel.Current.SetInstanceRegisteredOnDashboard(true);
                            var queueResult = await ViewModel.AppViewModel.Current.QueueAllDashboardStatusReports();

                            if (queueResult?.IsSuccess == true)
                            {
                                MessageBox.Show("Server registration completed and all certificate status reports were queued.");
                            }
                            else
                            {
                                MessageBox.Show("Server registration completed, but queuing certificate status reports did not complete: " + (queueResult?.Message ?? "Unknown error"));
                            }
                        }

                        Close();
                    }
                    else
                    {
                        if (IsRemovalMode)
                        {
                            var confirmMarkRemoved = MessageBox.Show(
                                "Server removal could not complete. Check your username and password is correct and that outgoing https connections are allowed from this machine. If you no longer control the dashboard account, do you want to mark this server as removed from the dashboard anyway?",
                                "Remove from Dashboard",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);

                            if (confirmMarkRemoved == MessageBoxResult.Yes)
                            {
                                await ViewModel.AppViewModel.Current.SetInstanceRegisteredOnDashboard(false);
                                MessageBox.Show("Server marked as removed from dashboard.");
                                Close();
                            }
                        }
                        else
                        {
                            MessageBox.Show("Server registration could not complete. Check your username and password is correct and that outgoing https connections are allowed from this machine.");
                        }
                    }
                }
                catch (Exception)
                {
                    if (IsRemovalMode)
                    {
                        var confirmMarkRemoved = MessageBox.Show(
                            "Server removal could not complete. If you no longer control the dashboard account, do you want to mark this server as removed from the dashboard anyway?",
                            "Remove from Dashboard",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (confirmMarkRemoved == MessageBoxResult.Yes)
                        {
                            await ViewModel.AppViewModel.Current.SetInstanceRegisteredOnDashboard(false);
                            MessageBox.Show("Server marked as removed from dashboard.");
                            Close();
                        }
                    }
                    else
                    {
                        MessageBox.Show(Certify.Locales.SR.Registration_KeyValidationError);
                    }
                }
            }
            else
            {
                MessageBox.Show(Certify.Locales.SR.Registration_UnableToVerify);
            }

            ValidateKey.IsEnabled = true;
            Mouse.OverrideCursor = Cursors.Arrow;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    }
}
