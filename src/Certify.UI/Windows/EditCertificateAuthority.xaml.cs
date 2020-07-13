using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Certify.Models;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Edit details for an ACME Account
    /// </summary>
    public partial class EditCertificateAuthority
    {
        public class EditModel : BindableBase
        {
            public string SelectedCertificateAuthorityId { get; set; }
            public CertificateAuthority Item { get; set; } = new CertificateAuthority();
            public IEnumerable<CertificateAuthority> CertificateAuthorities
            {
                get
                {
                    var list = new List<CertificateAuthority>(ViewModel.AppViewModel.Current.CertificateAuthorities);
                    list.Insert(0, new CertificateAuthority { Id = null, Title = "(New Certificate Authority)" });

                    return list;
                }
            }
        }

        public EditModel Model { get; set; } = new EditModel();

        public Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

        public EditCertificateAuthority()
        {
            InitializeComponent();

            DataContext = this;

            this.Width *= MainViewModel.UIScaleFactor;
            this.Height *= MainViewModel.UIScaleFactor;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Mouse.OverrideCursor = Cursors.Arrow;
            Close();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            // add/update ca
            var result = await MainViewModel.UpdateCertificateAuthority(Model.Item);

            if (result.IsSuccess)
            {
                this.Close();
            }
            else
            {
                MessageBox.Show(result.Message);
            }

        }

        private void CertificateAuthorityList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CertificateAuthorityList.SelectedItem != null)
            {
                if (((CertificateAuthority)CertificateAuthorityList.SelectedItem).Id != null)
                {
                    // edit existing
                    var ca = MainViewModel.CertificateAuthorities.FirstOrDefault(a => a.Id == Model.SelectedCertificateAuthorityId);
                    this.Model.Item = Newtonsoft.Json.JsonConvert.DeserializeObject<CertificateAuthority>(Newtonsoft.Json.JsonConvert.SerializeObject(ca));
                }
                else
                {
                    // add new
                    CertificateAuthority ca = new CertificateAuthority
                    {
                        Id = Guid.NewGuid().ToString().ToLower(),
                        IsCustom = true,
                        IsEnabled = true,
                        APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                        SupportedFeatures = new List<string> {
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString()
                }
                    };
                    this.Model.Item = ca;
                }

            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you wish to delete this Certificate Authority?", "Confirm Delete?", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
            {
                var result = await MainViewModel.DeleteCertificateAuthority(Model.Item.Id);
                if (result.IsSuccess)
                {
                    this.Close();
                }
                else
                {
                    MessageBox.Show(result.Message);
                }
            }
        }
    }
}
