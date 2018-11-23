using Certify.Models;
using Certify.Shared.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace Certify.UI
{
    /// <summary>
    /// Mock data view model for use in the XAML designer in Visual Studio 
    /// </summary>
    public class AppDesignViewModel : ViewModel.AppViewModel
    {
        public AppDesignViewModel()
        {
            // create mock registration
            PrimaryContactEmail = "username@example.org";

            // generate mock data starting point
            GenerateMockData();

            // auto-load data if in WPF designer
            bool inDesignMode = !(Application.Current is App);
            if (inDesignMode)
            {
                SelectedItem = ManagedCertificates.First();
            }
        }

        private void GenerateMockData()
        {
            // generate 20 mock sites
            ManagedCertificates = new ObservableCollection<ManagedCertificate>();
            for (int i = 1; i <= 20; i++)
            {
                var site = new ManagedCertificate()
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"test{i}.example.org",
                    ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS,
                    DateExpiry = DateTime.Now.AddDays(60 - 5 * i),
                    DateRenewed = DateTime.Now.AddDays(-15),
                    DateLastRenewalAttempt = DateTime.Now,
                    DateStart = DateTime.Now.AddMonths(-3),

                    RequestConfig = new CertRequestConfig()
                    {
                        Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                           new List<CertRequestChallengeConfig> {
                               new CertRequestChallengeConfig{ ChallengeType= SupportedChallengeTypes.CHALLENGE_TYPE_HTTP}
                           }
                           ),
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
                ManagedCertificates.Add(site);
            }

            MockDataStore = JsonConvert.SerializeObject(ManagedCertificates);
            foreach (var site in ManagedCertificates) site.IsChanged = false;
            ManagedCertificates = new ObservableCollection<ManagedCertificate>(ManagedCertificates);

            this.ProgressResults = new ObservableCollection<RequestProgressState>
            {
                new RequestProgressState( RequestState.Running, "This is a long message to test text overflow and wrapping", ManagedCertificates[0], false),
                new RequestProgressState( RequestState.Error, "This is another long message to test text overflow and wrapping", ManagedCertificates[1], false),
            };
        }

        private string MockDataStore;

        public void LoadSettings()
        {
            var mockSites = JsonConvert.DeserializeObject<List<ManagedCertificate>>(MockDataStore);
            foreach (var site in mockSites) site.IsChanged = false;
            ManagedCertificates = new ObservableCollection<ManagedCertificate>(mockSites);
            ImportedManagedCertificates = new ObservableCollection<ManagedCertificate>();
        }

        public override bool IsIISAvailable => true;
        public override Version IISVersion => new Version(10, 0);
        public override bool HasRegisteredContacts => true;
    }
}
