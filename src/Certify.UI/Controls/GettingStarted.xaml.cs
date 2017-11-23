using System.Windows;
using System.Windows.Controls;

namespace Certify.UI.Controls
{
    /// <summary>
    /// Interaction logic for GettingStarted.xaml 
    /// </summary>
    public partial class GettingStarted : UserControl
    {
        public GettingStarted()
        {
            InitializeComponent();
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
    }
}