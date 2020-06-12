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
            var testSecret = "This is a secret";
            var test = new StoredCredential
            {
                ProviderType = "DNS01.API.Route53",
                Title = "A test credential",
                StorageKey = Guid.NewGuid().ToString(),
                Secret = testSecret
            };
            var credentialsManager = new CredentialsManager();
            credentialsManager.StorageSubfolder = "Tests\\credentials";
            var result = await credentialsManager.Update(test);

            Assert.IsNotNull(result, "Credential stored OK");

            var list = await credentialsManager.GetCredentials();

            Assert.IsTrue(list.Any(l => l.StorageKey == test.StorageKey), "Credential retrieved");

            var secret = await credentialsManager.GetUnlockedCredential(test.StorageKey);
            Assert.IsNotNull(secret);
            Assert.IsTrue(secret == testSecret, "Credential decrypted");
        }

        [TestMethod]
        public async Task TestStoreCredentialDictionary()
        {
            var secrets = new Dictionary<string, string>();
            secrets.Add("zoneid", "ABC123");
            secrets.Add("secretid", "thereisnosecret");

            var test = new StoredCredential
            {
                ProviderType = "DNS01.API.Route53",
                Title = "A test credential",
                StorageKey = Guid.NewGuid().ToString(),
                Secret = Newtonsoft.Json.JsonConvert.SerializeObject(secrets)
            };

            var credentialsManager = new CredentialsManager();
            credentialsManager.StorageSubfolder = "Tests\\credentials";
            var result = await credentialsManager.Update(test);

            Assert.IsNotNull(result, "Credential stored OK");

            var list = await credentialsManager.GetCredentials();

            Assert.IsTrue(list.Any(l => l.StorageKey == test.StorageKey), "Credential retrieved");

            var secret = await credentialsManager.GetUnlockedCredentialsDictionary(test.StorageKey);

            Assert.IsNotNull(secret);
            Assert.IsTrue(secret["zoneid"] == "ABC123", "Credential decrypted");
        }
    }
}
