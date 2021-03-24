using System;
using System.Windows.Controls;
using System.Windows.Input;
using Certify.Models;

namespace Certify.UI.Controls
{
    /// <summary>
    /// Interaction logic for ProgressMonitor.xaml 
    /// </summary>
    public partial class ProgressMonitor : UserControl
    {
        protected Certify.UI.ViewModel.AppViewModel MainViewModel => UI.ViewModel.AppViewModel.Current;

        public ProgressMonitor()
        {
            InitializeComponent();
            DataContext = MainViewModel;
        }

        private void ManagedCertificate_ViewLog(object sender, MouseButtonEventArgs e)
        {
            // show log for the selected managed site
            try
            {
                var itemId = ((RequestProgressState)((StackPanel)sender).DataContext).ManagedCertificate.Id;
                var logPath = Models.ManagedCertificateLog.GetLogPath(itemId);
                if (System.IO.File.Exists(logPath))
                {
                    //open file
                    System.Diagnostics.Process.Start(logPath);
                }
            }
            catch (Exception) { }
        }
    }


}
