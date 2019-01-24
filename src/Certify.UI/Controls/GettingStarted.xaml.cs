using System.Windows;
using System.Windows.Controls;

namespace Certify.UI.Controls
{
    /// <summary>
    /// Interaction logic for GettingStarted.xaml 
    /// </summary>
    public partial class GettingStarted : UserControl
    {
        protected Certify.UI.ViewModel.AppViewModel AppViewModel => UI.ViewModel.AppViewModel.Current;
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ItemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;


        public GettingStarted()
        {
            InitializeComponent();
            this.DataContext = AppViewModel;
          
        }

        private void AddToDashboard_Click(object sender, RoutedEventArgs e)
        {
            var d = new Windows.AddToDashboard { Owner = Window.GetWindow(this) };
            d.ShowDialog();
        }

        private void ViewDashboard_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://certifytheweb.com/dashboard");
        }

        private void QuickStart_Click(object sender, RoutedEventArgs e)
        {
            var d = new Windows.GettingStartedGuide { Owner = Window.GetWindow(this) };
            d.Show();
        }
    }
}
