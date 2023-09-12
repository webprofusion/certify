
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using Certify.Models;
using Certify.Models.Reporting;
using Certify.UI.ViewModel;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for Dashboard.xaml
    /// </summary>
    public partial class Dashboard : UserControl
    {
        public event FilterNotify FilterApplied; // filter notification event

        public class DailySummary : BindableBase
        {

        }

        public Summary ViewModel { get; set; } = new Summary();

        protected ViewModel.AppViewModel _appViewModel => AppViewModel.Current;

        public Dashboard()
        {
            InitializeComponent();

            DataContext = ViewModel;

            _appViewModel.PropertyChanged -= AppViewModel_PropertyChanged;
            _appViewModel.PropertyChanged += AppViewModel_PropertyChanged;
        }

        private void AppViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "ManagedCertificates")
            {
                RefreshSummary();
            }
        }

        public async Task RefreshSummary()
        {
            var summary = await AppViewModel.Current.GetManagedCertificateSummary();

            if (summary?.Total > 0)
            {

                ViewModel.Total = summary.Total;
                ViewModel.Healthy = summary.Healthy;
                ViewModel.Error = summary.Error;
                ViewModel.Warning = summary.Warning;
                ViewModel.AwaitingUser = summary.AwaitingUser;
                ViewModel.NoCertificate = summary.NoCertificate;

                // count items with invalid config (e.g. multiple primary domains)
                ViewModel.InvalidConfig = summary.InvalidConfig;

                ViewModel.TotalDomains = summary.TotalDomains;

                PanelTotal.Visibility = ViewModel.Total == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
                PanelHealthy.Visibility = ViewModel.Healthy == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
                PanelError.Visibility = ViewModel.Error == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
                PanelWarning.Visibility = ViewModel.Warning == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
                PanelAwaitingUser.Visibility = ViewModel.AwaitingUser == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

                Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                ViewModel.Total = 0;
                ViewModel.Healthy = 0;
                ViewModel.Error = 0;
                ViewModel.Warning = 0;
                ViewModel.AwaitingUser = 0;
                ViewModel.InvalidConfig = 0;
                ViewModel.NoCertificate = 0;

                ViewModel.TotalDomains = 0;

                Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void Hyperlink_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender != null)
            {
                var filter = (sender as System.Windows.Documents.Hyperlink).Tag.ToString();
                FilterApplied.Invoke(filter);
            }
        }
    }
}
