using Certify.Models;
using Certify.Models.Shared.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
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
        [Ignore("This test is ignored because we have currently removed validation on CN length")]
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
        [Ignore("This test is ignored because we no longer gate local intranet names and leave this to the CA")]
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
            Assert.AreEqual(ValidationErrorCodes.INVALID_IDENTIFIER_DNS.ToString(), validationResult.ErrorCode);
        }

        private static ManagedCertificate CreateSavedItem(string id, string name)
        {
            var item = new ManagedCertificate { Id = id, Name = name };
            item.IsChanged = false;
            return item;
        }

        [TestMethod, Description("An update received for the item being edited does not replace its unsaved changes")]
        public void KeepUnsavedChanges_KeepsEditedItemWithUnsavedChanges()
        {
            var edited = CreateSavedItem("item-1", "Original");
            edited.RequestConfig.PreferredChain = "Edited Chain";

            var received = CreateSavedItem("item-1", "Renewed elsewhere");

            Assert.AreSame(edited, CertificateEditorService.KeepUnsavedChanges(received, edited));
        }

        [TestMethod, Description("An update received for the item being viewed replaces it when it has no unsaved changes")]
        public void KeepUnsavedChanges_UsesReceivedCopyWithoutUnsavedChanges()
        {
            var edited = CreateSavedItem("item-1", "Original");
            var received = CreateSavedItem("item-1", "Renewed elsewhere");

            Assert.AreSame(received, CertificateEditorService.KeepUnsavedChanges(received, edited));
        }

        [TestMethod, Description("Unsaved changes to one item do not stop updates to other items")]
        public void KeepUnsavedChanges_UsesReceivedCopyOfDifferentItem()
        {
            var edited = CreateSavedItem("item-1", "Original");
            edited.Name = "Edited";

            var received = CreateSavedItem("item-2", "Other item");

            Assert.AreSame(received, CertificateEditorService.KeepUnsavedChanges(received, edited));
            Assert.AreSame(received, CertificateEditorService.KeepUnsavedChanges(received, null));
        }

        [TestMethod, Description("A new item which has not been saved yet is never matched to a received item")]
        public void KeepUnsavedChanges_UsesReceivedCopyForNewItem()
        {
            var edited = new ManagedCertificate { Name = "New item" };
            var received = new ManagedCertificate { Name = "Received" };
            received.IsChanged = false;

            Assert.IsTrue(edited.IsChanged);
            Assert.AreSame(received, CertificateEditorService.KeepUnsavedChanges(received, edited));
        }
    }
}
