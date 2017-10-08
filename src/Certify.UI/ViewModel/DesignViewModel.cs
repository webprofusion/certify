using Certify.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.UI
{
    /// <summary>
    /// Mock data view model for use in the WPF designer in Visual Studio
    /// </summary>
    public class DesignViewModel : ViewModel.AppModel
    {
        public DesignViewModel()
        {
            // generate 20 mock sites
            var msites = new List<ManagedSite>();
            for (int i=1; i<=20; i++)
            {
                msites.Add(new ManagedSite()
                {
                    Name = $"test{i}.example.org",
                    ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS,
                    DateExpiry = DateTime.Now.AddDays(60-5*i)
                });
            }
            ManagedSites = new ObservableCollection<ManagedSite>(msites);

            // flesh out the selected site
            SelectedItem = msites.First();
            SelectedItem.RequestConfig = new CertRequestConfig()
            {
                ChallengeType = ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_SNI,
                PerformAutomatedCertBinding = true,
                PreRequestPowerShellScript = @"c:\inetpub\scripts\pre-req-script.ps1",
                PostRequestPowerShellScript = @"c:\inetpub\scripts\post-req-script.ps1",
                WebhookTrigger = Webhook.ON_SUCCESS,
                WebhookUrl = "https://certifytheweb.com/api/notify?domain=$domain&key=123456",
                WebhookMethod = Webhook.METHOD_POST
            };
            SelectedItem.CertificatePath = @"C:\ProgramData\ACMESharp\sysVault\99-ASSET\cert_ident1a2b3c4d-all.pfx";
            SelectedItem.DomainOptions = new ObservableCollection<DomainOption>();
            SelectedItem.DomainOptions.Add(new DomainOption()
            {
                Domain = SelectedItem.Name,
                IsPrimaryDomain = true,
                IsSelected = true
            });
            // add lots of mock domains
            for (int i=1; i<=20; i++)
            {
                SelectedItem.DomainOptions.Add(new DomainOption()
                {
                    Domain = $"www{i}.{SelectedItem.Name}",
                    IsSelected = i <= 3
                });
            }

            // create mock registration
            PrimaryContactEmail = "username@example.org";
        }

        internal override void LoadVaultTree()
        {
            List<VaultItem> tree = new List<VaultItem>();
            VaultTree = tree;
        }
    }
}
