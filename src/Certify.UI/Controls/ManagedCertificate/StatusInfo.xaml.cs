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
        protected Certify.UI.ViewModel.ManagedCertificateModel ItemViewModel => UI.ViewModel.ManagedCertificateModel.Current;

        public StatusInfo()
        {
            InitializeComponent();
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