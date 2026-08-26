using System;
using System.IO;
using System.Text.Json;
using Certify.Server.Hub.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class HubJwtSecretProvisioningTests
    {
        private string _testDir = default!;

        [TestInitialize]
        public void Setup()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "certify-jwt-secret-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(_testDir, recursive: true);
            }
            catch (Exception)
            {
                // test cleanup only
            }
        }

        private string WriteSettings(string content)
        {
            var path = Path.Combine(_testDir, "hubservice.json");
            File.WriteAllText(path, content);
            return path;
        }

        private static string? ReadConfiguredSecret(string path)
        {
            return new ConfigurationBuilder()
                .AddJsonFile(path, optional: false)
                .Build()["JwtSettings:secret"];
        }

        [TestMethod]
        public void EnsureSecret_GeneratesAndSavesSecret_WhenJwtSettingsHasNoSecret()
        {
            var path = WriteSettings(@"{ ""JwtSettings"": { ""issuer"": ""Certify.Server.Hub.Api"" } }");

            var result = HubJwtSecretProvisioning.EnsureSecret(path);

            Assert.AreEqual(JwtSecretProvisioningOutcome.SecretGenerated, result.Outcome, result.Message);

            var secret = ReadConfiguredSecret(path);
            Assert.IsFalse(string.IsNullOrWhiteSpace(secret), "A secret should have been saved to the settings file.");
            Assert.AreEqual(32, Convert.FromBase64String(secret!).Length, "Secret should be 32 bytes of base64 encoded random data.");

            // unrelated settings must survive the rewrite
            Assert.AreEqual("Certify.Server.Hub.Api", new ConfigurationBuilder().AddJsonFile(path, optional: false).Build()["JwtSettings:issuer"]);
        }

        [TestMethod]
        public void EnsureSecret_GeneratesAndSavesSecret_WhenJwtSettingsSectionIsMissingEntirely()
        {
            var path = WriteSettings(@"{ ""AllowedHosts"": ""*"" }");

            var result = HubJwtSecretProvisioning.EnsureSecret(path);

            Assert.AreEqual(JwtSecretProvisioningOutcome.SecretGenerated, result.Outcome, result.Message);
            Assert.IsFalse(string.IsNullOrWhiteSpace(ReadConfiguredSecret(path)));
            Assert.AreEqual("*", new ConfigurationBuilder().AddJsonFile(path, optional: false).Build()["AllowedHosts"]);
        }

        [TestMethod]
        public void EnsureSecret_GeneratesAndSavesSecret_WhenSecretIsBlank()
        {
            var path = WriteSettings(@"{ ""JwtSettings"": { ""secret"": ""   "" } }");

            var result = HubJwtSecretProvisioning.EnsureSecret(path);

            Assert.AreEqual(JwtSecretProvisioningOutcome.SecretGenerated, result.Outcome, result.Message);
            Assert.IsFalse(string.IsNullOrWhiteSpace(ReadConfiguredSecret(path)));
        }

        [TestMethod]
        public void EnsureSecret_ReplacesPlaceholder_AndPreservesComments()
        {
            var path = WriteSettings(@"{
  // the commented out Kestrel HTTPS example must survive
  ""JwtSettings"": { ""secret"": """ + HubJwtSecretProvisioning.SecretPlaceholder + @""" }
}");

            var result = HubJwtSecretProvisioning.EnsureSecret(path);

            Assert.AreEqual(JwtSecretProvisioningOutcome.PlaceholderReplaced, result.Outcome, result.Message);

            var content = File.ReadAllText(path);
            Assert.IsFalse(content.Contains(HubJwtSecretProvisioning.SecretPlaceholder, StringComparison.Ordinal));
            StringAssert.Contains(content, "// the commented out Kestrel HTTPS example must survive");
            Assert.IsFalse(string.IsNullOrWhiteSpace(ReadConfiguredSecret(path)));
        }

        [TestMethod]
        public void EnsureSecret_LeavesFileUntouched_WhenSecretIsAlreadyPresent()
        {
            var path = WriteSettings(@"{ ""JwtSettings"": { ""secret"": ""existing-secret-value"" } }");
            var originalContent = File.ReadAllText(path);

            var result = HubJwtSecretProvisioning.EnsureSecret(path);

            Assert.AreEqual(JwtSecretProvisioningOutcome.AlreadyPresent, result.Outcome, result.Message);
            Assert.AreEqual(originalContent, File.ReadAllText(path), "An existing secret must not be rotated.");
            Assert.IsFalse(File.Exists(path + ".bak"), "No backup should be written when nothing changed.");
        }

        [TestMethod]
        public void EnsureSecret_BacksUpOriginalFile_WhenRewritingToAddSecret()
        {
            var path = WriteSettings(@"{ ""JwtSettings"": { ""issuer"": ""Certify.Server.Hub.Api"" } }");
            var originalContent = File.ReadAllText(path);

            var result = HubJwtSecretProvisioning.EnsureSecret(path);

            Assert.AreEqual(JwtSecretProvisioningOutcome.SecretGenerated, result.Outcome, result.Message);
            Assert.IsNotNull(result.BackupPath);
            Assert.IsTrue(File.Exists(result.BackupPath!), "The previous settings file should have been backed up.");
            Assert.AreEqual(originalContent, File.ReadAllText(result.BackupPath!));
        }

        [TestMethod]
        public void EnsureSecret_ReportsFailure_WhenFileIsNotValidJson()
        {
            var path = WriteSettings("this is not json");

            var result = HubJwtSecretProvisioning.EnsureSecret(path);

            Assert.AreEqual(JwtSecretProvisioningOutcome.Failed, result.Outcome);
        }

        [TestMethod]
        public void EnsureSecret_ReportsFailure_WhenFileDoesNotExist()
        {
            var result = HubJwtSecretProvisioning.EnsureSecret(Path.Combine(_testDir, "no-such-file.json"));

            Assert.AreEqual(JwtSecretProvisioningOutcome.Failed, result.Outcome);
        }

        [TestMethod]
        public void EnsureSecret_ProducesADistinctSecretEachTime()
        {
            var first = HubJwtSecretProvisioning.GenerateSecret();
            var second = HubJwtSecretProvisioning.GenerateSecret();

            Assert.AreNotEqual(first, second);
        }

        [TestMethod]
        public void EnsureSecret_WritesValidJson_WhenAddingSecret()
        {
            var path = WriteSettings(@"{ ""JwtSettings"": { ""issuer"": ""x"" }, ""Kestrel"": { ""Endpoints"": { ""Http"": { ""Url"": ""http://0.0.0.0:8080"" } } } }");

            HubJwtSecretProvisioning.EnsureSecret(path);

            // must remain parseable, and the rest of the configuration intact
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var config = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();

            Assert.AreEqual("http://0.0.0.0:8080", config["Kestrel:Endpoints:Http:Url"]);
            Assert.IsFalse(string.IsNullOrWhiteSpace(config["JwtSettings:secret"]));
        }
    }
}
