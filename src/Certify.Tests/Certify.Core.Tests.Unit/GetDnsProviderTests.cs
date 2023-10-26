using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Core.Management.Challenges;
using Certify.Management;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class GetDnsProviderTests
    {
        private SQLiteCredentialStore credentialsManager;
        private DnsChallengeHelper dnsHelper;

        public GetDnsProviderTests() 
        {
            var pluginManager = new PluginManager();
            pluginManager.LoadPlugins(new List<string> { PluginManager.PLUGINS_DNS_PROVIDERS });
            var TEST_PATH = "Tests\\credentials";
            credentialsManager = new SQLiteCredentialStore(storageSubfolder: TEST_PATH);
            dnsHelper = new DnsChallengeHelper(credentialsManager);
        }

        [TestMethod, Description("Test Getting DNS Provider with empty CredentialsID")]
        public async Task TestGetDnsProvidersEmptyCredentialsID()
        {
            var providerTypeId = "DNS01.Powershell";
            var credentialsId = "";
            var result = await dnsHelper.GetDnsProvider(providerTypeId, credentialsId, null, credentialsManager);

            // Assert
            Assert.AreEqual("DNS Challenge API Provider not set or could not load.", result.Result.Message);
            Assert.IsFalse(result.Result.IsSuccess);
            Assert.IsFalse(result.Result.IsWarning);
        }

        [TestMethod, Description("Test Getting DNS Provider with empty ProviderTypeId")]
        public async Task TestGetDnsProvidersEmptyProviderTypeId()
        {
            var providerTypeId = "";
            var secrets = new Dictionary<string, string>();
            secrets.Add("zoneid", "ABC123");
            secrets.Add("secretid", "thereisnosecret");
            var testCredential = new StoredCredential
            {
                ProviderType = "DNS01.Manual",
                Title = "A test credential",
                StorageKey = Guid.NewGuid().ToString(),
                Secret = Newtonsoft.Json.JsonConvert.SerializeObject(secrets)
            };
            var updateResult = await credentialsManager.Update(testCredential);

            var result = await dnsHelper.GetDnsProvider(providerTypeId, testCredential.StorageKey, null, credentialsManager);

            // Assert
            Assert.AreEqual("DNS Challenge API Provider not set or could not load.", result.Result.Message);
            Assert.IsFalse(result.Result.IsSuccess);
            Assert.IsFalse(result.Result.IsWarning);
        }

        [TestMethod, Description("Test Getting DNS Provider with a bad CredentialId")]
        public async Task TestGetDnsProvidersBadCredentialId()
        {
            var secrets = new Dictionary<string, string>();
            secrets.Add("zoneid", "ABC123");
            secrets.Add("secretid", "thereisnosecret");
            var testCredential = new StoredCredential
            {
                ProviderType = "DNS01.Manual",
                Title = "A test credential",
                StorageKey = Guid.NewGuid().ToString(),
                Secret = Newtonsoft.Json.JsonConvert.SerializeObject(secrets)
            };

            var updateResult = await credentialsManager.Update(testCredential);

            var result = await dnsHelper.GetDnsProvider(testCredential.ProviderType, testCredential.StorageKey.Substring(5), null, credentialsManager);

            // Assert
            Assert.AreEqual("DNS Challenge API Credentials could not be decrypted or no longer exists. The original user must be used for decryption.", result.Result.Message);
            Assert.IsFalse(result.Result.IsSuccess);
            Assert.IsFalse(result.Result.IsWarning);
        }

        [TestMethod, Description("Test Getting DNS Provider")]
        public async Task TestGetDnsProviders()
        {
            var secrets = new Dictionary<string, string>();
            secrets.Add("zoneid", "ABC123");
            secrets.Add("secretid", "thereisnosecret");
            var testCredential = new StoredCredential
            {
                ProviderType = "DNS01.Manual",
                Title = "A test credential",
                StorageKey = Guid.NewGuid().ToString(),
                Secret = Newtonsoft.Json.JsonConvert.SerializeObject(secrets)
            };

            var updateResult = await credentialsManager.Update(testCredential);

            var result = await dnsHelper.GetDnsProvider(testCredential.ProviderType, testCredential.StorageKey, null, credentialsManager);

            // Assert
            Assert.AreEqual("Create Provider Instance", result.Result.Message);
            Assert.IsTrue(result.Result.IsSuccess);
            Assert.IsFalse(result.Result.IsWarning);
            Assert.AreEqual(testCredential.ProviderType, result.Provider.ProviderId);
        }

        [TestMethod, Description("Test Getting Challenge API Providers")]
        public async Task TestGetChallengeAPIProviders()
        {
            var challengeAPIProviders = await ChallengeProviders.GetChallengeAPIProviders();

            // Assert
            Assert.IsNotNull(challengeAPIProviders);
            Assert.AreNotEqual(0, challengeAPIProviders.Count);
            foreach (object item in challengeAPIProviders)
            {
                var itemType = item.GetType();
                Assert.IsTrue(itemType.GetProperty("ChallengeType") != null);
                Assert.IsTrue(itemType.GetProperty("Config") != null);
                Assert.IsTrue(itemType.GetProperty("Description") != null);
                Assert.IsTrue(itemType.GetProperty("HandlerType") != null);
                Assert.IsTrue(itemType.GetProperty("HasDynamicParameters") != null);
                Assert.IsTrue(itemType.GetProperty("HelpUrl") != null);
                Assert.IsTrue(itemType.GetProperty("Id") != null);
                Assert.IsTrue(itemType.GetProperty("IsEnabled") != null);
                Assert.IsTrue(itemType.GetProperty("IsExperimental") != null);
                Assert.IsTrue(itemType.GetProperty("IsTestModeSupported") != null);
                Assert.IsTrue(itemType.GetProperty("PropagationDelaySeconds") != null);
                Assert.IsTrue(itemType.GetProperty("ProviderCategoryId") != null);
                Assert.IsTrue(itemType.GetProperty("ProviderParameters") != null);
                Assert.IsTrue(itemType.GetProperty("Title") != null);
            }
        }
    }
}
