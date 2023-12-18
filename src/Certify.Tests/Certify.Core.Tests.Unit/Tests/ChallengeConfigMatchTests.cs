using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
                                DomainMatch= "example.com;www.exaomple.com, *.exaomple1.com ", ///should allow edither semicolon or comma delimiters, spaces trimmed
                                ChallengeCredentialKey="config4"
                            },
                        }),
                    PerformAutomatedCertBinding = true,
                    WebsiteRootPath = "c:\\inetpub\\wwwroot",
                    DeploymentSiteOption = DeploymentOption.SingleSite
                },
                ItemType = ManagedCertificateType.SSL_ACME
            };

            // Assert
            var configMatch = managedCertificate.GetChallengeConfig(null);
            Assert.AreEqual("config-default", configMatch.ChallengeCredentialKey, "Blank domain should match blank domain match config");

            configMatch = managedCertificate.GetChallengeConfig(new CertIdentifierItem(CertIdentifierType.Dns, null));
            Assert.AreEqual("config-default", configMatch.ChallengeCredentialKey, "Blank domain should match blank domain match config");

            configMatch = managedCertificate.GetChallengeConfig(new CertIdentifierItem(CertIdentifierType.Dns, "*.fred.com"));
            Assert.AreEqual("config-wildcard", configMatch.ChallengeCredentialKey, "Should match on wildcard");

            configMatch = managedCertificate.GetChallengeConfig(new CertIdentifierItem(CertIdentifierType.Dns, "fred.com"));
            Assert.AreEqual("config2", configMatch.ChallengeCredentialKey, "Should match on domain");

            configMatch = managedCertificate.GetChallengeConfig(new CertIdentifierItem(CertIdentifierType.Dns, "subdomain.example.com"));
            Assert.AreEqual("config3", configMatch.ChallengeCredentialKey, "Should match on domain");

            configMatch = managedCertificate.GetChallengeConfig(new CertIdentifierItem(CertIdentifierType.Dns, "www.example.com"));
            Assert.AreEqual("config-default", configMatch.ChallengeCredentialKey, "Should match default");

            configMatch = managedCertificate.GetChallengeConfig(new CertIdentifierItem(CertIdentifierType.Dns, "www.exaomple.com"));
            Assert.AreEqual("config4", configMatch.ChallengeCredentialKey, "Should match on domain");

            configMatch = managedCertificate.GetChallengeConfig(new CertIdentifierItem(CertIdentifierType.Dns, "subdomain.exaomple1.com"));
            Assert.AreEqual("config4", configMatch.ChallengeCredentialKey, "Should match on domain wildcard");

            configMatch = managedCertificate.GetChallengeConfig(new CertIdentifierItem(CertIdentifierType.Dns, "www.subdomain.exaomple1.com"));
            Assert.AreEqual("config-default", configMatch.ChallengeCredentialKey, "Should not match on domain wildcard");

            configMatch = managedCertificate.GetChallengeConfig(new CertIdentifierItem(CertIdentifierType.Dns, "example.com"));
            Assert.AreEqual("config4", configMatch.ChallengeCredentialKey, "Should match on domain");

            configMatch = managedCertificate.GetChallengeConfig(new CertIdentifierItem(CertIdentifierType.Dns, "www.microsoft.com"));
            Assert.AreEqual("config-default", configMatch.ChallengeCredentialKey, "Should match default");
        }

        [TestMethod, Description("Ensure correct challenge config selected based on domain")]
        public void ChallengeDelegationRuleTests()
        {
            // wildcard rule tests [any subdomain source, any subdomain target]
            var testRule = "*.test.com:*.auth.test.co.uk";
            var result = Management.Challenges.DnsChallengeHelper.ApplyChallengeDelegationRule("test.com", "_acme-challenge.test.com", testRule);
            Assert.AreEqual("_acme-challenge.auth.test.co.uk", result);

            result = Management.Challenges.DnsChallengeHelper.ApplyChallengeDelegationRule("www.test.com", "_acme-challenge.www.test.com", testRule);
            Assert.AreEqual("_acme-challenge.www.auth.test.co.uk", result);

            result = Management.Challenges.DnsChallengeHelper.ApplyChallengeDelegationRule("www.subdomain.test.com", "_acme-challenge.www.subdomain.test.com", testRule);
            Assert.AreEqual("_acme-challenge.www.subdomain.auth.test.co.uk", result);

            // non-wildcard rule tests [specific subdomain source matches specific subdomain target]

            testRule = "test.com:auth.test.co.uk";

            result = Management.Challenges.DnsChallengeHelper.ApplyChallengeDelegationRule("test.com", "_acme-challenge.test.com", testRule);
            Assert.AreEqual("_acme-challenge.auth.test.co.uk", result);

            result = Management.Challenges.DnsChallengeHelper.ApplyChallengeDelegationRule("www.test.com", "_acme-challenge.www.test.com", testRule);
            Assert.AreEqual("_acme-challenge.www.test.com", result);

            // wildcard source matched to specific subdomain target
            testRule = "*.test.com : auth.test.co.uk ";

            result = Management.Challenges.DnsChallengeHelper.ApplyChallengeDelegationRule("www.test.com", "_acme-challenge.www.test.com", testRule);
            Assert.AreEqual("_acme-challenge.auth.test.co.uk", result);

            result = Management.Challenges.DnsChallengeHelper.ApplyChallengeDelegationRule("www.subdomain.test.com", "_acme-challenge.www.subdomain.test.com", testRule);
            Assert.AreEqual("_acme-challenge.auth.test.co.uk", result);

            // multi rule tests (first matching rule is used)
            testRule = "*.test.com:*.auth.test.co.uk;*.example.com:*.auth.example.co.uk";

            result = Management.Challenges.DnsChallengeHelper.ApplyChallengeDelegationRule("www.subdomain.example.com", "_acme-challenge.www.subdomain.example.com", testRule);
            Assert.AreEqual("_acme-challenge.www.subdomain.auth.example.co.uk", result);
        }

        [TestMethod, Description("Ensure correct challenge config selected when rule is blank")]
        public void ChallengeDelegationRuleBlankRule()
        {
            var result = Management.Challenges.DnsChallengeHelper.ApplyChallengeDelegationRule("test.com", "_acme-challenge.test.com", null);
            Assert.AreEqual("_acme-challenge.test.com", result);
        }
    }
}
