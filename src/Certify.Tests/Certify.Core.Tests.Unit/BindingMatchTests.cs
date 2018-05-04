using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class BindingMatchTests
    {
        public List<BindingInfo> _allSites { get; set; }

        [TestInitialize]
        public void Setup()
        {
            _allSites = new List<BindingInfo>
            {
                // Site 1 : test.com and www.test.com bindings, both existing https
                new BindingInfo{ Name="TestDotCom", Host="test.com", IP="*", HasCertificate=true, Protocol="https", Port=443, Id="1"},
                new BindingInfo{ Name="TestDotCom", Host="www.test.com", IP="*", HasCertificate=true, Protocol="https", Port=443, Id="1"},

                // Site 1.1.: same top level as site 1, different subdomains
                new BindingInfo{ Name="TestDotCom", Host="ignore.test.com", IP="*", HasCertificate=true, Protocol="https", Port=443, Id="1.1"},
                new BindingInfo{ Name="TestDotCom", Host="www.ignore.test.com", IP="*", HasCertificate=true, Protocol="https", Port=443, Id="1.1"},

                // Site 2 : test.co.uk and www.test.co.uk bindings, no existing https
                new BindingInfo{ Name="Test.co.uk", Host="test.co.uk", IP="*", HasCertificate=true, Protocol="http", Port=80, Id="2"},
                new BindingInfo{ Name="Test.co.uk", Host="www.test.co.uk", IP="*", HasCertificate=true, Protocol="http", Port=80, Id="2"},

                // Site 3 : test.com.au and www.test.com.au bindings, http and existing https
                new BindingInfo{ Name="Test.com.au", Host="test.com.au", IP="*", HasCertificate=true, Protocol="https", Port=443, Id="3"},
                new BindingInfo{ Name="Test.com.au", Host="www.test.com.au", IP="*", HasCertificate=true, Protocol="http", Port=80, Id="3"},
                new BindingInfo{ Name="Test.com.au", Host="dev.www.test.com.au", IP="*", HasCertificate=true, Protocol="http", Port=80, Id="3"},

                // Site 4 : 1 one deeply nested subdomain and an alt domain, wilcard should mathch on
                // one item
                new BindingInfo{ Name="Test", Host="test.com.hk", IP="*", HasCertificate=true, Protocol="https", Port=443, Id="4"},
                new BindingInfo{ Name="Test", Host="dev.test.com", IP="*", HasCertificate=true, Protocol="http", Port=80, Id="4"},
                new BindingInfo{ Name="Test", Host="dev.sub.test.com", IP="*", HasCertificate=true, Protocol="http", Port=80, Id="4"}
            };
        }

        [TestMethod, Description("Ensure binding add/update decisions are correct based on deployment criteria")]
        public async Task BindingMatchTestsVarious()
        {
            /*
                Single Site - Default Match
                www.test.com
                test.com*/

            /*

            All Sites(*.test.com)
            All Sites(*.test.co.uk)
            */

            var managedCertificate = new ManagedCertificate

            {
                Id = Guid.NewGuid().ToString(),
                Name = "TestSite..",
                GroupId = "test",

                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "test.com",
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                        new List<CertRequestChallengeConfig>
                        {
                            new CertRequestChallengeConfig{
                                ChallengeType="dns-01"
                            }
                        }),
                    PerformAutomatedCertBinding = true,
                    WebsiteRootPath = "c:\\inetpub\\wwwroot"
                },
                ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS
            };

            var bindingManager = new BindingDeploymentManager();

            var deploymentTarget = new MockBindingDeploymentTarget();

            deploymentTarget.AllBindings = _allSites;

            managedCertificate.ServerSiteId = "ShouldNotMatch";
            var preview = await bindingManager.StoreAndDeployManagedCertificate(deploymentTarget, managedCertificate, null, false, isPreviewOnly: true);
            Assert.IsFalse(preview.Any(), " Should not match any bindings");

            managedCertificate.ServerSiteId = "1.1";
            preview = await bindingManager.StoreAndDeployManagedCertificate(deploymentTarget, managedCertificate, null, false, isPreviewOnly: true);
            Assert.IsFalse(preview.Any(), "Should not match any bindings (same domain, different sudomains no wildcard)");

            managedCertificate.ServerSiteId = "1";
            preview = await bindingManager.StoreAndDeployManagedCertificate(deploymentTarget, managedCertificate, null, false, isPreviewOnly: true);
            Assert.IsTrue(preview.Count == 1, "Should match one binding");

            managedCertificate.ServerSiteId = "1";
            managedCertificate.RequestConfig.PrimaryDomain = "*.test.com";
            preview = await bindingManager.StoreAndDeployManagedCertificate(deploymentTarget, managedCertificate, null, false, isPreviewOnly: true);
            Assert.IsTrue(preview.Count == 2, "Should match 2 bindings");

            managedCertificate.ServerSiteId = "1";
            managedCertificate.RequestConfig.DeploymentSiteOption = DeploymentOption.AllSites;
            managedCertificate.RequestConfig.PrimaryDomain = "test.com";
            preview = await bindingManager.StoreAndDeployManagedCertificate(deploymentTarget, managedCertificate, null, false, isPreviewOnly: true);
            Assert.IsTrue(preview.Count == 1, "Should match 1 binding");

            managedCertificate.ServerSiteId = "1";
            managedCertificate.RequestConfig.DeploymentSiteOption = DeploymentOption.AllSites;
            managedCertificate.RequestConfig.PrimaryDomain = "*.test.com";
            preview = await bindingManager.StoreAndDeployManagedCertificate(deploymentTarget, managedCertificate, null, false, isPreviewOnly: true);
            Assert.IsTrue(preview.Count == 2, "Should match 4 bindings across all sites");

            foreach (var a in preview)
            {
                System.Diagnostics.Debug.WriteLine(a.Description);
            }
        }

        [TestMethod, Description("Ensure domains match wildcards on first level matches")]
        public void WildcardMatches()
        {
            var domains = new List<string>
            {
                "test.com",
                "www.test.com",
                "fred.test.com",
                "dev.sub.test.com",
                "dev1.dev2.test.com",
                "test.co.uk",
                "test.com.au"
            };

            Assert.IsTrue(BindingDeploymentManager.IsDomainOrWildcardMatch(domains, "test.com"));

            Assert.IsFalse(BindingDeploymentManager.IsDomainOrWildcardMatch(domains, "fred.com"));

            Assert.IsFalse(BindingDeploymentManager.IsDomainOrWildcardMatch(domains, "*.fred.com"));

            Assert.IsTrue(BindingDeploymentManager.IsDomainOrWildcardMatch(domains, "*.test.com"));

            Assert.IsTrue(BindingDeploymentManager.IsDomainOrWildcardMatch(domains, "*.test.com.uk"));
        }
    }
}
