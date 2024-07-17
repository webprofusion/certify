using Certify.Models;
using Certify.Models.Shared.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class CertificateEditorServiceTests
    {

        [TestMethod, Description("Test primary domain required")]
        public void TestPrimaryDomainRequired()
        {

            var item = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption>
                {
                    new DomainOption { Domain = "test.com", IsPrimaryDomain=false, IsSelected=true },
                    new DomainOption { Domain = "www.test.com", IsPrimaryDomain=false, IsSelected=true }
                },
                RequestConfig = new CertRequestConfig
                {
                    Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>
                    {
                        new CertRequestChallengeConfig
                        {
                            ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP
                        }
                    },
                    SubjectAlternativeNames = new[] { "test.com", "www.test.com" }
                }
            };

            // skip auto config to that primary domain is not auto selected
            var validationResult = CertificateEditorService.Validate(item, null, null, false);

            Assert.IsNotNull(validationResult);
            Assert.IsFalse(validationResult.IsValid);
            Assert.AreEqual(ValidationErrorCodes.PRIMARY_IDENTIFIER_REQUIRED.ToString(), validationResult.ErrorCode);
        }

        [TestMethod, Description("Test primary domain too many")]
        public void TestPrimaryDomainTooMany()
        {

            var item = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption>
                {
                    new DomainOption { Domain = "test.com", IsPrimaryDomain=true, IsSelected=true },
                    new DomainOption { Domain = "www.test.com", IsPrimaryDomain=true, IsSelected=true }
                },
                RequestConfig = new CertRequestConfig
                {
                    Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>
                  {
                      new CertRequestChallengeConfig
                      {
                          ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP
                      }
                  }
                }
            };

            var validationResult = CertificateEditorService.Validate(item, null, null, true);

            Assert.IsNotNull(validationResult);
            Assert.IsFalse(validationResult.IsValid);
            Assert.AreEqual(ValidationErrorCodes.PRIMARY_IDENTIFIER_TOOMANY.ToString(), validationResult.ErrorCode);
        }

        [TestMethod, Description("Test mixed wildcard label validation")]
        public void TestMixedWildcardLabels()
        {

            var item = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption>
                {
                    new DomainOption { Domain = "test.com", IsPrimaryDomain=true, IsSelected=true },
                    new DomainOption { Domain = "www.test.com", IsPrimaryDomain=false,IsSelected=true },
                    new DomainOption { Domain = "*.test.com", IsPrimaryDomain=false,IsSelected=true }
                },
                RequestConfig = new CertRequestConfig
                {
                    Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>
                  {
                      new CertRequestChallengeConfig
                      {
                          ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS
                      }
                  }
                }
            };

            var validationResult = CertificateEditorService.Validate(item, null, null, true);

            Assert.IsNotNull(validationResult);
            Assert.IsFalse(validationResult.IsValid);
            Assert.AreEqual(ValidationErrorCodes.MIXED_WILDCARD_WITH_LABELS.ToString(), validationResult.ErrorCode);
        }

        [TestMethod, Description("Test mixed wildcard subdomain-like name allowed")]
        public void TestMixedWildcardSubdomainLabels()
        {
            // in this example *.test.com and *.vs-test.com should be allowed as they are distinct
            var item = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption>
                {
                    new DomainOption { Domain = "test.com", IsPrimaryDomain=true, IsSelected=true },
                    new DomainOption { Domain = "vs-test.com", IsPrimaryDomain=false, IsSelected=true },
                    new DomainOption { Domain = "*.test.com", IsPrimaryDomain=false,IsSelected=true },
                    new DomainOption { Domain = "*.vs-test.com", IsPrimaryDomain=false,IsSelected=true }
                },
                RequestConfig = new CertRequestConfig
                {
                    Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>
                  {
                      new CertRequestChallengeConfig
                      {
                          ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                          ChallengeProvider = "DNS01.API.Route53"
                      }
                  }
                }
            };

            var validationResult = CertificateEditorService.Validate(item, null, null, true);

            Assert.IsNotNull(validationResult);
            Assert.IsTrue(validationResult.IsValid);

        }

        [TestMethod, Description("Test mixed wildcard subdomain-like with invalid subdomain label")]
        public void TestMixedWildcardSubdomainWithInvalidLabels()
        {
            // in this example *.test.com and *.vs-test.com should be allowed as they are distinct
            var item = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption>
                {
                    new DomainOption { Domain = "test.com", IsPrimaryDomain=true, IsSelected=true },
                    new DomainOption { Domain = "vs-test.com", IsPrimaryDomain=false, IsSelected=true },
                    new DomainOption { Domain = "*.test.com", IsPrimaryDomain=false,IsSelected=true },
                    new DomainOption { Domain = "*.vs-test.com", IsPrimaryDomain=false,IsSelected=true },
                    new DomainOption { Domain = "www.vs-test.com", IsPrimaryDomain=false,IsSelected=true }
                },
                RequestConfig = new CertRequestConfig
                {
                    Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>
                  {
                      new CertRequestChallengeConfig
                      {
                          ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                          ChallengeProvider = "DNS01.API.Route53"
                      }
                  }
                }
            };

            var validationResult = CertificateEditorService.Validate(item, null, null, true);

            Assert.IsNotNull(validationResult);
            Assert.IsFalse(validationResult.IsValid);
            Assert.AreEqual(ValidationErrorCodes.MIXED_WILDCARD_WITH_LABELS.ToString(), validationResult.ErrorCode);

        }

        [TestMethod, Description("Test mixed wildcard invalid challenge type")]
        public void TestMixedWildcardInvalidChallenge()
        {

            var item = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption>
                {
                    new DomainOption { Domain = "test.com", IsPrimaryDomain=true, IsSelected=true },
                    new DomainOption { Domain = "www.test.com", IsPrimaryDomain=false,IsSelected=true },
                    new DomainOption { Domain = "*.test.com", IsPrimaryDomain=false,IsSelected=true }
                },
                RequestConfig = new CertRequestConfig
                {
                    Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>
                  {
                      new CertRequestChallengeConfig
                      {
                          ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP
                      }
                  }
                }
            };

            var validationResult = CertificateEditorService.Validate(item, null, null, true);

            Assert.IsNotNull(validationResult);
            Assert.IsFalse(validationResult.IsValid);
            Assert.AreEqual(ValidationErrorCodes.CHALLENGE_TYPE_INVALID.ToString(), validationResult.ErrorCode);
        }

        [TestMethod, Description("Test max CN length")]
        public void TestMaxCNLength()
        {
            var item = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption>
                {
                    new DomainOption { Domain = "TherearemanyvariationsofpassagesofLoremIpsumavailablebutthemajorityhavesufferedalterationinsomeformbyinjectedhumourorrandomisedwordswhichdontlookevenslightlybelievable.com", IsPrimaryDomain=true, IsSelected=true },
                    new DomainOption { Domain = "www.test.com", IsPrimaryDomain=false,IsSelected=true }
                },
                RequestConfig = new CertRequestConfig
                {
                    Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>
                  {
                      new CertRequestChallengeConfig
                      {
                          ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP
                      }
                  }
                }
            };

            var validationResult = CertificateEditorService.Validate(item, null, null, true);

            Assert.IsNotNull(validationResult);
            Assert.IsFalse(validationResult.IsValid);
            Assert.AreEqual(ValidationErrorCodes.CN_LIMIT.ToString(), validationResult.ErrorCode);
        }

        [TestMethod, Description("Test with invalid local hostname")]
        public void TestInvalidHostname()
        {
            var item = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption>
                {
                    new DomainOption { Domain = "intranet.local", IsPrimaryDomain=true, IsSelected=true },
                    new DomainOption { Domain = "exchange01", IsPrimaryDomain=false,IsSelected=true }
                },
                RequestConfig = new CertRequestConfig
                {
                    Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>
                  {
                      new CertRequestChallengeConfig
                      {
                          ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP
                      }
                  }
                }
            };

            var validationResult = CertificateEditorService.Validate(item, null, null, true);

            Assert.IsNotNull(validationResult);
            Assert.IsFalse(validationResult.IsValid);
            Assert.AreEqual(ValidationErrorCodes.INVALID_HOSTNAME.ToString(), validationResult.ErrorCode);
        }
    }
}
