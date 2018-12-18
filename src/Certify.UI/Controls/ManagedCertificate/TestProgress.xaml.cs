using System.Windows;
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

        private void TextBlock_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // copy text to clipboard
            if (sender != null)
            {
                var text = (sender as TextBlock).Text;
                Clipboard.SetText(text);

                MessageBox.Show("Copied to clipboard");
            }
        }
    }
}
