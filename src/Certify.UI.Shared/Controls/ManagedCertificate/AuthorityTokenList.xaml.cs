using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Certify.Models;
using Certify.UI.ViewModel;
using Microsoft.Win32;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for AuthorityTokenList.xaml
    /// </summary>
    public partial class AuthorityTokenList : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ItemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;

        public AuthorityTokenList()
        {
            InitializeComponent();
        }

        private void AddFromFile_Click(object sender, RoutedEventArgs e)
        {
            // browse to file, add token from JSON
            var openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                var fileContent = File.ReadAllText(openFileDialog.FileName);

                if (fileContent.Trim().StartsWith("{"))
                {

                    var tokenWrapper = JsonSerializer.Deserialize<TkAuthToken>(fileContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (tokenWrapper != null)
                    {
                        Token.Text = tokenWrapper.Token;
                        CRL.Text = tokenWrapper.Crl;
                    }
                }
            }
        }

        private void AddToken_Click(object sender, RoutedEventArgs e)
        {
            var tokenWrapper = new TkAuthToken
            {
                Token = Token.Text.Trim(),
                Crl = CRL.Text.ToLower().Trim()
            };

            if (string.IsNullOrEmpty(tokenWrapper.Token) || string.IsNullOrEmpty(tokenWrapper.Crl))
            {
                return;
            }

            if (ItemViewModel.SelectedItem.RequestConfig.AuthorityTokens == null)
            {
                ItemViewModel.SelectedItem.RequestConfig.AuthorityTokens = new System.Collections.ObjectModel.ObservableCollection<TkAuthToken>();
            }

            if (!ItemViewModel.SelectedItem.RequestConfig.AuthorityTokens.Any(t => t.Token == tokenWrapper.Token))
            {
                ItemViewModel.SelectedItem.RequestConfig.AuthorityTokens.Add(tokenWrapper);
            }

            Token.Text = "";
            CRL.Text = "";
            ItemViewModel.RaisePropertyChangedEvent(nameof(ItemViewModel.ParsedTokenList));
        }

        private void DeleteToken_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as Button).DataContext as ManagedCertificateViewModel.AuthorityToken;

            if (item != null)
            {
                var token = ItemViewModel.SelectedItem.RequestConfig.AuthorityTokens.FirstOrDefault(a => a.Token == item.Token);
                if (token != null)
                {
                    ItemViewModel.SelectedItem.RequestConfig.AuthorityTokens.Remove(token);
                    ItemViewModel.RaisePropertyChangedEvent(nameof(ItemViewModel.ParsedTokenList));
                }
            }
        }
    }
}
