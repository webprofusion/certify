using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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

    internal class StateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value.ToString() == RequestState.Error.ToString())
            {
                return new SolidColorBrush(System.Windows.Media.Colors.Red);
            }
            if (value.ToString() == RequestState.Success.ToString())
            {
                return new SolidColorBrush(System.Windows.Media.Colors.Green);
            }

            return new SolidColorBrush(System.Windows.Media.Colors.DarkGray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
    }
}
