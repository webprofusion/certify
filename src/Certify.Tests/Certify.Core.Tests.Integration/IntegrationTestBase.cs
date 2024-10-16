using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Certify.Models;
using Certify.Models.Providers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Certify.Core.Tests
{
    public class IntegrationTestBase
    {
        public Dictionary<string, string> ConfigSettings = new Dictionary<string, string>();

        public string PrimaryTestDomain = "test.certifytheweb.com"; // TODO: get this from debug config as it changes per dev machine
        public string PrimaryWebRootPath = @"c:\inetpub\wwwroot\";

        private string _testConfigPath = @"c:\temp\Certify\TestConfigSettings.json";

        internal ILog _log;

        public IntegrationTestBase()
        {
            if (Environment.GetEnvironmentVariable("CERTIFY_TESTDOMAIN") != null)
            {
                PrimaryTestDomain = Environment.GetEnvironmentVariable("CERTIFY_TESTDOMAIN");
            }

            if (File.Exists("C:\\temp\\Certify\\TestConfigSettings.json"))
            {
                ConfigSettings = JsonConvert.DeserializeObject<Dictionary<string, string>>(System.IO.File.ReadAllText(_testConfigPath));
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Test config file not found: " + _testConfigPath);
            }

            _log = new Loggy(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<IntegrationTestBase>());
        }

        public ManagedCertificate GetMockManagedCertificate(string siteName, string testDomain, string siteId = null, string testPath = null)
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
                CertificatePath = Path.Combine(AppContext.BaseDirectory, "Assets\\dummycert.pfx")
            };

            return dummyManagedCertificate;

        }
    }
}
