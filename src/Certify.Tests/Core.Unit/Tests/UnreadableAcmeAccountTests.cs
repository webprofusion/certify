using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// An ACME account whose stored credential cannot be decrypted cannot be used, but the hub still lists it so that
    /// it can be removed
    /// </summary>
    [TestClass]
    public class UnreadableAcmeAccountTests
    {
        private const string UnreadableKey = "acct-unreadable";
        private const string DnsCredentialKey = "dns-1";

        private static (CertifyManager manager, Mock<ICredentialsManager> credentials) CreateManager()
        {
            var acmeCredential = new StoredCredential { StorageKey = UnreadableKey, Title = "letsencrypt.org_Production", ProviderType = StandardAuthTypes.STANDARD_ACME_ACCOUNT };
            var dnsCredential = new StoredCredential { StorageKey = DnsCredentialKey, Title = "DNS", ProviderType = "DNS01.API.Cloudflare" };

            var stored = new List<StoredCredential> { acmeCredential, dnsCredential };

            var credentials = new Mock<ICredentialsManager>();
            credentials
                .Setup(c => c.GetCredentials(null, null))
                .ReturnsAsync(() => stored.ToList());
            credentials
                .Setup(c => c.GetCredentials(StandardAuthTypes.STANDARD_ACME_ACCOUNT, null))
                .ReturnsAsync(() => stored.Where(c => c.ProviderType == StandardAuthTypes.STANDARD_ACME_ACCOUNT).ToList());
            credentials
                .Setup(c => c.GetCredential(It.IsAny<string>()))
                .ReturnsAsync((string key) => stored.FirstOrDefault(c => c.StorageKey == key));
            credentials
                .Setup(c => c.GetUnlockedCredential(UnreadableKey))
                .ThrowsAsync(new AggregateException($"Failed to decrypt Credential [{UnreadableKey}]"));
            credentials
                .Setup(c => c.Delete(It.IsAny<IManagedItemStore>(), It.IsAny<string>()))
                .ReturnsAsync((IManagedItemStore _, string key) => new ActionResult("Credential Deleted", stored.RemoveAll(c => c.StorageKey == key) > 0));

            var manager = new CertifyManager();

            var field = typeof(CertifyManager).GetField("_credentialsManager", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "_credentialsManager should be available for testing");
            field.SetValue(manager, credentials.Object);

            return (manager, credentials);
        }

        [TestMethod]
        public async Task GetAcmeAccountsCommand_ListsUnreadableAccount()
        {
            var (manager, _) = CreateManager();

            var result = await manager.PerformHubCommandWithResult(new InstanceCommandRequest(ManagementHubCommands.GetAcmeAccounts));

            var account = ((List<AccountDetails>)result.ObjectValue).Single();
            Assert.IsTrue(account.IsUnreadable);
            Assert.AreEqual(UnreadableKey, account.ID);
            Assert.AreEqual(UnreadableKey, account.StorageKey);
            Assert.AreEqual("letsencrypt.org_Production", account.Title);
        }

        [TestMethod]
        public async Task GetAccountRegistrations_LeavesOutUnreadableAccount()
        {
            // certificate requests choose from these accounts, so an account which cannot be used is not one of them
            var (manager, _) = CreateManager();

            Assert.AreEqual(0, (await manager.GetAccountRegistrations()).Count);
        }

        [TestMethod]
        public async Task RemoveAccount_UnreadableAccount_RemovesItsStoredCredential()
        {
            var (manager, credentials) = CreateManager();

            var result = await manager.RemoveAccount(UnreadableKey, includeAccountDeactivation: true);

            Assert.IsTrue(result.IsSuccess, result.Message);
            credentials.Verify(c => c.Delete(It.IsAny<IManagedItemStore>(), UnreadableKey), Times.Once);

            var listed = await manager.PerformHubCommandWithResult(new InstanceCommandRequest(ManagementHubCommands.GetAcmeAccounts));
            Assert.AreEqual(0, ((List<AccountDetails>)listed.ObjectValue).Count, "the removed account should no longer be listed");
        }

        [TestMethod]
        public async Task RemoveAccount_UnknownAccount_ReportsNotFound()
        {
            var (manager, credentials) = CreateManager();

            var result = await manager.RemoveAccount("not-an-account");

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual("Account not found.", result.Message);
            credentials.Verify(c => c.Delete(It.IsAny<IManagedItemStore>(), It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public async Task RemoveAccount_OtherStoredCredential_IsNotRemoved()
        {
            var (manager, credentials) = CreateManager();

            var result = await manager.RemoveAccount(DnsCredentialKey);

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual("Account not found.", result.Message);
            credentials.Verify(c => c.Delete(It.IsAny<IManagedItemStore>(), It.IsAny<string>()), Times.Never);
        }
    }
}
