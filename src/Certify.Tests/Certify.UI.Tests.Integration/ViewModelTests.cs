using Certify.Client;
using Certify.Models;
using Certify.Models.Config;
using Certify.UI.ViewModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Certify.UI.Tests.Integration
{
    [TestClass]
    public class ViewModelTest
    {
        [TestMethod]
        public async Task TestViewModelSetup()
        {
            var mockClient = new Mock<ICertifyClient>();

            mockClient.Setup(c => c.GetPreferences()).Returns(
                Task.FromResult(new Models.Preferences { })
                );

            mockClient.Setup(c => c.GetManagedSites(It.IsAny<Models.ManagedSiteFilter>()))
                .Returns(
                Task.FromResult(new List<ManagedSite> {
                    new ManagedSite{
                         Id= Guid.NewGuid().ToString(),
                         Name="Test Managed Site"
                    }
                })
                );

            mockClient.Setup(c => c.GetPrimaryContact())
                .Returns(
                Task.FromResult("test@example.com")
                );

            mockClient.Setup(c => c.GetCredentials())
              .Returns(
              Task.FromResult(new List<StoredCredential> { })
              );

            AppModel appModel = new AppModel(mockClient.Object);

            await appModel.LoadSettingsAsync();

            Assert.IsTrue(appModel.ManagedSites.Count > 0, "Should have managed sites");

            Assert.IsTrue(appModel.HasRegisteredContacts, "Should have a registered contact");

            await appModel.RefreshStoredCredentialsList();

            appModel.RenewAll(true);
        }
    }
}