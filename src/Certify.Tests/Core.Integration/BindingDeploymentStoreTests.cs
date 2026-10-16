using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests
{
    /// <summary>
    /// Binding deployment which stores the certificate in the local machine certificate store, so on Windows it needs an
    /// elevated process. Deployment decisions which only preview are covered by the unit tests.
    /// </summary>
    [TestClass]
    public class BindingDeploymentStoreTests
    {
        private readonly string _dummyCertPath = Path.Combine(Environment.CurrentDirectory, "Assets", "dummycert.pfx");

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
            Assert.Contains("Certificate stored OK", results[0].Description, $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.AddBinding", results[1].Category);
            Assert.Contains("Add https binding |  | ***:443:test.com SNI**", results[1].Description, $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.Contains("Update https binding |  | **\\*:443:test.com SNI**", results[2].Description, $"Unexpected description: '{results[2].Description}'");
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
            Assert.Contains("Certificate stored OK", results[0].Description, $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.AddBinding", results[1].Category);
            Assert.Contains("Add https binding |  | ***:443:test.com SNI**", results[1].Description, $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.Contains("Update https binding |  | **\\*:443:test.com SNI**", results[2].Description, $"Unexpected description: '{results[2].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[2].Title);
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
            Assert.Contains("Certificate stored OK", results[0].Description, $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.AddBinding", results[1].Category);
            Assert.Contains("Add https binding |  | ***:443:test.com SNI**", results[1].Description, $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.Contains("Update https binding |  | **\\*:443:test.com SNI**", results[2].Description, $"Unexpected description: '{results[2].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[2].Title);
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

            Assert.HasCount(1, results);
            Assert.IsFalse(results[0].HasError);
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.Contains("Certificate stored OK", results[0].Description, $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);
        }

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
            Assert.Contains("Certificate stored OK", results[0].Description, $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.AddBinding", results[1].Category);
            Assert.Contains("Add https binding |  | ***:443:test.com SNI**", results[1].Description, $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.Contains("Update https binding |  | **\\*:443:test.com SNI**", results[2].Description, $"Unexpected description: '{results[2].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[2].Title);

            CertificateManager.RemoveCertificate(cert, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);
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
            Assert.Contains("Certificate stored OK", results[0].Description, $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.AddBinding", results[1].Category);
            Assert.Contains("Add https binding |  | ***:443:test.com SNI**", results[1].Description, $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.Contains("Update https binding |  | **\\*:443:test.com SNI**", results[2].Description, $"Unexpected description: '{results[2].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[2].Title);

            CertificateManager.RemoveCertificate(cert, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);
        }

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
            Assert.Contains("Certificate stored OK", results[0].Description, $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[1].Category);
            Assert.Contains("Update ftp binding |  | ***:20:ftp.test.com**", results[1].Description, $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.Contains("Update ftp binding |  | **127.0.0.1:20:ftp.test.com**", results[2].Description, $"Unexpected description: '{results[2].Description}'");
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
            Assert.Contains("Certificate stored OK", results[0].Description, $"Unexpected description: {results[0].Description}");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[1].Category);
            Assert.Contains("Update ftp binding |  | ***:20:ftp.test.com**", results[1].Description, $"Unexpected description: {results[1].Description}");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.Contains("Update ftp binding |  | **127.0.0.1:20:ftp.test.com**", results[2].Description, $"Unexpected description: {results[2].Description}");
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
            Assert.Contains("Certificate stored OK", results[0].Description, $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[1].Category);
            Assert.Contains("Update ftp binding |  | ***:20:ftp.test.com**", results[1].Description, $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.Contains("Update ftp binding |  | **127.0.0.1:20:ftp.test.com**", results[2].Description, $"Unexpected description: '{results[2].Description}'");
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
            Assert.Contains("Certificate stored OK", results[0].Description, $"Unexpected description: '{results[0].Description}'");
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
            Assert.Contains("Certificate stored OK", results[0].Description, $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            testManagedCert.RequestConfig.DeploymentSiteOption = DeploymentOption.AllSites;
            results = await deployment.StoreAndDeploy(mockTarget, testManagedCert, _dummyCertPath, pfxPwd: "", false, Certify.Management.CertificateManager.DEFAULT_STORE_NAME);

            Assert.IsTrue(results.Any());
            Assert.AreEqual(3, results.Count());
            Assert.IsFalse(results[0].HasError, "This call to StoreAndDeploy() should have no errors storing certificate");
            Assert.AreEqual("CertificateStorage", results[0].Category);
            Assert.Contains("Certificate stored OK", results[0].Description, $"Unexpected description: '{results[0].Description}'");
            Assert.AreEqual("Certificate Stored", results[0].Title);

            Assert.IsFalse(results[1].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[1].Category);
            Assert.Contains("Update ftp binding |  | ***:21:ftp.test.com**", results[1].Description, $"Unexpected description: '{results[1].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[1].Title);

            Assert.IsFalse(results[2].HasError, "This call to StoreAndDeploy() should not have an error adding binding while deploying certificate");
            Assert.AreEqual("Deployment.UpdateBinding", results[2].Category);
            Assert.Contains("Update ftp binding |  | **127.0.0.1:21:ftp.test.com**", results[2].Description, $"Unexpected description: '{results[2].Description}'");
            Assert.AreEqual("Install Certificate For Binding", results[2].Title);
        }
    }
}
