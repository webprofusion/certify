using System.Windows.Controls;

namespace Certify.UI.Controls.ManagedCertificate
{
    public partial class TestProgress : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ItemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;
        protected Certify.UI.ViewModel.AppViewModel AppViewModel => UI.ViewModel.AppViewModel.Current;

        public TestProgress()
        {
            InitializeComponent();
            this.DataContext = ItemViewModel;
        }
    }
}