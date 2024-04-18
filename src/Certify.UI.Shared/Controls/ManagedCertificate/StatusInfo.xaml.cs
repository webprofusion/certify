using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for Deployment.xaml 
    /// </summary>
    public partial class StatusInfo : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ItemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;
        protected Certify.UI.ViewModel.AppViewModel AppViewModel => UI.ViewModel.AppViewModel.Current;

        private string _tempLogFilePath;
        private System.Diagnostics.Process _tempLogViewerProcess;

        public StatusInfo()
        {
            InitializeComponent();

            AppViewModel.PropertyChanged -= AppViewModel_PropertyChanged;
            AppViewModel.PropertyChanged += AppViewModel_PropertyChanged;
        }

        private void AppViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SelectedItem")
            {
                Application.Current.Dispatcher.Invoke((Action)delegate
                {
                    if (ItemViewModel.SelectedItem != null)
                    {
                        if (ItemViewModel.SelectedItem.Health == Models.ManagedCertificateHealth.OK)
                        {
                            RenewalSuccess.Visibility = Visibility.Visible;
                            RenewalFailed.Visibility = Visibility.Collapsed;
                            RenewalPaused.Visibility = Visibility.Collapsed;
                        }
                        else if (ItemViewModel.SelectedItem.Health == Models.ManagedCertificateHealth.AwaitingUser)
                        {
                            RenewalSuccess.Visibility = Visibility.Collapsed;
                            RenewalFailed.Visibility = Visibility.Collapsed;
                            RenewalPaused.Visibility = Visibility.Visible;
                        }
                        else if (
                          ItemViewModel.SelectedItem.Health == Models.ManagedCertificateHealth.Error ||
                          ItemViewModel.SelectedItem.Health == Models.ManagedCertificateHealth.Warning
                          )
                        {
                            RenewalSuccess.Visibility = Visibility.Collapsed;
                            RenewalFailed.Visibility = Visibility.Visible;
                            RenewalPaused.Visibility = Visibility.Collapsed;
                        }

                        if (!string.IsNullOrEmpty(ItemViewModel?.SelectedItem.SourceId))
                        {
                            // hide log option if from external source
                            OpenLogFile.Visibility = Visibility.Hidden;
                        }
                        else
                        {
                            OpenLogFile.Visibility = Visibility.Visible;
                        }
                    }
                });
                
            }
        }

        private async void OpenLogFile_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (ItemViewModel?.SelectedItem?.Id == null || !string.IsNullOrEmpty(ItemViewModel?.SelectedItem.SourceId))
            {
                return;
            }

            // get file path for log
            var logPath = Models.ManagedCertificateLog.GetLogPath(ItemViewModel.SelectedItem.Id);

            //check file exists, if not inform user
            if (System.IO.File.Exists(logPath))
            {
                //open file
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(logPath) { UseShellExecute = true });
                }
                catch
                {
                    MessageBox.Show($"The system could not launch the default app for {logPath} - Open the file manually.");
                }
            }
            else
            {
                // fetch log from server

                try
                {
                    var log = await AppViewModel.GetItemLog(ItemViewModel.SelectedItem.Id, 1000);
                    var tempPath = System.IO.Path.GetTempFileName() + ".txt";

                    System.IO.File.WriteAllLines(tempPath, log.Select(i => i.ToString()));
                    _tempLogFilePath = tempPath;

                    try
                    {
                        _tempLogViewerProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tempPath) { UseShellExecute = true });
                        _tempLogViewerProcess.Exited += Process_Exited;

                    }
                    catch
                    {
                        MessageBox.Show($"The system could not launch the default app for {logPath} - Open the file manually.");
                    }
                }
                catch (Exception exp)
                {
                    MessageBox.Show("Failed to fetch log file. " + exp);
                }
            }
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            // attempt to cleanup temp log file copy
            if (!string.IsNullOrEmpty(_tempLogFilePath) && File.Exists(_tempLogFilePath))
            {
                try
                {
                    File.Delete(_tempLogFilePath);
                }
                catch { }
            }
        }

        private void LogViewer_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            try
            {
                if (e.Row.Item.ToString().Contains("[INF]"))
                {
                    e.Row.Foreground = new SolidColorBrush(Colors.Green);
                }
                else if (e.Row.Item.ToString().Contains("[ERR]"))
                {
                    e.Row.Foreground = new SolidColorBrush(Colors.Red);
                }
                else
                {
                    e.Row.Foreground = new SolidColorBrush(Colors.LightGray);
                }
            }
            catch
            {
            }
        }
    }
}
