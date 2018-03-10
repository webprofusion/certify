using Certify.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace Certify.UI.Controls.ManagedItem
{
    /// <summary>
    /// Interaction logic for DomainAuthorization.xaml 
    /// </summary>
    public partial class DomainAuthorization : UserControl
    {
        protected Certify.UI.ViewModel.ManagedItemModel ItemViewModel => UI.ViewModel.ManagedItemModel.Current;

        public DomainAuthorization()
        {
            InitializeComponent();

            ChallengeProviderList.ItemsSource = Models.Config.ChallengeProviders.Providers.Where(p => p.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS);
        }

        private void AddStoredCredential_Click(object sender, RoutedEventArgs e)
        {
        }

        private void DirectoryBrowse_Click(object sender, EventArgs e)
        {
            var config = ItemViewModel.SelectedItem.RequestConfig;
            var dialog = new WinForms.FolderBrowserDialog()
            {
                SelectedPath = config.WebsiteRootPath
            };
            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                config.WebsiteRootPath = dialog.SelectedPath;
            }
        }
    }
}