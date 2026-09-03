using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for what a certificate subscription does with the certificate its source supplies, before any of it
    /// reaches a deployment target: the lifetime checks which reject a certificate that is not worth deploying, and
    /// the metadata and identifiers taken from it, which are what binding deployment matches against
    /// </summary>
    [TestClass]
    public class SubscriptionAssetTests
    {
        private readonly List<string> _tempFiles = new();

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var file in _tempFiles)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // a temp file we could not remove is not a test failure
                }
            }
        }

        /// <summary>
        /// Write a certificate to a PFX for the code under test to load. No private key is included: nothing being
        /// tested here uses one, and importing a key would need the machine key store
        /// </summary>
        private string WriteCertificateAsset(
            string subjectCn,
            DateTimeOffset notBefore,
            DateTimeOffset notAfter,
            string[] dnsNames = null,
            string[] ipAddresses = null)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest($"CN={subjectCn}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            if (dnsNames?.Length > 0 || ipAddresses?.Length > 0)
            {
                var sanBuilder = new SubjectAlternativeNameBuilder();

                foreach (var dnsName in dnsNames ?? Array.Empty<string>())
                {
                    sanBuilder.AddDnsName(dnsName);
                }

                foreach (var ipAddress in ipAddresses ?? Array.Empty<string>())
                {
                    sanBuilder.AddIpAddress(IPAddress.Parse(ipAddress));
                }

                request.CertificateExtensions.Add(sanBuilder.Build());
            }

            using var signed = request.CreateSelfSigned(notBefore, notAfter);
            using var publicOnly = X509CertificateLoader.LoadCertificate(signed.Export(X509ContentType.Cert));

            var path = Path.Combine(Path.GetTempPath(), $"certify-subscription-asset-{Guid.NewGuid():N}.pfx");
            File.WriteAllBytes(path, publicOnly.Export(X509ContentType.Pfx));
            _tempFiles.Add(path);

            return path;
        }

        private string WriteCorruptAsset()
        {
            var path = Path.Combine(Path.GetTempPath(), $"certify-subscription-asset-{Guid.NewGuid():N}.pfx");
            File.WriteAllText(path, "this is not a certificate");
            _tempFiles.Add(path);

            return path;
        }

        private static ManagedCertificate CreateSubscription()
        {
            return new ManagedCertificate
            {
                Id = "subscriber-item",
                Name = "Subscriber Item",
                ItemType = ManagedCertificateType.SSL_ExternalSubscription,
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                    ExternalReference = "instance-1/managed-cert-1"
                }
            };
        }

        private sealed class ValidationOutcome
        {
            public bool IsValid { get; init; }
            public string Message { get; init; }
            public int? PercentageElapsed { get; init; }
            public string Thumbprint { get; init; }
            public DateTimeOffset? DateExpiry { get; init; }
        }

        private static async Task<ValidationOutcome> InvokeValidate(CertifyManager manager, ManagedCertificate item, string assetPath)
        {
            var method = typeof(CertifyManager).GetMethod("ValidateExternalCertificateAsset", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "ValidateExternalCertificateAsset should be available for testing");

            var task = (Task)method.Invoke(manager, new object[] { item, item.ExternalSource, assetPath });
            await task;

            var result = task.GetType().GetProperty("Result").GetValue(task);
            var resultType = result.GetType();

            return new ValidationOutcome
            {
                IsValid = (bool)resultType.GetProperty("IsValid").GetValue(result),
                Message = (string)resultType.GetProperty("Message").GetValue(result),
                PercentageElapsed = (int?)resultType.GetProperty("PercentageElapsed").GetValue(result),
                Thumbprint = (string)resultType.GetProperty("Thumbprint").GetValue(result),
                DateExpiry = (DateTimeOffset?)resultType.GetProperty("DateExpiry").GetValue(result)
            };
        }

        private static async Task<bool> InvokeApplyMetadata(CertifyManager manager, ManagedCertificate item, string assetPath)
        {
            var method = typeof(CertifyManager).GetMethod("ApplyExternalCertificateMetadata", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "ApplyExternalCertificateMetadata should be available for testing");

            var task = (Task<bool>)method.Invoke(manager, new object[] { item, item.ExternalSource, assetPath });
            return await task;
        }

        private static List<CertIdentifierItem> InvokeExtractIdentifiers(X509Certificate2 cert)
        {
            var method = typeof(CertifyManager).GetMethod("ExtractIdentifiersFromCertificate", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "ExtractIdentifiersFromCertificate should be available for testing");

            return (List<CertIdentifierItem>)method.Invoke(null, new object[] { cert });
        }

        [TestMethod, Description("A certificate with most of its lifetime left is accepted for deployment")]
        public async Task HealthyCertificateIsAccepted()
        {
            var manager = new CertifyManager();
            var item = CreateSubscription();
            var expiry = DateTimeOffset.UtcNow.AddDays(80);

            var assetPath = WriteCertificateAsset("healthy.example.com", DateTimeOffset.UtcNow.AddDays(-10), expiry);

            var result = await InvokeValidate(manager, item, assetPath);

            Assert.IsTrue(result.IsValid, "A certificate with most of its lifetime left is deployable");
            Assert.IsNull(result.Message, "A certificate which passes validation has nothing to report");
            Assert.IsNotNull(result.Thumbprint, "The thumbprint is recorded against the item and logged, so validation has to report it");
            Assert.AreEqual(expiry.Date, result.DateExpiry?.UtcDateTime.Date);
            Assert.IsTrue(result.PercentageElapsed < LifetimeHealthThresholds.PercentageDanger);
        }

        [TestMethod, Description("An expired certificate from the source is rejected rather than deployed")]
        public async Task ExpiredCertificateIsRejected()
        {
            var manager = new CertifyManager();
            var item = CreateSubscription();

            // a source which has stopped renewing an item would otherwise hand us an expired certificate to install
            // over a working one
            var assetPath = WriteCertificateAsset("expired.example.com", DateTimeOffset.UtcNow.AddDays(-90), DateTimeOffset.UtcNow.AddDays(-1));

            var result = await InvokeValidate(manager, item, assetPath);

            Assert.IsFalse(result.IsValid);
            Assert.Contains("expired", result.Message, "The rejection has to say the certificate had expired, so the operator can go and fix the source");
            Assert.IsNotNull(result.Thumbprint, "The rejected certificate is still identified, so the operator can tell which one the source supplied");
        }

        [TestMethod, Description("A certificate which has used up nearly all of its lifetime is rejected")]
        public async Task CertificateBeyondTheDangerThresholdIsRejected()
        {
            var manager = new CertifyManager();
            var item = CreateSubscription();

            // not yet expired, but so close to it that deploying it would leave the target failing again within hours
            var assetPath = WriteCertificateAsset("stale.example.com", DateTimeOffset.UtcNow.AddDays(-99), DateTimeOffset.UtcNow.AddDays(1));

            var result = await InvokeValidate(manager, item, assetPath);

            Assert.IsFalse(result.IsValid);
            Assert.Contains($"{LifetimeHealthThresholds.PercentageDanger}%", result.Message, "The rejection names the threshold the certificate exceeded");
            Assert.IsTrue(result.PercentageElapsed >= LifetimeHealthThresholds.PercentageDanger);
        }

        [TestMethod, Description("A certificate just inside the lifetime threshold is still accepted")]
        public async Task CertificateJustInsideTheThresholdIsAccepted()
        {
            var manager = new CertifyManager();
            var item = CreateSubscription();

            // ~90% elapsed: past the point of a healthy renewal, but still worth deploying over whatever is installed
            var assetPath = WriteCertificateAsset("nearly.example.com", DateTimeOffset.UtcNow.AddDays(-90), DateTimeOffset.UtcNow.AddDays(10));

            var result = await InvokeValidate(manager, item, assetPath);

            Assert.IsTrue(result.IsValid, "Only a certificate past the danger threshold is refused, and this one is not");
            Assert.IsTrue(result.PercentageElapsed < LifetimeHealthThresholds.PercentageDanger);
        }

        [TestMethod, Description("A certificate which cannot be loaded is rejected with the password guidance")]
        public async Task UnloadableAssetIsRejectedWithPasswordGuidance()
        {
            var manager = new CertifyManager();
            var item = CreateSubscription();

            var result = await InvokeValidate(manager, item, WriteCorruptAsset());

            Assert.IsFalse(result.IsValid);

            // the usual cause is the source using a different PFX password credential, so the message points there
            // rather than reporting an unexplained parse failure
            Assert.AreEqual(CertifyManager.SubscriptionPfxLoadErrorMessage, result.Message);
        }

        [TestMethod, Description("A certificate asset which is not there is rejected rather than throwing")]
        public async Task MissingAssetIsRejected()
        {
            var manager = new CertifyManager();
            var item = CreateSubscription();

            var missingPath = Path.Combine(Path.GetTempPath(), $"certify-missing-{Guid.NewGuid():N}.pfx");

            var result = await InvokeValidate(manager, item, missingPath);

            Assert.IsFalse(result.IsValid, "A missing asset is a failed validation, not an unhandled exception in the subscription pass");
            Assert.AreEqual(CertifyManager.SubscriptionPfxLoadErrorMessage, result.Message);
        }

        [TestMethod, Description("Applying a fetched certificate records the details the item is tracked by")]
        public async Task ApplyingMetadataRecordsTheNewCertificateDetails()
        {
            var manager = new CertifyManager();
            var item = CreateSubscription();

            item.CertificateThumbprintHash = "PREVIOUS-THUMBPRINT";
            item.CertificatePEM = "stale pem content";
            item.CertificateRevoked = true;
            item.DateNextScheduledRenewalAttempt = DateTimeOffset.UtcNow;

            var notBefore = DateTimeOffset.UtcNow.AddDays(-2);
            var notAfter = DateTimeOffset.UtcNow.AddDays(88);
            var assetPath = WriteCertificateAsset("applied.example.com", notBefore, notAfter);

            Assert.IsTrue(await InvokeApplyMetadata(manager, item, assetPath));

            Assert.AreEqual(assetPath, item.CertificatePath);
            Assert.AreEqual("PREVIOUS-THUMBPRINT", item.CertificatePreviousThumbprintHash, "The thumbprint being replaced is kept, so the previous certificate can be cleaned up");
            Assert.AreNotEqual("PREVIOUS-THUMBPRINT", item.CertificateThumbprintHash);
            Assert.IsNull(item.CertificatePEM, "The PEM of the previous certificate must not be left describing the new one");
            Assert.IsFalse(item.CertificateRevoked, "The certificate just fetched is not the revoked one");
            Assert.AreEqual(notAfter.UtcDateTime.Date, item.DateExpiry?.UtcDateTime.Date);
            Assert.AreEqual(notBefore.UtcDateTime.Date, item.DateStart?.UtcDateTime.Date);
            Assert.IsNotNull(item.DateRenewed);
            Assert.IsNotNull(item.DateRetrieved);

            // a certificate has just been retrieved, so any renewal attempt scheduled to go and get one no longer applies
            Assert.IsNull(item.DateNextScheduledRenewalAttempt);
        }

        [TestMethod, Description("Applying a fetched certificate takes its identifiers so bindings can be matched")]
        public async Task ApplyingMetadataAppliesTheCertificateIdentifiers()
        {
            var manager = new CertifyManager();
            var item = CreateSubscription();

            var assetPath = WriteCertificateAsset(
                "primary.example.com",
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(89),
                dnsNames: new[] { "primary.example.com", "alt.example.com" });

            Assert.IsTrue(await InvokeApplyMetadata(manager, item, assetPath));

            // the subscription never ran a request of its own, so these identifiers are the only thing binding
            // deployment has to match server hostname bindings against
            var identifiers = item.GetCertificateIdentifiers().Select(i => i.Value).ToList();

            Assert.Contains("primary.example.com", identifiers);
            Assert.Contains("alt.example.com", identifiers);
        }

        [TestMethod, Description("A certificate which cannot be parsed leaves the item's recorded certificate alone")]
        public async Task ApplyingMetadataFromAnUnreadableAssetLeavesTheItemAlone()
        {
            var manager = new CertifyManager();
            var item = CreateSubscription();

            item.CertificateThumbprintHash = "CURRENT-THUMBPRINT";
            item.CertificatePath = "existing-path.pfx";

            Assert.IsFalse(await InvokeApplyMetadata(manager, item, WriteCorruptAsset()));

            // the caller records this as a deployment failure and retries later, so the item must still describe the
            // certificate it actually holds
            Assert.AreEqual("CURRENT-THUMBPRINT", item.CertificateThumbprintHash);
            Assert.AreEqual("existing-path.pfx", item.CertificatePath);
        }

        [TestMethod, Description("Every DNS name in the certificate is taken as an identifier")]
        public void AllSanDnsNamesAreExtracted()
        {
            var assetPath = WriteCertificateAsset(
                "primary.example.com",
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(89),
                dnsNames: new[] { "primary.example.com", "www.example.com", "api.example.com" });

            using var cert = CertificateManager.LoadCertificate(assetPath, throwOnError: true);

            var identifiers = InvokeExtractIdentifiers(cert);
            var dnsValues = identifiers.Where(i => i.IdentifierType == CertIdentifierType.Dns).Select(i => i.Value).ToList();

            CollectionAssert.AreEquivalent(
                new[] { "primary.example.com", "www.example.com", "api.example.com" },
                dnsValues,
                "A binding for any name in the certificate has to be matchable");
        }

        [TestMethod, Description("IP address identifiers in the certificate are extracted alongside the DNS names")]
        public void SanIpAddressesAreExtracted()
        {
            var assetPath = WriteCertificateAsset(
                "primary.example.com",
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(89),
                dnsNames: new[] { "primary.example.com" },
                ipAddresses: new[] { "192.0.2.10" });

            using var cert = CertificateManager.LoadCertificate(assetPath, throwOnError: true);

            var identifiers = InvokeExtractIdentifiers(cert);

            Assert.IsTrue(identifiers.Any(i => i.IdentifierType == CertIdentifierType.Dns && i.Value == "primary.example.com"));
            Assert.IsTrue(identifiers.Any(i => i.IdentifierType == CertIdentifierType.Ip && i.Value == "192.0.2.10"),
                "An IP binding is matched from the certificate's IP identifiers");
        }

        [TestMethod, Description("A certificate with no subject alternative names falls back to its common name")]
        public void CommonNameIsUsedWhenThereAreNoSubjectAlternativeNames()
        {
            var assetPath = WriteCertificateAsset("legacy.example.com", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(89));

            using var cert = CertificateManager.LoadCertificate(assetPath, throwOnError: true);

            var identifiers = InvokeExtractIdentifiers(cert);

            Assert.HasCount(1, identifiers);
            Assert.AreEqual(CertIdentifierType.Dns, identifiers[0].IdentifierType);
            Assert.AreEqual("legacy.example.com", identifiers[0].Value, "Without a SAN the common name is the only name the certificate can be matched by");
        }

        [TestMethod, Description("A certificate with only IP identifiers still contributes its common name")]
        public void CommonNameIsAddedWhenTheCertificateOnlyHasIpIdentifiers()
        {
            var assetPath = WriteCertificateAsset(
                "iponly.example.com",
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(89),
                ipAddresses: new[] { "192.0.2.20" });

            using var cert = CertificateManager.LoadCertificate(assetPath, throwOnError: true);

            var identifiers = InvokeExtractIdentifiers(cert);

            Assert.AreEqual(CertIdentifierType.Dns, identifiers[0].IdentifierType, "The fallback name leads, so it is treated as the primary identifier");
            Assert.AreEqual("iponly.example.com", identifiers[0].Value);
            Assert.IsTrue(identifiers.Any(i => i.IdentifierType == CertIdentifierType.Ip && i.Value == "192.0.2.20"));
        }
    }
}
