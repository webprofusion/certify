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
    public class ChallengeConfigMatchTests
    {

        [TestMethod, Description("Ensure correct challenge config selected based on domain")]
        public void MultiChallengeConfigMatch()
        {

            var managedCertificate = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "TestSite..",
                GroupId = "test",
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "test.com",
                    SubjectAlternativeNames = new string[]{
                        "*.fred.com",
                        "fred.com",
                        "www.fred.com",
                        "example.com",
                        "www.example.com",
                        "www.subdomain.example.com"
                    },
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                        new List<CertRequestChallengeConfig>
                        {
                            new CertRequestChallengeConfig{
                                ChallengeType="http-01",
                                DomainMatch= null,
                                ChallengeCredentialKey="config-default"
                            },
                            new CertRequestChallengeConfig{
                                ChallengeType="dns-01",
                                DomainMatch= "*.fred.com",
                                ChallengeCredentialKey="config-wildcard"
                            },
                             new CertRequestChallengeConfig{
                                ChallengeType="dns-01",
                                DomainMatch= "fred.com",
                                ChallengeCredentialKey="config2"
                            },
                            new CertRequestChallengeConfig{
                                ChallengeType="dns-01",
                                DomainMatch= "subdomain.example.com",
                                ChallengeCredentialKey="config3"
                            },
                              new CertRequestChallengeConfig{
                                ChallengeType="http-01",
                                DomainMatch= "example.com;www.exaomple.com;*.exaomple1.com",
                                ChallengeCredentialKey="config4"
                            },
                        }),
                    PerformAutomatedCertBinding = true,
                    WebsiteRootPath = "c:\\inetpub\\wwwroot",
                    DeploymentSiteOption = DeploymentOption.SingleSite
                },
                ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS
            };

            // Assert
            var configMatch = managedCertificate.GetChallengeConfig(null);
            Assert.AreEqual("config-default", configMatch.ChallengeCredentialKey, "Blank domain should match blank domain match config");


            configMatch = managedCertificate.GetChallengeConfig("*.fred.com");
            Assert.AreEqual("config-wildcard", configMatch.ChallengeCredentialKey, "Should match on wildcard");

            configMatch = managedCertificate.GetChallengeConfig("fred.com");
            Assert.AreEqual("config2", configMatch.ChallengeCredentialKey, "Should match on domain");

            configMatch = managedCertificate.GetChallengeConfig("subdomain.example.com");
            Assert.AreEqual("config3", configMatch.ChallengeCredentialKey, "Should match on domain");

            configMatch = managedCertificate.GetChallengeConfig("www.example.com");
            Assert.AreEqual("config-default", configMatch.ChallengeCredentialKey, "Should match default");

            configMatch = managedCertificate.GetChallengeConfig("www.exaomple.com");
            Assert.AreEqual("config4", configMatch.ChallengeCredentialKey, "Should match on domain");

            configMatch = managedCertificate.GetChallengeConfig("subdomain.exaomple1.com");
            Assert.AreEqual("config4", configMatch.ChallengeCredentialKey, "Should match on domain wildcard");

            configMatch = managedCertificate.GetChallengeConfig("www.subdomain.exaomple1.com");
            Assert.AreEqual("config-default", configMatch.ChallengeCredentialKey, "Should not match on domain wildcard");

            configMatch = managedCertificate.GetChallengeConfig("example.com");
            Assert.AreEqual("config4", configMatch.ChallengeCredentialKey, "Should match on domain");

            configMatch = managedCertificate.GetChallengeConfig("www.microsoft.com");
            Assert.AreEqual("config-default", configMatch.ChallengeCredentialKey, "Should match default");
        }
    }
}
