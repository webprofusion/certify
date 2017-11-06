using Certify.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Certify.UI
{
    /// <summary>
    /// Mock data view model for use in the XAML designer in Visual Studio
    /// </summary>
    public class DesignViewModel : ViewModel.AppModel
    {
        public DesignViewModel()
        {
            // create mock registration
            PrimaryContactEmail = "username@example.org";

            // generate mock data starting point
            GenerateMockData();
            SaveSettings();

            // auto-load data if in WPF designer
            bool inDesignMode = !(Application.Current is App);
            if (inDesignMode)
            {
                SelectedItem = ManagedSites.First();
            }
        }

        internal override void LoadVaultTree()
        {
            List<VaultItem> tree = new List<VaultItem>();
            VaultTree = tree;
        }

        private void GenerateMockData()
        {
            // generate 20 mock sites
            ManagedSites = new ObservableCollection<ManagedSite>();
            for (int i = 1; i <= 20; i++)
            {
                var site = new ManagedSite()
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"test{i}.example.org",
                    ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS,
                    DateExpiry = DateTime.Now.AddDays(60 - 5 * i),
                    RequestConfig = new CertRequestConfig()
                    {
                        ChallengeType = ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_SNI,
                        PerformAutomatedCertBinding = true,
                        PreRequestPowerShellScript = @"c:\inetpub\scripts\pre-req-script.ps1",
                        PostRequestPowerShellScript = @"c:\inetpub\scripts\post-req-script.ps1",
                        WebhookTrigger = Webhook.ON_SUCCESS,
                        WebhookUrl = "https://certifytheweb.com/api/notify?domain=$domain&key=123456",
                        WebhookMethod = Webhook.METHOD_POST
                    },
                    CertificatePath = @"C:\ProgramData\ACMESharp\sysVault\99-ASSET\cert_ident1a2b3c4d-all.pfx"
                };
                site.DomainOptions.Add(new DomainOption()
                {
                    Domain = site.Name,
                    IsPrimaryDomain = true,
                    IsSelected = true
                });
                // add lots of mock domains
                for (int j = 1; j <= 20; j++)
                {
                    site.DomainOptions.Add(new DomainOption()
                    {
                        Domain = $"www{j}.{site.Name}",
                        IsSelected = j <= 3
                    });
                }
                ManagedSites.Add(site);
            }
        }

        private string MockDataStore;

        public override void LoadSettings()
        {
            var mockSites = JsonConvert.DeserializeObject<List<ManagedSite>>(MockDataStore);
            foreach (var site in mockSites) site.IsChanged = false;
            ManagedSites = new ObservableCollection<ManagedSite>(mockSites);
            ImportedManagedSites = new ObservableCollection<ManagedSite>();
        }

        public override void SaveSettings()
        {
            MockDataStore = JsonConvert.SerializeObject(ManagedSites);
            foreach (var site in ManagedSites) site.IsChanged = false;
            ManagedSites = new ObservableCollection<ManagedSite>(ManagedSites);
        }

        public override List<SiteBindingItem> WebSiteList => 
            Enumerable.Range(1, 20).Select(i => new SiteBindingItem()
            {
                SiteId = i.ToString(),
                SiteName = $"Website {i}",
                PhysicalPath = $@"c:\inetpub\wwwroot\website{i}",
                IsEnabled = true
            })
            .ToList();

        public override bool IsIISAvailable => true;
        public override Version IISVersion => new Version(10,0);
        public override bool HasRegisteredContacts => true;

        protected override IEnumerable<DomainOption> GetDomainOptionsFromSite(string siteId)
        {
            return Enumerable.Range(1, 50).Select(i => new DomainOption()
            {
                Domain = $"www{i}.domain.example.org",
                IsPrimaryDomain = i == 1,
                IsSelected = true
            });
        }
    }
}
