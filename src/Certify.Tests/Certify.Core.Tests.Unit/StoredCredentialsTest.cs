using Certify.Management;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class StoredCredentialsTest
    {
        [TestMethod]
        public async Task TestStoreCredential()
        {
            string testSecret = "This is a secret";
            var test = new StoredCredential
            {
                ProviderType = "DNS01.API.Route53",
                Title = "A test credential",
                StorageKey = Guid.NewGuid().ToString(),
                Secret = testSecret
            };
            var credentialsManager = new CredentialsManager();
            var result = await credentialsManager.UpdateCredential(test);

            Assert.IsNotNull(result, "Credential stored OK");

            List<StoredCredential> list = await credentialsManager.GetStoredCredentials();

            Assert.IsTrue(list.Any(l => l.StorageKey == test.StorageKey), "Credential retrieved");

            var secret = await credentialsManager.GetUnlockedCredential(test.StorageKey);
            Assert.IsNotNull(secret);
            Assert.IsTrue(secret == testSecret, "Credential decrypted");
        }
    }
}