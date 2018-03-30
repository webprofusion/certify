using System.Windows.Controls;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for DomainAuthorization.xaml 
    /// </summary>
    public partial class DomainAuthorization : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ItemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;
        protected Certify.UI.ViewModel.AppViewModel AppViewModel => UI.ViewModel.AppViewModel.Current;

        public DomainAuthorization()
        {
            InitializeComponent();

            this.AppViewModel.PropertyChanged += AppViewModel_PropertyChanged;
        }

        private void AppViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SelectedItem")
            {
                //RefreshCredentialOptions();
                //ItemViewModel.RaisePropertyChanged(nameof(ItemViewModel.PrimaryChallengeConfig));
            }
        }
    }
}