using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Certify.Core.Management;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class BindingMatchTests
    {
        public List<BindingInfo> _allSites { get; set; }
        private readonly string _dummyCertPath = Path.Combine(Environment.CurrentDirectory, "Assets", "dummycert.pfx");

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
            Assert.AreEqual(3, results.Count());
            Assert.IsFalse(results[0].HasError, "This call to StoreAndDeploy() should have no errors storing certificate");
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.IsTrue(results[0].Description.Contains("Certificate will be stored in the computer certificate store"), $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Storage", results[0].Title);

            Assert.IsTrue(results[1].HasError, "This call to StoreAndDeploy() should have an error adding binding while deploying certificate in preview");
            Assert.AreEqual("Deployment.AddBinding", results[1].Category);
            Assert.IsTrue(results[1].Description.Contains("Add https binding |  | ***:443:test.com SNI** Failed to add/update binding. [IIS Site Id could not be determined]"), $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsTrue(results[2].HasError, "This call to StoreAndDeploy() should have an error adding binding while deploying certificate in preview");
            Assert.AreEqual("Deployment.AddBinding", results[2].Category);
            Assert.IsTrue(results[2].Description.Contains("Add https binding |  | ***:443:test.com SNI** Failed to add/update binding. [IIS Site Id could not be determined]"), $"Unexpected description: '{results[2].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[2].Title);
        }

        [TestMethod, Description("Test if mixed ipv4+ipv6 bindings are handled when not in preview")]
        public async Task MixedIPBindingChecksNoPreview()
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
                ItemType = ManagedCertificateType.SSL_ACME,
                CertificatePath = _dummyCertPath
            };

            var mockTarget = new MockBindingDeploymentTarget();
            mockTarget.AllBindings = bindings;

            var results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, _dummyCertPath, pfxPwd: "", false, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);

            Assert.IsTrue(results.Any());
            Assert.AreEqual(3, results.Count());
            Assert.IsFalse(results[0].HasError, "This call to StoreAndDeploy() should have no errors storing certificate");
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.IsTrue(results[0].Description.Contains("Certificate stored OK"), $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.AddBinding", results[1].Category);
            Assert.IsTrue(results[1].Description.Contains("Add https binding |  | ***:443:test.com SNI**"), $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.IsTrue(results[2].Description.Contains("Update https binding |  | **\\*:443:test.com SNI**"), $"Unexpected description: '{results[2].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[2].Title);
        }

        [TestMethod, Description("Test if mixed ipv4+ipv6 bindings are handled with blank certStoreName")]
        public async Task MixedIPBindingChecksBlankCertStoreName()
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
                ItemType = ManagedCertificateType.SSL_ACME,
                CertificatePath = _dummyCertPath
            };

            var mockTarget = new MockBindingDeploymentTarget();
            mockTarget.AllBindings = bindings;

            var results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, _dummyCertPath, pfxPwd: "", false, "");

            Assert.IsTrue(results.Any());
            Assert.AreEqual(3, results.Count());
            Assert.IsFalse(results[0].HasError, "This call to StoreAndDeploy() should have no errors storing certificate");
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.IsTrue(results[0].Description.Contains("Certificate stored OK"), $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.AddBinding", results[1].Category);
            Assert.IsTrue(results[1].Description.Contains("Add https binding |  | ***:443:test.com SNI**"), $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.IsTrue(results[2].Description.Contains("Update https binding |  | **\\*:443:test.com SNI**"), $"Unexpected description: '{results[2].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[2].Title);
        }

        [TestMethod, Description("Test if mixed ipv4+ipv6 bindings are handled when given a bad pfx file path")]
        public async Task MixedIPBindingChecksBadPfxPath()
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
                ItemType = ManagedCertificateType.SSL_ACME,
                CertificatePath = _dummyCertPath
            };

            var mockTarget = new MockBindingDeploymentTarget();
            mockTarget.AllBindings = bindings;

            await Assert.ThrowsExceptionAsync<System.IO.FileNotFoundException>(async () => await deployment.StoreAndDeploy(mockTarget, testManagedCert, _dummyCertPath.Replace("Assets", "Asset"), pfxPwd: "", false, Certify.Management.CertificateManager.DEFAULT_STORE_NAME));
        }

        [TestMethod, Description("Test if mixed ipv4+ipv6 bindings are handled when given a bad pfx file")]
        public async Task MixedIPBindingChecksBadPfxFile()
        {
            var bindings = new List<BindingInfo> {
                new BindingInfo{ Host="test.com", IP="127.0.0.1", Port=80, Protocol="http" },
                new BindingInfo{ Host="test.com", IP="[fe80::3c4e:11b7:fe4f:c601%31]", Port=80, Protocol="http" },
                new BindingInfo{ Host="www.test.com", IP="127.0.0.1", Port=80, Protocol="http" },
                new BindingInfo{ Host="www.test.com", IP="[fe80::3c4e:11b7:fe4f:c601%31]", Port=80, Protocol="http" }
            };
            var deployment = new BindingDeploymentManager();
            var badCertPath = Path.Combine(Environment.CurrentDirectory, "Assets", "badcert.pfx");
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
                ItemType = ManagedCertificateType.SSL_ACME,
                CertificatePath = _dummyCertPath
            };

            var mockTarget = new MockBindingDeploymentTarget();
            mockTarget.AllBindings = bindings;

            await Assert.ThrowsExceptionAsync<ArgumentException>(async () => await deployment.StoreAndDeploy(mockTarget, testManagedCert, badCertPath, pfxPwd: "", false, Certify.Management.CertificateManager.DEFAULT_STORE_NAME));
        }

        [TestMethod, Description("Test if mixed ipv4+ipv6 bindings are handled when given a bad pfx password")]
        public async Task MixedIPBindingChecksBadPfxPassword()
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
                ItemType = ManagedCertificateType.SSL_ACME,
                CertificatePath = _dummyCertPath
            };

            var mockTarget = new MockBindingDeploymentTarget();
            mockTarget.AllBindings = bindings;

            var results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, _dummyCertPath, pfxPwd: "badpass", false, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);

            Assert.IsTrue(results.Any());
            Assert.AreEqual(3, results.Count());
            Assert.IsFalse(results[0].HasError, "This call to StoreAndDeploy() should have no errors storing certificate");
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.IsTrue(results[0].Description.Contains("Certificate stored OK"), $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.AddBinding", results[1].Category);
            Assert.IsTrue(results[1].Description.Contains("Add https binding |  | ***:443:test.com SNI**"), $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.IsTrue(results[2].Description.Contains("Update https binding |  | **\\*:443:test.com SNI**"), $"Unexpected description: '{results[2].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[2].Title);
        }

        [TestMethod, Description("Test if mixed ipv4+ipv6 bindings are handled when given a bad cert store name")]
        public async Task MixedIPBindingChecksBadCertStoreName()
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
                ItemType = ManagedCertificateType.SSL_ACME,
                CertificatePath = _dummyCertPath
            };

            var mockTarget = new MockBindingDeploymentTarget();
            mockTarget.AllBindings = bindings;

            var results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, _dummyCertPath, pfxPwd: "", false, "BadCertStoreName");

            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].HasError);
            Assert.AreEqual("CertificateStorage", results[0].Category);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.IsTrue(results[0].Description.Contains("Error storing certificate. The system cannot find the file specified."), $"Unexpected description: '{results[0].Description}'");
            }
            else
            {
                Assert.IsTrue(results[0].Description.Contains("Error storing certificate. The specified X509 certificate store does not exist."), $"Unexpected description: '{results[0].Description}'");
            }

            Assert.AreEqual("Certificate Storage Failed", results[0].Title);
        }

        [TestMethod, Description("Test if mixed ipv4+ipv6 bindings are handled when DeploymentBindingOption = DeploymentBindingOption.UpdateOnly")]
        public async Task MixedIPBindingChecksRequestConfigUpdateOnly()
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
                    DeploymentSiteOption = DeploymentOption.AllSites,
                    DeploymentBindingOption = DeploymentBindingOption.UpdateOnly,
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>
                        {
                            new CertRequestChallengeConfig{
                                ChallengeType= SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                                ChallengeProvider = "DNS01.API.Route53",
                                ChallengeCredentialKey = "ABC123"
                            }
                        }
                },
                ItemType = ManagedCertificateType.SSL_ACME,
                CertificatePath = _dummyCertPath
            };

            var mockTarget = new MockBindingDeploymentTarget();
            mockTarget.AllBindings = bindings;

            var results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, _dummyCertPath, pfxPwd: "", false, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);

            Assert.AreEqual(1, results.Count);
            Assert.IsFalse(results[0].HasError);
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.IsTrue(results[0].Description.Contains("Certificate stored OK"), $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);
        }

        [TestMethod, Description("Test if https IP bindings are handled")]
        public async Task HttpsIPBindingChecks()
        {
            var bindings = new List<BindingInfo> {
                new BindingInfo{ Host="test.com", IP="127.0.0.1", Port=80, Protocol="https" },
                new BindingInfo{ Host="www.test.com", IP="127.0.0.1", Port=80, Protocol="https" },
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
            Assert.AreEqual(2, results.Count());
            Assert.IsFalse(results[0].HasError, "This call to StoreAndDeploy() should have no errors storing certificate");
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.IsTrue(results[0].Description.Contains("Certificate will be stored in the computer certificate store"), $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Storage", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[1].Category);
            Assert.IsTrue(results[1].Description.Contains("Update https binding |  | **127.0.0.1:80:test.com Non-SNI**"), $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);
        }

#if NET462
        [TestMethod, Description("Test if mixed ipv4+ipv6 bindings are handled when CertificateThumbprintHash is defined")]
        public async Task MixedIPBindingChecksCertificateThumbprintHash()
        {
            var cert = CertificateManager.GenerateSelfSignedCertificate("test.com", new DateTime(1934, 01, 01), new DateTime(1934, 03, 01));
            cert = CertificateManager.StoreCertificate(cert, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);

            var bindings = new List<BindingInfo> {
                new BindingInfo{ Host="test.com", IP="127.0.0.1", Port=80, Protocol="http", CertificateHash = cert.Thumbprint},
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
                ItemType = ManagedCertificateType.SSL_ACME,
                CertificateThumbprintHash = cert.Thumbprint,
                CertificatePath = _dummyCertPath
            };

            var mockTarget = new MockBindingDeploymentTarget();
            mockTarget.AllBindings = bindings;

            var results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, _dummyCertPath, pfxPwd: "", false, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);

            Assert.IsTrue(results.Any());
            Assert.AreEqual(3, results.Count());
            Assert.IsFalse(results[0].HasError, "This call to StoreAndDeploy() should have no errors storing certificate");
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.IsTrue(results[0].Description.Contains("Certificate stored OK"), $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.AddBinding", results[1].Category);
            Assert.IsTrue(results[1].Description.Contains("Add https binding |  | ***:443:test.com SNI**"), $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.IsTrue(results[2].Description.Contains("Update https binding |  | **\\*:443:test.com SNI**"), $"Unexpected description: '{results[2].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[2].Title);
        }

        [TestMethod, Description("Test if mixed ipv4+ipv6 bindings are handled when CertificatePreviousThumbprintHash is defined")]
        public async Task MixedIPBindingChecksCertificatePreviousThumbprintHash()
        {

            var cert = CertificateManager.GenerateSelfSignedCertificate("test.com", new DateTime(1934, 01, 01), new DateTime(1934, 03, 01));
            cert = CertificateManager.StoreCertificate(cert, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);

            var bindings = new List<BindingInfo> {
                new BindingInfo{ Host="test.com", IP="127.0.0.1", Port=80, Protocol="http", CertificateHash = cert.Thumbprint},
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
                ItemType = ManagedCertificateType.SSL_ACME,
                CertificatePreviousThumbprintHash = cert.Thumbprint,
                CertificatePath = _dummyCertPath
            };

            var mockTarget = new MockBindingDeploymentTarget();
            mockTarget.AllBindings = bindings;

            var results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, _dummyCertPath, pfxPwd: "", false, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);

            Assert.IsTrue(results.Any());
            Assert.AreEqual(3, results.Count());
            Assert.IsFalse(results[0].HasError, "This call to StoreAndDeploy() should have no errors storing certificate");
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.IsTrue(results[0].Description.Contains("Certificate stored OK"), $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.AddBinding", results[1].Category);
            Assert.IsTrue(results[1].Description.Contains("Add https binding |  | ***:443:test.com SNI**"), $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.IsTrue(results[2].Description.Contains("Update https binding |  | **\\*:443:test.com SNI**"), $"Unexpected description: '{results[2].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[2].Title);
        }
#endif

        [TestMethod, Description("Test if ftp bindings are handled when not in preview")]
        public async Task FtpBindingChecksNoPreview()
        {
            var bindings = new List<BindingInfo> {
                new BindingInfo{ Host="ftp.test.com", IP="*", Port = 20, Protocol="ftp", IsFtpSite=true },
                new BindingInfo{ Host="ftp.test.com", IP="127.0.0.1", Port = 20, Protocol="ftp", IsFtpSite=true },
            };
            var deployment = new BindingDeploymentManager();
            var testManagedCert = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "FtpBindings",
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "ftp.test.com",
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
                ItemType = ManagedCertificateType.SSL_ACME,
                CertificatePath = _dummyCertPath
            };

            var mockTarget = new MockBindingDeploymentTarget();
            mockTarget.AllBindings = bindings;

            var results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, _dummyCertPath, pfxPwd: "", false, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);

            Assert.IsTrue(results.Any());
            Assert.AreEqual(3, results.Count());
            Assert.IsFalse(results[0].HasError, "This call to StoreAndDeploy() should have no errors storing certificate");
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.IsTrue(results[0].Description.Contains("Certificate stored OK"), $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[1].Category);
            Assert.IsTrue(results[1].Description.Contains("Update ftp binding |  | ***:20:ftp.test.com**"), $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.IsTrue(results[2].Description.Contains("Update ftp binding |  | **127.0.0.1:20:ftp.test.com**"), $"Unexpected description: '{results[2].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[2].Title);
        }

        [TestMethod, Description("Test if ftp bindings are handled when not in preview with Certificate Request that defines a BindingIPAddress")]
        public async Task FtpBindingChecksCertReqBindingIPAddr()
        {
            var bindings = new List<BindingInfo> {
                new BindingInfo{ Host="ftp.test.com", IP="*", Port = 20, Protocol="ftp", IsFtpSite=true },
                new BindingInfo{ Host="ftp.test.com", IP="127.0.0.1", Port = 20, Protocol="ftp", IsFtpSite=true },
            };
            var deployment = new BindingDeploymentManager();
            var testManagedCert = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "FtpBindings",
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "ftp.test.com",
                    BindingIPAddress = "127.0.0.1",
                    PerformAutomatedCertBinding = false,
                    DeploymentSiteOption = DeploymentOption.AllSites,
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>
                        {
                            new CertRequestChallengeConfig{
                                ChallengeType= SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                                ChallengeProvider = "DNS01.API.Route53",
                                ChallengeCredentialKey = "ABC123"
                            }
                        }
                },
                ItemType = ManagedCertificateType.SSL_ACME,
                CertificatePath = _dummyCertPath
            };

            var mockTarget = new MockBindingDeploymentTarget();
            mockTarget.AllBindings = bindings;

            var results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, _dummyCertPath, pfxPwd: "", false, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);

            Assert.IsTrue(results.Any());
            Assert.AreEqual(3, results.Count());
            Assert.IsFalse(results[0].HasError, "This call to StoreAndDeploy() should have no errors storing certificate");
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.IsTrue(results[0].Description.Contains("Certificate stored OK"), $"Unexpected description: {results[0].Description}");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[1].Category);
            Assert.IsTrue(results[1].Description.Contains("Update ftp binding |  | ***:20:ftp.test.com**"), $"Unexpected description: {results[1].Description}");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.IsTrue(results[2].Description.Contains("Update ftp binding |  | **127.0.0.1:20:ftp.test.com**"), $"Unexpected description: {results[2].Description}");
            Assert.AreEqual("Install Certificate For Binding", results[2].Title);
        }

        [TestMethod, Description("Test if ftp bindings are handled when not in preview with Certificate Request that defines a BindingPort")]
        public async Task FtpBindingChecksCertReqBindingPort()
        {
            var bindings = new List<BindingInfo> {
                new BindingInfo{ Host="ftp.test.com", IP="*", Port = 20, Protocol="ftp", IsFtpSite=true },
                new BindingInfo{ Host="ftp.test.com", IP="127.0.0.1", Port = 20, Protocol="ftp", IsFtpSite=true },
            };
            var deployment = new BindingDeploymentManager();
            var testManagedCert = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "FtpBindings",
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "ftp.test.com",
                    BindingPort = "22",
                    PerformAutomatedCertBinding = false,
                    DeploymentSiteOption = DeploymentOption.AllSites,
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>
                        {
                            new CertRequestChallengeConfig{
                                ChallengeType= SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                                ChallengeProvider = "DNS01.API.Route53",
                                ChallengeCredentialKey = "ABC123"
                            }
                        }
                },
                ItemType = ManagedCertificateType.SSL_ACME,
                CertificatePath = _dummyCertPath
            };

            var mockTarget = new MockBindingDeploymentTarget();
            mockTarget.AllBindings = bindings;

            var results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, _dummyCertPath, pfxPwd: "", false, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);

            Assert.IsTrue(results.Any());
            Assert.AreEqual(3, results.Count());
            Assert.IsFalse(results[0].HasError, "This call to StoreAndDeploy() should have no errors storing certificate");
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.IsTrue(results[0].Description.Contains("Certificate stored OK"), $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[1].Category);
            Assert.IsTrue(results[1].Description.Contains("Update ftp binding |  | ***:20:ftp.test.com**"), $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.IsTrue(results[2].Description.Contains("Update ftp binding |  | **127.0.0.1:20:ftp.test.com**"), $"Unexpected description: '{results[2].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[2].Title);
        }

        [TestMethod, Description("Test update bindings are skipped when using a protocol other than http, https, or ftp")]
        public async Task UpdateBindingSkippedUnsupportedProtocol()
        {
            var bindings = new List<BindingInfo> {
                new BindingInfo{ Host="smtp.test.com", IP="127.0.0.1", Port = 587, Protocol="smtp" },
            };
            var deployment = new BindingDeploymentManager();
            var testManagedCert = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "SmtpBindings",
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "smtp.test.com",
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
                ItemType = ManagedCertificateType.SSL_ACME,
                CertificatePath = _dummyCertPath
            };

            var mockTarget = new MockBindingDeploymentTarget();
            mockTarget.AllBindings = bindings;

            var results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, _dummyCertPath, pfxPwd: "", false, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);

            Assert.IsTrue(results.Any());
            Assert.AreEqual(1, results.Count());
            Assert.IsFalse(results[0].HasError, "This call to StoreAndDeploy() should have no errors storing certificate");
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.IsTrue(results[0].Description.Contains("Certificate stored OK"), $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);
        }

        [TestMethod, Description("Test if ftp bindings are handled when not in preview")]
        public async Task FtpBindingChecksUpdateExisting()
        {
            var bindings = new List<BindingInfo> {
                new BindingInfo{ Host="ftp.test.com", IP="*", Port = 21, Protocol="ftp", IsFtpSite=true },
                new BindingInfo{ Host="ftp.test.com", IP="127.0.0.1", Port = 21, Protocol="ftp", IsFtpSite=true },
            };
            var deployment = new BindingDeploymentManager();
            var testManagedCert = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "FtpBindings",
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "ftp.test.com",
                    PerformAutomatedCertBinding = true,
                    //DeploymentSiteOption = DeploymentOption.SingleSite,
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>
                        {
                            new CertRequestChallengeConfig{
                                ChallengeType= SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                                ChallengeProvider = "DNS01.API.Route53",
                                ChallengeCredentialKey = "ABC123"
                            }
                        }
                },
                ItemType = ManagedCertificateType.SSL_ACME,
                CertificatePath = _dummyCertPath
            };

            var mockTarget = new MockBindingDeploymentTarget();
            mockTarget.AllBindings = bindings;

            var results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, _dummyCertPath, pfxPwd: "", false, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);

            Assert.IsTrue(results.Any());
            Assert.AreEqual(1, results.Count());
            Assert.IsFalse(results[0].HasError, "This call to StoreAndDeploy() should have no errors storing certificate");
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.IsTrue(results[0].Description.Contains("Certificate stored OK"), $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            testManagedCert.RequestConfig.DeploymentSiteOption = DeploymentOption.AllSites;
            results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, _dummyCertPath, pfxPwd: "", false, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);

            Assert.IsTrue(results.Any());
            Assert.AreEqual(3, results.Count());
            Assert.IsFalse(results[0].HasError, "This call to StoreAndDeploy() should have no errors storing certificate");
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.IsTrue(results[0].Description.Contains("Certificate stored OK"), $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[1].Category);
            Assert.IsTrue(results[1].Description.Contains("Update ftp binding |  | ***:21:ftp.test.com**"), $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.IsTrue(results[2].Description.Contains("Update ftp binding |  | **127.0.0.1:21:ftp.test.com**"), $"Unexpected description: '{results[2].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[2].Title);
        }

        [TestMethod, Description("Test that duplicate https bindings are not created when multiple non-port 443 same-hostname bindings exist")]
        [Ignore("Currently not supported")]
        public async Task MixedPortMixedSiteTest()
        {

            var bindings = new List<BindingInfo> {
                new BindingInfo{  Host="example.test.com", IP="*", Port=80, Protocol="http", SiteId="1"}, // an http site setup just for redirects
                new BindingInfo{  Host="example.test.com", IP="*", Port=81, Protocol="http", SiteId="2"}, // an http site setup as demo
                new BindingInfo{  Host="example.test.com", IP="*", Port=443, Protocol="https", SiteId="3"}, // the real https site
            };

            var deployment = new BindingDeploymentManager();
            var testManagedCert = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "MixedBindings",
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "example.test.com",
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

            // this test will currently fail because we are not looking at all sites to prevent duplicate bindings
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod, Description("Test that existing IP specific https bindings is preserved")]

        public async Task TestIPSpecific_ExistingHttpsBinding()
        {

            var bindings = new List<BindingInfo> {
                new BindingInfo{  Host="ipspecific.test.com", IP="127.0.0.1", Port=80, Protocol="http", SiteId="2"},
                new BindingInfo{  Host="ipspecific.test.com", IP="127.0.0.1", Port=443, IsSNIEnabled=true, Protocol="https",  SiteId="2"},
                new BindingInfo{  Host="ipspecific2.test.com", IP="127.0.0.1", Port=80, Protocol="http", SiteId="2"},
                new BindingInfo{  Host="", IP="127.0.0.1", Port=80, Protocol="http", SiteId="2"},
                new BindingInfo{  Host="nonipspecific.test.com", IP="*", Port=80, Protocol="http", SiteId="2"},
                new BindingInfo{  Host="nonipspecific2.test.com", IP="*", Port=443, IsSNIEnabled=true, Protocol="https", SiteId="2"},
                new BindingInfo{  Host="nonipspecific3.test.com", IP="*", Port=443, IsSNIEnabled=false, Protocol="https", SiteId="2"},
            };

            var deployment = new BindingDeploymentManager();
            var testManagedCert = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "IPSpecificBindings",
                UseStagingMode = true,
                ServerSiteId = "2", //target for single site deployment
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "ipspecific.test.com",
                    SubjectAlternativeNames = new string[] { "ipspecific.test.com", "ipspecific2.test.com", "nonipspecific.test.com", "nonipspecific2.test.com", "nonipspecific3.test.com" },
                    PerformAutomatedCertBinding = true,
                    DeploymentSiteOption = DeploymentOption.SingleSite,
                    
                    DeploymentBindingBlankHostname = true,
                    BindingIPAddress = "127.0.0.1",
                    BindingPort = "443",
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

            var results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, "test.pfx", pfxPwd: "", true, Certify.Management.CertificateManager.WEBHOSTING_STORE_NAME);

            Assert.AreEqual(6, results.Count(r=>r.ObjectResult is BindingInfo));

            // existing IP specific https binding should be preserved
            var bindingInfo = results.Last(r => (r.ObjectResult as BindingInfo)?.Host == "ipspecific.test.com")?.ObjectResult as BindingInfo;
            Assert.IsTrue(BindingDeploymentManager.AreBindingsEquivalent(bindingInfo, new BindingInfo { Host = "ipspecific.test.com", IP = "127.0.0.1", IsSNIEnabled = true, Port = 443, Protocol = "https" }), "IP should be preserved on existing https binding");

            // new https binding should not be IP specific when SNI can be used
            bindingInfo = results.Last(r => (r.ObjectResult as BindingInfo)?.Host == "ipspecific2.test.com")?.ObjectResult as BindingInfo;
            Assert.IsTrue(BindingDeploymentManager.AreBindingsEquivalent(bindingInfo, new BindingInfo { Host = "ipspecific2.test.com", IP = "*", IsSNIEnabled = true, Port = 443, Protocol = "https" }), "IP should not be preserved on new https binding converted from IP specific http binding where hostname and SNI are applicable");

            // new https binding blank hostname, IP specific, non-sni
            bindingInfo = results.Last(r => (r.ObjectResult as BindingInfo)?.Host == "")?.ObjectResult as BindingInfo;
            Assert.IsTrue(BindingDeploymentManager.AreBindingsEquivalent(bindingInfo, new BindingInfo { Host = "", IP = "127.0.0.1", IsSNIEnabled = false, Port = 443, Protocol = "https" }), "IP should be preserved on new https binding converted from IP specific http binding where hostname and SNI are not applicable");

            // new https binding with hostname, IP unassigned, sni
            bindingInfo = results.Last(r => (r.ObjectResult as BindingInfo)?.Host == "nonipspecific.test.com")?.ObjectResult as BindingInfo;
            Assert.IsTrue(BindingDeploymentManager.AreBindingsEquivalent(bindingInfo, new BindingInfo { Host = "nonipspecific.test.com", IP = "*", IsSNIEnabled = true, Port = 443, Protocol = "https" }), "Standard http binding conversion, IP unassigned, hostname set, sni enabled");

            // existing https binding with hostname, IP unassigned, sni
            bindingInfo = results.Last(r => (r.ObjectResult as BindingInfo)?.Host == "nonipspecific2.test.com")?.ObjectResult as BindingInfo;
            Assert.IsTrue(BindingDeploymentManager.AreBindingsEquivalent(bindingInfo, new BindingInfo { Host = "nonipspecific2.test.com", IP = null, IsSNIEnabled = true, Port = 443, Protocol = "https" }), "Should be equivalent");

            // existing https binding with hostname, IP unassigned, sni not enabled on original binding
            bindingInfo = results.Last(r => (r.ObjectResult as BindingInfo)?.Host == "nonipspecific3.test.com")?.ObjectResult as BindingInfo;
            Assert.IsTrue(BindingDeploymentManager.AreBindingsEquivalent(bindingInfo, new BindingInfo { Host = "nonipspecific3.test.com", IP = "*", IsSNIEnabled = false, Port = 443, Protocol = "https" }), "Should be equivalent");

        }

        [TestMethod, Description("Test that new IP specific https bindings is created as All Unassigned")]
        public async Task TestIPSpecific_NewHttpsBinding()
        {

            var bindings = new List<BindingInfo> {
                new BindingInfo{  Host="ipspecific.test.com", IP="127.0.0.1", Port=80, Protocol="http", SiteId="2"}
            };

            var deployment = new BindingDeploymentManager();
            var testManagedCert = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "IPSpecificBindings",
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "ipspecific.test.com",
                    PerformAutomatedCertBinding = true,
                    DeploymentSiteOption = DeploymentOption.Auto,
                    BindingIPAddress = "127.0.0.1",
                    BindingPort = "443",
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

            Assert.AreEqual(2, results.Count);

            var bindingResult = results.Last();
            var bindingInfo = bindingResult.ObjectResult as BindingInfo;
            Assert.IsNotNull(bindingInfo);
            Assert.IsTrue(bindingInfo.IsSNIEnabled, "SNI should be enabled on the target binding");
            Assert.AreEqual("*", bindingInfo.IP, "IP should be All Unassigned * instead of a specific IP");
            Assert.AreEqual("ipspecific.test.com", bindingInfo.Host);

        }
    }
}
