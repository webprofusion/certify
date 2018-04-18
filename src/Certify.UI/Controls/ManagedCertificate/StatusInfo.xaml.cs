using Certify.Locales;
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

        public StatusInfo()
        {
            InitializeComponent();

            this.AppViewModel.PropertyChanged += AppViewModel_PropertyChanged; ;
        }

        private void AppViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SelectedItem")
            {
                if (ItemViewModel.SelectedItem != null)
                {
                    if (ItemViewModel.SelectedItem.Health == Models.ManagedCertificateHealth.OK)
                    {
                        this.RenewalSuccess.Visibility = Visibility.Visible;
                        this.RenewalFailed.Visibility = Visibility.Collapsed;
                    }

                    if (ItemViewModel.SelectedItem.Health == Models.ManagedCertificateHealth.Error ||
                        ItemViewModel.SelectedItem.Health == Models.ManagedCertificateHealth.Warning
                        )
                    {
                        this.RenewalSuccess.Visibility = Visibility.Collapsed;
                        this.RenewalFailed.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        private void OpenLogFile_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (this.ItemViewModel?.SelectedItem?.Id == null) return;

            // get file path for log
            var logPath = Models.ManagedCertificateLog.GetLogPath(this.ItemViewModel.SelectedItem.Id);

            //check file exists, if not inform user
            if (System.IO.File.Exists(logPath))
            {
                //open file
                System.Diagnostics.Process.Start(logPath);
            }
            else
            {
                MessageBox.Show(SR.ManagedCertificateSettings_LogNotCreated);
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