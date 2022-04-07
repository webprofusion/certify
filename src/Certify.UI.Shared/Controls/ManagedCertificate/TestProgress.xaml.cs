using System.Threading.Tasks;
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
            DataContext = ItemViewModel;
        }

        private async Task<bool> WaitForClipboard(string text)
        {
            // if running under terminal services etc the clipboard can take multiple attempts to set
            // https://stackoverflow.com/questions/68666/clipbrd-e-cant-open-error-when-setting-the-clipboard-from-net
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    Clipboard.SetText(text);

                    return true;
                }
                catch { }

                await Task.Delay(50);
            }

            return false;
        }

        private async void TextBlock_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // copy text to clipboard
            if (sender != null)
            {
                var text = (sender as TextBlock).Text;
                var copiedOK = await WaitForClipboard(text);

                if (copiedOK)
                {
                    MessageBox.Show("Copied to clipboard");
                }
                else
                {
                    MessageBox.Show("Another process is preventing access to the clipboard. Please try again.");
                }
            }
        }
    }
}
