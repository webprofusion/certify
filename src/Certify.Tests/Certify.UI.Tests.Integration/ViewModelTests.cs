using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models;
using Certify.Models.Config;
using Certify.UI.ViewModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.UI.Tests.Integration
{
    [TestClass]
    public class ViewModelTest
    {
        [TestMethod, Ignore]
        public async Task TestViewModelSetup()
        {
            var mockClient = new Mock<ICertifyClient>();

            mockClient.Setup(c => c.GetPreferences()).Returns(
                Task.FromResult(new Models.Preferences { })
                );

            mockClient.Setup(c => c.GetManagedCertificates(It.IsAny<Models.ManagedCertificateFilter>()))
                .Returns(
                Task.FromResult(new List<ManagedCertificate> {
                    new ManagedCertificate{
                         Id= Guid.NewGuid().ToString(),
                         Name="Test  Managed Certificate"
                    }
                })
                );

            mockClient.Setup(c => c.GetAccounts())
                .Returns(
                Task.FromResult(
                    new List<AccountDetails> {
                        new AccountDetails {
                            Email = "test@example.com",
                            IsStagingAccount = true,
                            CertificateAuthorityId = StandardCertAuthorities.LETS_ENCRYPT
                        }
                    })
                );

            mockClient.Setup(c => c.GetCredentials())
              .Returns(
              Task.FromResult(new List<StoredCredential> { })
              );

            var appModel = new AppViewModel(mockClient.Object);

            await appModel.LoadSettingsAsync();

            Assert.IsTrue(appModel.ManagedCertificates.Count > 0, "Should have managed sites");

            Assert.IsTrue(appModel.HasRegisteredContacts, "Should have a registered contact");

            await appModel.RefreshStoredCredentialsList();

            appModel.RenewAll(new RenewalSettings { });
        }

        [TestMethod]
        public void TestManagedCertViewModelValidationWithDomains()
        {
            var model = new ManagedCertificateViewModel();

            // ensure item with selected primary domain is valid
            model.SelectedItem = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption> {
                    new DomainOption{ Domain="test1.test.com", IsSelected=true, IsPrimaryDomain=true, IsManualEntry=true, Type= "dns"}
                }
            };

            var result = model.Validate(applyAutoConfiguration: true);

            Assert.IsTrue(result.IsValid, result.Message);

            // ensure item with selected domain not set to primary is valid after auto config
            model.SelectedItem = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption> {
                    new DomainOption{ Domain="test1.test.com", IsSelected=true, IsPrimaryDomain=false, IsManualEntry=true, Type= "dns"}
                }
            };

            result = model.Validate(applyAutoConfiguration: true);

            Assert.IsTrue(result.IsValid, result.Message);

            // ensure item with no name is invalid
            model.SelectedItem = new ManagedCertificate() { Name = "" };

            result = model.Validate(applyAutoConfiguration: true);

            Assert.IsFalse(result.IsValid, result.Message);
            Assert.AreEqual(ManagedCertificateViewModel.ValidationErrorCodes.REQUIRED_NAME.ToString(), result.ErrorCode);


            // ensure item with no identifiers is invalid
            model.SelectedItem = new ManagedCertificate();

            result = model.Validate(applyAutoConfiguration: true);

            Assert.IsFalse(result.IsValid, result.Message);
            Assert.AreEqual(ManagedCertificateViewModel.ValidationErrorCodes.REQUIRED_PRIMARY_IDENTIFIER.ToString(), result.ErrorCode);

            // ensure item with no selected identifiers (but present) is invalid
            model.SelectedItem = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption> {
                        new DomainOption{ Domain="www.test.com", IsPrimaryDomain=false, IsSelected=false }
                }
            };

            result = model.Validate(applyAutoConfiguration: true);

            Assert.IsFalse(result.IsValid, result.Message);
            Assert.AreEqual(ManagedCertificateViewModel.ValidationErrorCodes.REQUIRED_PRIMARY_IDENTIFIER.ToString(), result.ErrorCode);

            // ensure item with local host name is invalid
            model.SelectedItem = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption> {
                        new DomainOption{ Domain="srv01", IsPrimaryDomain=true, IsSelected=true }
                }
            };

            result = model.Validate(applyAutoConfiguration: true);

            Assert.IsFalse(result.IsValid, result.Message);
            Assert.AreEqual(ManagedCertificateViewModel.ValidationErrorCodes.INVALID_HOSTNAME.ToString(), result.ErrorCode);

            // ensure item with wildcard cannot use http validation
            model.SelectedItem = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption> {
                        new DomainOption{ Domain="*.test.com", IsPrimaryDomain=true, IsSelected=true }
                }
            };

            result = model.Validate(applyAutoConfiguration: true);

            Assert.IsFalse(result.IsValid, result.Message);
            Assert.AreEqual(ManagedCertificateViewModel.ValidationErrorCodes.CHALLENGE_TYPE_INVALID.ToString(), result.ErrorCode);


            // ensure item with multiple auth config can only have one blank domain match rule
            model.SelectedItem = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption> {
                        new DomainOption{ Domain="test.com", IsPrimaryDomain=true, IsSelected=true }
                },
                RequestConfig = new CertRequestConfig
                {
                    Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig> {
                        new CertRequestChallengeConfig{ DomainMatch=""},
                        new CertRequestChallengeConfig{ DomainMatch=""}
                 }
                }
            };

            result = model.Validate(applyAutoConfiguration: true);

            Assert.IsFalse(result.IsValid, result.Message);
            Assert.AreEqual(ManagedCertificateViewModel.ValidationErrorCodes.CHALLENGE_TYPE_INVALID.ToString(), result.ErrorCode);

            // ensure item with dns challenge type must have challenge provider set
            model.SelectedItem = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption> {
                        new DomainOption{ Domain="test.com", IsPrimaryDomain=true, IsSelected=true }
                },
                RequestConfig = new CertRequestConfig
                {
                    Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig> {
                        new CertRequestChallengeConfig{ DomainMatch="", ChallengeType=SupportedChallengeTypes.CHALLENGE_TYPE_DNS , ChallengeProvider=""}
                    }
                }
            };

            result = model.Validate(applyAutoConfiguration: true);

            Assert.IsFalse(result.IsValid, result.Message);
            Assert.AreEqual(ManagedCertificateViewModel.ValidationErrorCodes.CHALLENGE_TYPE_INVALID.ToString(), result.ErrorCode);


            // ensure item with challenge type required parameters has param set
            model.SelectedItem = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption> {
                        new DomainOption{ Domain="test.com", IsPrimaryDomain=true, IsSelected=true }
                },
                RequestConfig = new CertRequestConfig
                {
                    Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig> {
                        new CertRequestChallengeConfig{
                            DomainMatch="",
                            ChallengeType=SupportedChallengeTypes.CHALLENGE_TYPE_DNS ,
                            ChallengeProvider="An.Example.Provider",
                            Parameters = new System.Collections.ObjectModel.ObservableCollection<ProviderParameter>{
                                new ProviderParameter{ IsRequired=true, Name="param01", Value=""  },
                                new ProviderParameter{ IsRequired=false, Name="param02", Value="test"  }
                            }
                        }
                    }
                }
            };

            result = model.Validate(applyAutoConfiguration: true);

            Assert.IsFalse(result.IsValid, result.Message);
            Assert.AreEqual(ManagedCertificateViewModel.ValidationErrorCodes.REQUIRED_CHALLENGE_CONFIG_PARAM.ToString(), result.ErrorCode);

            // ensure item cannot select over 100 domains 
            model.SelectedItem = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption>
                {
                }
            };

            for (var i = 0; i < 200; i++)
            {
                model.SelectedItem.DomainOptions.Add(new DomainOption { Domain = i + ".test.com", IsSelected = true });
            }

            result = model.Validate(applyAutoConfiguration: true);

            Assert.IsFalse(result.IsValid, result.Message);
            Assert.AreEqual(ManagedCertificateViewModel.ValidationErrorCodes.SAN_LIMIT.ToString(), result.ErrorCode);

        }

        [TestMethod]
        public void TestManagedCertViewModelValidationWithIPs()
        {
            var model = new ManagedCertificateViewModel();

            // ensure item with selected primary ip is valid
            model.SelectedItem = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption> {
                    new DomainOption{ Domain="192.168.1.1", IsSelected=true, IsPrimaryDomain=true, IsManualEntry=true, Type= "ip"}
                }
            };

            var result = model.Validate(applyAutoConfiguration: true);

            Assert.IsTrue(result.IsValid, result.Message);

            // ensure item with selected identifier not set to primary is valid after auto config
            model.SelectedItem = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption> {
                    new DomainOption{ Domain="192.168.1.1", IsSelected=true, IsPrimaryDomain=false, IsManualEntry=true, Type= "ip"}
                }
            };

            result = model.Validate(applyAutoConfiguration: true);

            Assert.IsTrue(result.IsValid, result.Message);



            // ensure item can have mix of domains and ip
            model.SelectedItem = new ManagedCertificate
            {
                DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption> {
                    new DomainOption{ Domain="192.168.1.1", IsSelected=true, IsPrimaryDomain=true, IsManualEntry=true, Type= "ip"},
                    new DomainOption{ Domain="www.test.com", IsSelected=true, IsPrimaryDomain=false, IsManualEntry=true, Type= "dns"}
                }
            };

            result = model.Validate(applyAutoConfiguration: true);

            Assert.IsTrue(result.IsValid, result.Message);

        }
    }
}
