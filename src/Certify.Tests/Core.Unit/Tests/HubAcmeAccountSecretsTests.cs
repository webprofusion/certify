using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// The account private key and EAB key are only used by the instance holding an ACME account, so the instance does
    /// not send them to the hub when it lists its accounts
    /// </summary>
    [TestClass]
    public class HubAcmeAccountSecretsTests
    {
        private const string AccountKey = "-----BEGIN PRIVATE KEY-----";
        private const string EabKey = "eab-hmac";

        private static CertifyManager CreateManager()
        {
            var account = new AccountDetails
            {
                StorageKey = "acct-1",
                ID = "acct-1",
                Email = "admin@example.com",
                AccountURI = "https://acme.example.com/acct/1",
                AccountFingerprint = "ab:cd",
                AccountKey = AccountKey,
                EabKeyId = "eab-1",
                EabKey = EabKey
            };

            var credentials = new Mock<ICredentialsManager>();
            credentials
                .Setup(c => c.GetCredentials(StandardAuthTypes.STANDARD_ACME_ACCOUNT, null))
                .ReturnsAsync([new StoredCredential { StorageKey = "acct-1", Title = "Account 1" }]);
            credentials
                .Setup(c => c.GetUnlockedCredential("acct-1"))
                .ReturnsAsync(() => JsonConvert.SerializeObject(account));

            var manager = new CertifyManager();

            var field = typeof(CertifyManager).GetField("_credentialsManager", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "_credentialsManager should be available for testing");
            field.SetValue(manager, credentials.Object);

            return manager;
        }

        [TestMethod]
        public async Task GetAcmeAccountsCommand_DoesNotSendAccountKeyOrEabKey()
        {
            var manager = CreateManager();

            var result = await manager.PerformHubCommandWithResult(new InstanceCommandRequest(ManagementHubCommands.GetAcmeAccounts));

            Assert.IsFalse(result.Value.Contains(AccountKey), "the account private key should not be sent to the hub");
            Assert.IsFalse(result.Value.Contains(EabKey), "the EAB key should not be sent to the hub");

            var account = ((List<AccountDetails>)result.ObjectValue).Single();
            Assert.AreEqual(string.Empty, account.AccountKey);
            Assert.IsNull(account.EabKey);
            Assert.AreEqual("https://acme.example.com/acct/1", account.AccountURI);
            Assert.AreEqual("ab:cd", account.AccountFingerprint);
            Assert.AreEqual("eab-1", account.EabKeyId);
        }

        [TestMethod]
        public async Task GetAccountRegistrations_StillReturnsAccountKeyAndEabKey()
        {
            // the local service and desktop app use both keys, for account export and editing
            var manager = CreateManager();

            await manager.PerformHubCommandWithResult(new InstanceCommandRequest(ManagementHubCommands.GetAcmeAccounts));

            var account = (await manager.GetAccountRegistrations()).Single();
            Assert.AreEqual(AccountKey, account.AccountKey);
            Assert.AreEqual(EabKey, account.EabKey);
        }
    }
}
