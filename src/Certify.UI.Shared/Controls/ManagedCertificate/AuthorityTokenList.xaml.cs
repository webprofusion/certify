using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Certify.Models;
using Certify.UI.ViewModel;
using Microsoft.IdentityModel.JsonWebTokens;
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
                    try
                    {

                        var tokenWrapper = JsonSerializer.Deserialize<TkAuthToken>(fileContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (tokenWrapper != null)
                        {
                            Token.Text = tokenWrapper.Token;
                            CRL.Text = tokenWrapper.Crl;
                        }
                    }
                    catch
                    {
                        MessageBox.Show("The file provided does not appear to be either a valid token or a json token wrapper.");
                        return;
                    }
                }
                else
                {
                    if (fileContent.Length < 8192)
                    {
                        Token.Text = fileContent.Trim();
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
                MessageBox.Show("Both the authority token and CRL url are required.");
                return;
            }

            JsonWebToken parsedJwt = null;

            try
            {
                parsedJwt = new JsonWebToken(jwtEncodedString: tokenWrapper.Token);
            }
            catch
            { }

            if (parsedJwt == null)
            {
                MessageBox.Show("The Authority Token supplied is not a valid JWT (JSON Web Token).");
                return;
            }

            if (parsedJwt.ValidTo < DateTime.Now)
            {
                MessageBox.Show("The Authority Token has expired.");
                return;
            }

            var parsedAtc = CertRequestConfig.GetParsedAtc(tokenWrapper.Token);

            if (parsedAtc == null)
            {
                MessageBox.Show("Both the authority token and CRL url are required.");
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
