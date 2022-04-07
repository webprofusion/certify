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
                new BindingInfo{ SiteName="TestDotCom", Host="test.com", IP="*", HasCertificate=true, Protocol="https", Port=443, SiteId="1"},
                new BindingInfo{ SiteName="TestDotCom", Host="www.test.com", IP="*", HasCertificate=true, Protocol="https", Port=443, SiteId="1"},

                // Site 1.1.: same top level as site 1, different subdomains
                new BindingInfo{ SiteName="TestDotCom", Host="ignore.test.com", IP="*", HasCertificate=true, Protocol="https", Port=443, SiteId="1.1"},
                new BindingInfo{ SiteName="TestDotCom", Host="www.ignore.test.com", IP="*", HasCertificate=true, Protocol="https", Port=443, SiteId="1.1"},

                // Site 2 : test.co.uk and www.test.co.uk bindings, no existing https
                new BindingInfo{ SiteName="Test.co.uk", Host="test.co.uk", IP="*", HasCertificate=true, Protocol="http", Port=80, SiteId="2"},
                new BindingInfo{ SiteName="Test.co.uk", Host="www.test.co.uk", IP="*", HasCertificate=true, Protocol="http", Port=80, SiteId="2"},

                // Site 3 : test.com.au and www.test.com.au bindings, http and existing https
                new BindingInfo{ SiteName="Test.com.au", Host="test.com.au", IP="*", HasCertificate=true, Protocol="https", Port=443, SiteId="3"},
                new BindingInfo{ SiteName="Test.com.au", Host="www.test.com.au", IP="*", HasCertificate=true, Protocol="http", Port=80, SiteId="3"},
                new BindingInfo{ SiteName="Test.com.au", Host="dev.www.test.com.au", IP="*", HasCertificate=true, Protocol="http", Port=80, SiteId="3"},

                // Site 4 : 1 one deeply nested subdomain and an alt domain, wilcard should match on one item
                new BindingInfo{ SiteName="Test", Host="test.com.hk", IP="*", HasCertificate=true, Protocol="https", Port=443, SiteId="4"},
                new BindingInfo{ SiteName="Test", Host="dev.test.com", IP="*", HasCertificate=true, Protocol="http", Port=80, SiteId="4"},
                new BindingInfo{ SiteName="Test", Host="dev.sub.test.com", IP="*", HasCertificate=true, Protocol="http", Port=80, SiteId="4"},

                // Site 5 : alternative https port
                // one item
                new BindingInfo{ SiteName="TestAltPort", Host="altport.com", IP="*", HasCertificate=true, Protocol="https", Port=9000, SiteId="5"},
                new BindingInfo{ SiteName="TestAltPort", Host="altport.com", IP="*", HasCertificate=true, Protocol="https", Port=9001, SiteId="5"},

                // Site 6 : mixed with wildcard host match
                new BindingInfo{ SiteName="TestWild", Host="*.wildtest.com", IP="*", HasCertificate=true, Protocol="https", Port=9000, SiteId="6"},
                new BindingInfo{ SiteName="TestWild", Host="wildtest.com", IP="*", HasCertificate=true, Protocol="https", Port=9001, SiteId="6"},
                new BindingInfo{ SiteName="TestWild", Host="sub.wildtest.com", IP="*", HasCertificate=true, Protocol="https", Port=9001, SiteId="6"},
                new BindingInfo{ SiteName="TestWild", Host="subsub.*.wildtest.com", IP="*", HasCertificate=true, Protocol="https", Port=9001, SiteId="6"},
                new BindingInfo{ SiteName="TestWild", Host="subsub.sub.wildtest.com", IP="*", HasCertificate=true, Protocol="https", Port=9001, SiteId="6"},

                // Site 7:  cert will be used to match multiple ports with same hostname and one blank hostname
                new BindingInfo{ SiteName="TestMultiPorts", Host="admin.example.com", IP="*", HasCertificate=true, Protocol="https", Port=443, SiteId="7"},
                new BindingInfo{ SiteName="TestMultiPorts", Host="admin.example.com", IP="*", HasCertificate=true, Protocol="https", Port=8530, SiteId="7"},
                new BindingInfo{ SiteName="TestMultiPorts", Host="", IP="*", HasCertificate=true, Protocol="https", Port=8531, SiteId="7"}
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
                UseStagingMode = true,
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
                    WebsiteRootPath = "c:\\inetpub\\wwwroot",
                    DeploymentSiteOption = DeploymentOption.SingleSite
                },
                ItemType = ManagedCertificateType.SSL_ACME
            };

            var bindingManager = new BindingDeploymentManager();

            var deploymentTarget = new MockBindingDeploymentTarget();

            deploymentTarget.AllBindings = _allSites;

            var certStoreName = Certify.Management.CertificateManager.DEFAULT_STORE_NAME;

            var allBindings = await deploymentTarget.GetBindings(null);
            Assert.AreEqual(allBindings.Count, _allSites.Count, "Null target id should return all bindings on all target sites");
            allBindings = await deploymentTarget.GetBindings("1");
            Assert.AreEqual(allBindings.Count, 2, "Specific target id should return subset of all bindings");

            managedCertificate.ServerSiteId = "ShouldNotMatch";
            var preview = await bindingManager.StoreAndDeploy(deploymentTarget, managedCertificate, null, pfxPwd: "", isPreviewOnly: true, certStoreName: certStoreName);
            Assert.IsFalse(preview.Any(b => b.Category.EndsWith("Binding")), " Should not match any bindings");

            managedCertificate.ServerSiteId = "1.1";
            preview = await bindingManager.StoreAndDeploy(deploymentTarget, managedCertificate, null, pfxPwd: "", isPreviewOnly: true, certStoreName: certStoreName);
            Assert.IsFalse(preview.Any(b => b.Category.EndsWith("Binding")), "Should not match any bindings (same domain, different sudomains no wildcard)");

            managedCertificate.ServerSiteId = "1";
            preview = await bindingManager.StoreAndDeploy(deploymentTarget, managedCertificate, null, pfxPwd: "", isPreviewOnly: true, certStoreName: certStoreName);
            Assert.IsTrue(preview.Count(b => b.Category.EndsWith("Binding")) == 1, "Should match one binding");

            managedCertificate.ServerSiteId = "1";
            managedCertificate.RequestConfig.PrimaryDomain = "*.test.com";
            preview = await bindingManager.StoreAndDeploy(deploymentTarget, managedCertificate, null, pfxPwd: "", isPreviewOnly: true, certStoreName: certStoreName);
            Assert.IsTrue(preview.Count(b => b.Category.EndsWith("Binding")) == 1, "Should match 1 binding (root level domain should be ignored using wildcard)");

            managedCertificate.ServerSiteId = "1";
            managedCertificate.RequestConfig.DeploymentSiteOption = DeploymentOption.AllSites;
            managedCertificate.RequestConfig.PrimaryDomain = "test.com";
            preview = await bindingManager.StoreAndDeploy(deploymentTarget, managedCertificate, null, pfxPwd: "", isPreviewOnly: true, certStoreName: certStoreName);
            Assert.IsTrue(preview.Count(b => b.Category.EndsWith("Binding")) == 1, "Should match 1 binding");

            managedCertificate.ServerSiteId = "1";
            managedCertificate.RequestConfig.DeploymentSiteOption = DeploymentOption.AllSites;
            managedCertificate.RequestConfig.PrimaryDomain = "*.test.com";
            preview = await bindingManager.StoreAndDeploy(deploymentTarget, managedCertificate, null, pfxPwd: "", isPreviewOnly: true, certStoreName: certStoreName);
            Assert.IsTrue(preview.Count(b => b.Category.EndsWith("Binding")) == 3, "Should match 3 bindings across all sites");

            managedCertificate.ServerSiteId = "5";
            managedCertificate.RequestConfig.DeploymentSiteOption = DeploymentOption.AllSites;
            managedCertificate.RequestConfig.PrimaryDomain = "altport.com";

            preview = await bindingManager.StoreAndDeploy(deploymentTarget, managedCertificate, null, pfxPwd: "", isPreviewOnly: true, certStoreName: certStoreName);
            Assert.IsTrue(preview.Count(b => b.Category.EndsWith("Binding")) == 2, "Should match 2 bindings across all sites");
            Assert.IsTrue(preview.Count(b => b.Category == "Deployment.UpdateBinding" && b.Description.Contains(":9000")) == 1, "Should have 1 port 9000 binding");
            Assert.IsTrue(preview.Count(b => b.Category == "Deployment.UpdateBinding" && b.Description.Contains(":9001")) == 1, "Should have 1 port 9001 binding");

            managedCertificate.ServerSiteId = "6";
            managedCertificate.RequestConfig.DeploymentSiteOption = DeploymentOption.AllSites;
            managedCertificate.RequestConfig.PrimaryDomain = "*.wildtest.com";
            managedCertificate.RequestConfig.SubjectAlternativeNames = new string[] { "*.wildtest.com", "wildtest.com" };

            preview = await bindingManager.StoreAndDeploy(deploymentTarget, managedCertificate, null, pfxPwd: "", isPreviewOnly: true, certStoreName: certStoreName);
            Assert.IsTrue(preview.Count(b => b.Category.EndsWith("Binding")) == 3, "Should match 3 bindings across all sites");
            Assert.IsTrue(preview.Count(b => b.Category == "Deployment.UpdateBinding" && b.Description.Contains(":9000")) == 1, "Should have 1 port 9000 binding");
            Assert.IsTrue(preview.Count(b => b.Category == "Deployment.UpdateBinding" && b.Description.Contains(":9001")) == 2, "Should have 2 port 9001 bindings");

            managedCertificate.ServerSiteId = "7";
            managedCertificate.RequestConfig.DeploymentSiteOption = DeploymentOption.AllSites;
            managedCertificate.RequestConfig.DeploymentBindingBlankHostname = true;
            managedCertificate.RequestConfig.PrimaryDomain = "*.admin.example.com";
            managedCertificate.RequestConfig.SubjectAlternativeNames = new string[] { "*.admin.example.com", "admin.example.com" };

            preview = await bindingManager.StoreAndDeploy(deploymentTarget, managedCertificate, null, pfxPwd: "", isPreviewOnly: true, certStoreName: certStoreName);
            Assert.AreEqual(3, preview.Count(b => b.Category.EndsWith("Binding")), "Should match 3 bindings across all sites");
            Assert.IsTrue(preview.Count(b => b.Category == "Deployment.UpdateBinding" && b.Description.Contains(":443")) == 1, "Should have 1 port 443 binding");
            Assert.IsTrue(preview.Count(b => b.Category == "Deployment.UpdateBinding" && b.Description.Contains(":8530")) == 1, "Should have 1 port 8530 binding");
            Assert.IsTrue(preview.Count(b => b.Category == "Deployment.UpdateBinding" && b.Description.Contains(":8531")) == 1, "Should have 1 port 8531 binding");

            foreach (var a in preview)
            {
                System.Diagnostics.Debug.WriteLine(a.Description);
            }
        }

        [TestMethod, Description("Ensure domains match wildcards on first level matches and wildcard binding host")]
        public void WildcardMatchesWithHostnameWildcard()
        {
            var domains = new List<string>
            {
                "*.wildtest.com","wildtest.com"
            };

            Assert.IsFalse(ManagedCertificate.IsDomainOrWildcardMatch(domains, "test.com"));

            Assert.IsTrue(ManagedCertificate.IsDomainOrWildcardMatch(domains, "www.wildtest.com"));

            Assert.IsTrue(ManagedCertificate.IsDomainOrWildcardMatch(domains, "*.wildtest.com"));

            Assert.IsFalse(ManagedCertificate.IsDomainOrWildcardMatch(domains, "sub.*.wildtest.com"));

        }

        [TestMethod, Description("Ensure domains match wildcards on first level matches")]
        public void WildcardMatches()
        {
            var domains = new List<string>
            {
                "*.test.com",
                "*.test.co.uk"
            };

            Assert.IsFalse(ManagedCertificate.IsDomainOrWildcardMatch(domains, "test.com"));

            Assert.IsTrue(ManagedCertificate.IsDomainOrWildcardMatch(domains, "test.com", matchWildcardsToRootDomain: true));

            Assert.IsFalse(ManagedCertificate.IsDomainOrWildcardMatch(domains, "thisisatest.com"));

            Assert.IsFalse(ManagedCertificate.IsDomainOrWildcardMatch(domains, "www.thisisatest.com"));

            Assert.IsTrue(ManagedCertificate.IsDomainOrWildcardMatch(domains, "www.test.com"));

            Assert.IsFalse(ManagedCertificate.IsDomainOrWildcardMatch(domains, "www.subdomain.test.com"));

            Assert.IsFalse(ManagedCertificate.IsDomainOrWildcardMatch(domains, "fred.com"));

            Assert.IsFalse(ManagedCertificate.IsDomainOrWildcardMatch(domains, "*.fred.com"));

            Assert.IsTrue(ManagedCertificate.IsDomainOrWildcardMatch(domains, "*.test.com"));

            Assert.IsTrue(ManagedCertificate.IsDomainOrWildcardMatch(domains, "*.TEST.COM"));

            Assert.IsTrue(ManagedCertificate.IsDomainOrWildcardMatch(domains, "www.test.co.uk"));

            Assert.IsFalse(ManagedCertificate.IsDomainOrWildcardMatch(domains, "www.dev.test.co.uk"));
        }

        [TestMethod, Description("Detect if binding already exists")]
        public void ExistingBindingChecks()
        {
            var bindings = new List<BindingInfo> {
                new BindingInfo{ Host="test.com", IP="0.0.0.0", Port=443, Protocol="https" },
                new BindingInfo{ Host="www.test.com", IP="*", Port=80, Protocol="http" },
                new BindingInfo{ Host="UPPERCASE.TEST.COM", IP="*", Port=80, Protocol="http" },
                new BindingInfo{ Host="dev.test.com", IP="192.168.1.1", Port=80, Protocol="http" },
                new BindingInfo{ Host="ftp.test.com", IP="*", Port=20, Protocol="ftp" },
                new BindingInfo{ Host="", IP="192.168.1.1", Port=443, Protocol="https" },
                new BindingInfo{ Host="", IP="*", Port=443, Protocol="https" },
            };

            var spec = new BindingInfo
            {
                Host = "test.com",
                IP = "*",
                Port = 443,
                Protocol = "https"
            };
            Assert.IsTrue(BindingDeploymentManager.HasExistingBinding(bindings, spec));

            spec = new BindingInfo
            {
                Host = "www.test.com",
                IP = "*",
                Port = 443,
                Protocol = "https"
            };
            Assert.IsFalse(BindingDeploymentManager.HasExistingBinding(bindings, spec));

            spec = new BindingInfo
            {
                Host = "www.test.com",
                IP = "*",
                Port = 80,
                Protocol = "http"
            };
            Assert.IsTrue(BindingDeploymentManager.HasExistingBinding(bindings, spec));

            spec = new BindingInfo
            {
                Host = "UPPERCASE.TEST.COM",
                IP = "*",
                Port = 80,
                Protocol = "http"
            };
            Assert.IsTrue(BindingDeploymentManager.HasExistingBinding(bindings, spec));

            spec = new BindingInfo
            {
                Host = "dev.test.com",
                IP = "192.168.1.1",
                Port = 443,
                Protocol = "https"
            };
            Assert.IsFalse(BindingDeploymentManager.HasExistingBinding(bindings, spec));

            spec = new BindingInfo
            {
                Host = "dev.test.com",
                IP = "192.168.1.1",
                Port = 80,
                Protocol = "http"
            };
            Assert.IsTrue(BindingDeploymentManager.HasExistingBinding(bindings, spec));

            spec = new BindingInfo
            {
                Host = "ftp.test.com",
                IP = "*",
                Port = 20,
                Protocol = "ftp"
            };
            Assert.IsTrue(BindingDeploymentManager.HasExistingBinding(bindings, spec));

            spec = new BindingInfo
            {
                Host = null,
                IP = "0.0.0.0",
                Port = 443,
                Protocol = "https"
            };
            Assert.IsTrue(BindingDeploymentManager.HasExistingBinding(bindings, spec));
        }

        [TestMethod, Description("Test if mixed ipv4+ipv6 bindings are handled")]
        public async Task MixedIPBindingChecks()
        {
            var bindings = new List<BindingInfo> {
                new BindingInfo{ Host="test.com", IP="127.0.0.1", Port=80, Protocol="http" },
                new BindingInfo{ Host="test.com", IP="[fe80::3c4e:11b7:fe4f:c601%31]", Port=80, Protocol="http" },
                new BindingInfo{ Host="www.test.com", IP="127.0.0.1", Port=80, Protocol="http" },
                new BindingInfo{ Host="www.test.com", IP="[fe80::3c4e:11b7:fe4f:c601%31]", Port=80, Protocol="http" }
            };
            var deployment = new BindingDeploymentManager();
            var testManagedCert = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "MixedIPBindings",
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "test.com",
                    PerformAutomatedCertBinding = true,
                    DeploymentSiteOption = DeploymentOption.Auto,
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>
                        {
                            new CertRequestChallengeConfig{
                                ChallengeType= SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                                ChallengeProvider = "DNS01.API.Route53",
                                ChallengeCredentialKey = "ABC123"
                            }
                        }
                },
                ItemType = ManagedCertificateType.SSL_ACME
            };

            var mockTarget = new MockBindingDeploymentTarget();
            mockTarget.AllBindings = bindings;

            var results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, "test.pfx", pfxPwd: "", true, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);

            Assert.IsTrue(results.Any());
        }
    }
}
