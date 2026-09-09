using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for selection of the windows CSP/CNG key storage provider used when importing a certificate private key.
    /// </summary>
    /// <remarks>
    /// The .NET 9+ pkcs12 loader imports every private key into the CNG 'Microsoft Software Key Storage Provider'
    /// unless the pfx declares a provider and the loader is asked to preserve it. Consumers which only speak legacy
    /// CryptoAPI cannot see CNG keys, so the provider needs to be selectable.
    /// </remarks>
    [TestClass]
    public class CertificateKeyStorageProviderTests
    {
        // user keyset rather than machine keyset so the tests do not require elevation. The key must be persisted for
        // the provider to be observable at all (an ephemeral key set is always CNG), so each test deletes it again.
        private const X509KeyStorageFlags TestStorageFlags = X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable;

        private static readonly Pkcs12LoaderLimits PreserveStorageProvider = new Pkcs12LoaderLimits(Pkcs12LoaderLimits.Defaults) { PreserveStorageProvider = true };

        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>
        /// Build a pfx containing a fresh self signed cert and its private key, without leaving the generated key on disk
        /// </summary>
        private static byte[] CreateTestPfx(string domain)
        {
            var cert = CertificateManager.GenerateSelfSignedCertificate(domain, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

            try
            {
                return cert.Export(X509ContentType.Pkcs12, "");
            }
            finally
            {
                DeletePersistedKey(cert);
                cert.Dispose();
            }
        }

        private const int PROV_RSA_FULL = 1;
        private const int PROV_RSA_SCHANNEL = 12;

        /// <summary>
        /// Remove the private key this certificate persisted on import, so tests do not leave key files behind
        /// </summary>
        private static void DeletePersistedKey(X509Certificate2 cert)
        {
            string providerName;
            string containerName;

            using (var rsa = cert.GetRSAPrivateKey())
            {
                if (rsa is not RSACng cng)
                {
                    return;
                }

                if (cng.Key.Provider?.Provider == WindowsKeyStorageProviders.SoftwareKeyStorageProvider)
                {
                    cng.Key.Delete();
                    return;
                }

                providerName = cng.Key.Provider?.Provider;
                containerName = cng.Key.KeyName;
            }

            // legacy CSP keys are only reachable through CryptoAPI, CngKey.Delete reports success but leaves the key file
            var providerType = providerName == WindowsKeyStorageProviders.RsaSChannelCryptographicProvider ? PROV_RSA_SCHANNEL : PROV_RSA_FULL;
            var cspParams = new CspParameters(providerType, providerName, containerName) { Flags = CspProviderFlags.UseExistingKey };

            using var csp = new RSACryptoServiceProvider(cspParams) { PersistKeyInCsp = false };
            csp.Clear();
        }

        /// <summary>
        /// Import the pfx, report which key storage provider the private key ended up in and its export policy, then
        /// delete the imported key
        /// </summary>
        private static (string providerName, CngExportPolicies? exportPolicy) ImportAndInspectKey(byte[] pfxData)
        {
            using var cert = X509CertificateLoader.LoadPkcs12(pfxData, "", TestStorageFlags, PreserveStorageProvider);

            try
            {
                using var rsa = cert.GetRSAPrivateKey();

                return rsa switch
                {
                    RSACng cng => (cng.Key.Provider?.Provider, cng.Key.ExportPolicy),
                    RSACryptoServiceProvider csp => (csp.CspKeyContainerInfo.ProviderName, null),
                    _ => (null, null)
                };
            }
            finally
            {
                DeletePersistedKey(cert);
            }
        }

        [TestMethod, Description("Private keys import into the CNG key storage provider when no provider is requested")]
        public void ImportWithoutRequestedProviderUsesCng()
        {
            if (!IsWindows)
            {
                Debug.WriteLine("Test only valid on Windows, skipping");
                return;
            }

            var pfxData = CreateTestPfx($"ksp-default-{Guid.NewGuid():N}.test.com");

            Assert.AreEqual(WindowsKeyStorageProviders.SoftwareKeyStorageProvider, ImportAndInspectKey(pfxData).providerName);
        }

        [TestMethod, Description("Private keys import into the requested key storage provider")]
        [DataRow(WindowsKeyStorageProviders.EnhancedCryptographicProvider)]
        [DataRow(WindowsKeyStorageProviders.RsaSChannelCryptographicProvider)]
        [DataRow(WindowsKeyStorageProviders.SoftwareKeyStorageProvider)]
        public void ImportWithRequestedProviderUsesThatProvider(string requestedProvider)
        {
            if (!IsWindows)
            {
                Debug.WriteLine("Test only valid on Windows, skipping");
                return;
            }

            var pfxData = CreateTestPfx($"ksp-requested-{Guid.NewGuid():N}.test.com");

            var stampedPfxData = CertificateManager.GetPfxDataWithKeyProviderName(requestedProvider, pfxData, "");

            Assert.AreEqual(requestedProvider, ImportAndInspectKey(stampedPfxData).providerName);
        }

        [TestMethod, Description("Legacy CSP keys allow plain text export, CNG keys do not")]
        public void LegacyCspKeysArePlainTextExportable()
        {
            if (!IsWindows)
            {
                Debug.WriteLine("Test only valid on Windows, skipping");
                return;
            }

            var pfxData = CreateTestPfx($"ksp-export-{Guid.NewGuid():N}.test.com");

            // CNG keys are exportable but not in plain text, which is what blocks legacy export tooling
            Assert.AreEqual(CngExportPolicies.AllowExport, ImportAndInspectKey(pfxData).exportPolicy);

            var legacyPfxData = CertificateManager.GetPfxDataWithKeyProviderName(WindowsKeyStorageProviders.EnhancedCryptographicProvider, pfxData, "");

            Assert.IsTrue(ImportAndInspectKey(legacyPfxData).exportPolicy?.HasFlag(CngExportPolicies.AllowPlaintextExport) == true, "Legacy CSP keys should remain plain text exportable");
        }

        [TestMethod, Description("Applying a key provider name preserves the certificate content and key pairing")]
        public void ApplyingKeyProviderNamePreservesCertificateContent()
        {
            var domain = $"ksp-content-{Guid.NewGuid():N}.test.com";
            var pfxData = CreateTestPfx(domain);

            using var original = X509CertificateLoader.LoadPkcs12(pfxData, "", X509KeyStorageFlags.EphemeralKeySet);

            var stampedPfxData = CertificateManager.GetPfxDataWithKeyProviderName(WindowsKeyStorageProviders.EnhancedCryptographicProvider, pfxData, "");

            using var stamped = X509CertificateLoader.LoadPkcs12(stampedPfxData, "", X509KeyStorageFlags.EphemeralKeySet);

            Assert.AreEqual(original.Thumbprint, stamped.Thumbprint);
            Assert.IsTrue(stamped.HasPrivateKey);
            Assert.IsTrue(CertificateManager.VerifyCertificateSAN(stamped, domain));
        }

        [TestMethod, Description("A pfx which cannot be read throws, so callers can fall back to the unmodified pfx")]
        public void ApplyingKeyProviderNameWithWrongPasswordThrows()
        {
            var pfxData = CreateTestPfx($"ksp-pwd-{Guid.NewGuid():N}.test.com");

            // callers rely on this throwing so they can fall back to the unmodified pfx and let the import report the problem
            Assert.Throws<Exception>(
                () => CertificateManager.GetPfxDataWithKeyProviderName(WindowsKeyStorageProviders.EnhancedCryptographicProvider, pfxData, "not-the-password"));
        }
    }
}
