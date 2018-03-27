using System.Windows.Controls;

namespace Certify.UI.Controls.ManagedCertificate
{
    public partial class TestProgress : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateModel ItemViewModel => UI.ViewModel.ManagedCertificateModel.Current;
        protected Certify.UI.ViewModel.AppModel AppViewModel => UI.ViewModel.AppModel.Current;

        public TestProgress()
        {
            InitializeComponent();
            this.DataContext = ItemViewModel;
        }
    }
}