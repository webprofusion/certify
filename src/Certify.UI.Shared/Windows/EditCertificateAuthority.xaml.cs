using System;
using System.Collections.Generic;
using System.Linq;
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

            public bool IsFeatureEnabled(CertAuthoritySupportedRequests feature)
            {
                return IsFeatureEnabled(feature.ToString());
            }

            public bool IsFeatureEnabled(string feature)
            {
                if (Item.SupportedFeatures.Contains(feature))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public void ToggleFeature(string feature)
            {
                if (IsFeatureEnabled(feature))
                {
                    Item.SupportedFeatures.Remove(feature);
                }
                else
                {
                    Item.SupportedFeatures.Add(feature);
                }
            }

        }

        public EditModel Model { get; set; } = new EditModel();

        public Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

        public EditCertificateAuthority()
        {
            InitializeComponent();

            DataContext = this;

            Width *= MainViewModel.UIScaleFactor;
            Height *= MainViewModel.UIScaleFactor;
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
                Close();
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
                    Model.Item = Newtonsoft.Json.JsonConvert.DeserializeObject<CertificateAuthority>(Newtonsoft.Json.JsonConvert.SerializeObject(ca));

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
                    Model.Item = ca;
                }

                DOMAIN_SINGLE.IsOn = Model.IsFeatureEnabled(CertAuthoritySupportedRequests.DOMAIN_SINGLE);
                DOMAIN_SINGLE_PLUS_WWW.IsOn = Model.IsFeatureEnabled(CertAuthoritySupportedRequests.DOMAIN_SINGLE_PLUS_WWW);
                DOMAIN_WILDCARD.IsOn = Model.IsFeatureEnabled(CertAuthoritySupportedRequests.DOMAIN_WILDCARD);
                DOMAIN_MULTIPLE_SAN.IsOn = Model.IsFeatureEnabled(CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN);

            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            var ca = MainViewModel.CertificateAuthorities.FirstOrDefault(a => a.Id == Model.SelectedCertificateAuthorityId);

            if (ca != null && ca.IsCustom)
            {
                if (MessageBox.Show("Are you sure you wish to delete this Certificate Authority?", "Confirm Delete?", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
                {
                    var result = await MainViewModel.DeleteCertificateAuthority(Model.Item.Id);
                    if (result.IsSuccess)
                    {
                        Close();
                    }
                    else
                    {
                        MessageBox.Show(result.Message);
                    }
                }
            }
            else
            {
                Close();
            }
        }

        private void OnFeatureToggled(object sender, RoutedEventArgs e)
        {
            if (sender != null)
            {
                var s = ((MahApps.Metro.Controls.ToggleSwitch)sender);
                var featureTag = s.Tag.ToString();

                Model.ToggleFeature(featureTag);
            }
        }
    }
}
