using Certify.Models;
using Certify.Models.Providers;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;

namespace Certify.Core.Tests
{
    public class IntegrationTestBase
    {
        public string PrimaryTestDomain = "test.certifytheweb.com"; // TODO: get this from debug config as it changes per dev machine
        public string PrimaryIISRoot = @"c:\inetpub\wwwroot\";
        public Dictionary<string, string> ConfigSettings = new Dictionary<string, string>();
        internal ILog _log;

        public IntegrationTestBase()
        {
            if (Environment.GetEnvironmentVariable("CERTIFYSSLDOMAIN") != null)
            {
                PrimaryTestDomain = Environment.GetEnvironmentVariable("CERTIFYSSLDOMAIN");
            }

            /* ConfigSettings.Add("AWS_ZoneId", "example");
             ConfigSettings.Add("Azure_ZoneId", "example");
             ConfigSettings.Add("Cloudflare_ZoneId", "example");
             System.IO.File.WriteAllText("C:\\temp\\TestConfigSettings.json", JsonConvert.SerializeObject(ConfigSettings));
             */

            ConfigSettings = JsonConvert.DeserializeObject<Dictionary<string, string>>(System.IO.File.ReadAllText("C:\\temp\\Certify\\TestConfigSettings.json"));

            var logImp = new LoggerConfiguration()
           .WriteTo.Debug()
           .CreateLogger();

            _log = new Loggy(logImp);

        }

        public ManagedCertificate GetMockManagedCertificate(string siteName,string testDomain, string siteId =null, string testPath =null)
        {
            var dummyManagedCertificate = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = siteName,
                GroupId = siteId,
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = testDomain,
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    PerformChallengeFileCopy = true,
                    PerformExtensionlessConfigChecks = true,
                    WebsiteRootPath = testPath,
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>
                    {

                    },
                    DeploymentSiteOption = DeploymentOption.SingleSite
                },
                ItemType = ManagedCertificateType.SSL_ACME,
                CertificatePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)+"\\Assets\\dummycert.pfx"
            };

            return dummyManagedCertificate;

        }
    }
}
