using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Certify.ACME.Anvil;
using Certify.ACME.Anvil.Pkcs;
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
        /// Build a pfx containing a fresh self signed cert and its private key the same way certificate requests do, which
        /// does not declare a key storage provider
        /// </summary>
        private static byte[] CreateTestPfx(string domain, bool useEcdsa = false)
        {
            using AsymmetricAlgorithm key = useEcdsa ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : RSA.Create(2048);

            var request = key is ECDsa ecdsa
                ? new CertificateRequest($"CN={domain}", ecdsa, HashAlgorithmName.SHA256)
                : new CertificateRequest($"CN={domain}", (RSA)key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName(domain);
            request.CertificateExtensions.Add(san.Build());

            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

            return new PfxBuilder(cert.RawData, KeyFactory.FromDer(key.ExportPkcs8PrivateKey()))
                .Build(domain, "", skipChainBuild: true);
        }

        private const int PROV_RSA_FULL = 1;
        private const int PROV_RSA_SCHANNEL = 12;

        /// <summary>
        /// Remove the private key this certificate persisted on import, so tests do not leave key files behind
        /// </summary>
        private static void DeletePersistedKey(X509Certificate2 cert)
        {
            using (var ecdsa = cert.GetECDsaPrivateKey())
            {
                if (ecdsa is ECDsaCng ecdsaCng)
                {
                    ecdsaCng.Key.Delete();
                    return;
                }
            }

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
        /// Import the pfx as the certificate store import does, declaring and honouring the requested provider if there
        /// is one. Report which key storage provider the private key ended up in and its export policy, then delete the
        /// imported key.
        /// </summary>
        private static (string providerName, CngExportPolicies? exportPolicy) ImportAndInspectKey(byte[] pfxData, string requestedProvider = null)
        {
            if (requestedProvider != null)
            {
                pfxData = CertificateManager.GetPfxDataWithKeyProviderName(requestedProvider, pfxData, "");
            }

            using var cert = X509CertificateLoader.LoadPkcs12(pfxData, "", TestStorageFlags, requestedProvider != null ? PreserveStorageProvider : null);

            try
            {
                using var key = (AsymmetricAlgorithm)cert.GetRSAPrivateKey() ?? cert.GetECDsaPrivateKey();

                return key switch
                {
                    RSACng cng => (cng.Key.Provider?.Provider, cng.Key.ExportPolicy),
                    ECDsaCng ecdsaCng => (ecdsaCng.Key.Provider?.Provider, ecdsaCng.Key.ExportPolicy),
                    RSACryptoServiceProvider csp => (csp.CspKeyContainerInfo.ProviderName, null),
                    _ => (null, null)
                };
            }
            finally
            {
                DeletePersistedKey(cert);
            }
        }

        [TestMethod, Description("Private keys import into the CNG key storage provider when no provider is requested, including when the pfx declares one")]
        public void ImportWithoutRequestedProviderUsesCng()
        {
            if (!IsWindows)
            {
                Debug.WriteLine("Test only valid on Windows, skipping");
                return;
            }

            var pfxData = CreateTestPfx($"ksp-default-{Guid.NewGuid():N}.test.com");

            Assert.AreEqual(WindowsKeyStorageProviders.SoftwareKeyStorageProvider, ImportAndInspectKey(pfxData).providerName);

            var legacyPfxData = CertificateManager.GetPfxDataWithKeyProviderName(WindowsKeyStorageProviders.EnhancedCryptographicProvider, pfxData, "");

            Assert.AreEqual(WindowsKeyStorageProviders.SoftwareKeyStorageProvider, ImportAndInspectKey(legacyPfxData).providerName, "A provider declared by the pfx itself should not be honoured unless one is requested");
        }

        [TestMethod, Description("Private keys import into the requested key storage provider")]
        [DataRow(WindowsKeyStorageProviders.EnhancedCryptographicProvider)]
        [DataRow(WindowsKeyStorageProviders.RsaSChannelCryptographicProvider)]
        public void ImportWithRequestedProviderUsesThatProvider(string requestedProvider)
        {
            if (!IsWindows)
            {
                Debug.WriteLine("Test only valid on Windows, skipping");
                return;
            }

            var pfxData = CreateTestPfx($"ksp-requested-{Guid.NewGuid():N}.test.com");

            Assert.AreEqual(requestedProvider, ImportAndInspectKey(pfxData, requestedProvider).providerName);
        }

        [TestMethod, Description("ECDSA keys requested into a legacy CSP import into CNG rather than failing")]
        public void ImportEcdsaWithLegacyProviderUsesCng()
        {
            if (!IsWindows)
            {
                Debug.WriteLine("Test only valid on Windows, skipping");
                return;
            }

            var pfxData = CreateTestPfx($"ksp-ecdsa-{Guid.NewGuid():N}.test.com", useEcdsa: true);

            Assert.AreEqual(WindowsKeyStorageProviders.SoftwareKeyStorageProvider, ImportAndInspectKey(pfxData, WindowsKeyStorageProviders.RsaSChannelCryptographicProvider).providerName);
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

            Assert.IsTrue(ImportAndInspectKey(pfxData, WindowsKeyStorageProviders.EnhancedCryptographicProvider).exportPolicy?.HasFlag(CngExportPolicies.AllowPlaintextExport) == true, "Legacy CSP keys should remain plain text exportable");
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
