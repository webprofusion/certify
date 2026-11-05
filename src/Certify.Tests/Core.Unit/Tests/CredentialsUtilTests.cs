using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class CredentialsUtilTests
    {
        private static Mock<ICredentialsManager> GetCredentialsManager(params string[] storageKeys)
        {
            var credentialsManager = new Mock<ICredentialsManager>();

            credentialsManager
                .Setup(c => c.GetCredentials(null, null))
                .ReturnsAsync(storageKeys.Select(k => new StoredCredential { StorageKey = k, Title = k }).ToList());

            credentialsManager
                .Setup(c => c.GetUnlockedCredential(It.IsAny<string>()))
                .ReturnsAsync("{}");

            return credentialsManager;
        }

        [TestMethod, Description("Credentials which cannot be decrypted are reported, others are not")]
        public async Task GetUnreadableCredentials_ReturnsOnlyCredentialsWhichCannotBeDecrypted()
        {
            var credentialsManager = GetCredentialsManager("readable", "unreadable");

            credentialsManager
                .Setup(c => c.GetUnlockedCredential("unreadable"))
                .ThrowsAsync(new AggregateException("Failed to decrypt"));

            var unreadable = await CredentialsUtil.GetUnreadableCredentials(credentialsManager.Object);

            Assert.AreEqual("unreadable", unreadable.Single().StorageKey);
        }

        [TestMethod, Description("With no stored credentials there is nothing to report")]
        public async Task GetUnreadableCredentials_NoCredentials_ReturnsNone()
        {
            var unreadable = await CredentialsUtil.GetUnreadableCredentials(GetCredentialsManager().Object);

            Assert.AreEqual(0, unreadable.Count);
        }

        [TestMethod, Description("A store failure is not a decryption failure, so must not report credentials as unreadable")]
        public async Task GetUnreadableCredentials_StoreUnavailable_Throws()
        {
            var credentialsManager = GetCredentialsManager("credential");

            credentialsManager
                .Setup(c => c.GetUnlockedCredential("credential"))
                .ThrowsAsync(new InvalidOperationException("Data store unavailable"));

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CredentialsUtil.GetUnreadableCredentials(credentialsManager.Object));
        }
    }
}
