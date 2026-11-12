using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Certify.Management;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

namespace Certify.Tests.Core.Unit.Tests
{
    [TestClass]
    public class CertificateLoadTests
    {
        private const string FriendlyName = "load.test [Certify] - 2026-11-12 to 2026-12-12";

        private static (string path, string thumbprint) CreatePfx(string password, bool modernAlgorithms)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=load.test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

            // built the same way as issued certificates
            var builder = new Pkcs12StoreBuilder();
            builder.SetCertAlgorithm(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc);

            if (modernAlgorithms)
            {
                builder.SetKeyAlgorithm(NistObjectIdentifiers.IdAes256Cbc, PkcsObjectIdentifiers.IdHmacWithSha256);
            }
            else
            {
                builder.SetKeyAlgorithm(PkcsObjectIdentifiers.PbewithShaAnd40BitRC2Cbc);
            }

            var store = builder.Build();
            var entry = new X509CertificateEntry(DotNetUtilities.FromX509Certificate(cert));
            store.SetCertificateEntry(FriendlyName, entry);
            store.SetKeyEntry(FriendlyName, new AsymmetricKeyEntry(DotNetUtilities.GetRsaKeyPair(rsa).Private), new[] { entry });

            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pfx");
            using (var output = File.Create(path))
            {
                store.Save(output, password.ToCharArray(), new SecureRandom());
            }

            return (path, cert.Thumbprint);
        }

        [TestMethod, Description("The end entity certificate and its friendly name are read from a PFX without its private key")]
        [DataRow(false, DisplayName = "Legacy algorithms")]
        [DataRow(true, DisplayName = "Modern algorithms")]
        public void LoadCertificateWithoutKey_ReadsCertificateOnly(bool modernAlgorithms)
        {
            var (path, thumbprint) = CreatePfx("secret", modernAlgorithms);

            try
            {
                using var cert = CertificateManager.LoadCertificateWithoutKey(path, "secret", out var friendlyName);

                Assert.AreEqual(thumbprint, cert.Thumbprint);
                Assert.AreEqual(FriendlyName, friendlyName);
                Assert.IsFalse(cert.HasPrivateKey);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod, Description("A PFX with an empty password can be read with an empty or absent password")]
        public void LoadCertificateWithoutKey_AcceptsEmptyOrAbsentPassword()
        {
            var (path, thumbprint) = CreatePfx("", modernAlgorithms: false);

            try
            {
                using var withEmpty = CertificateManager.LoadCertificateWithoutKey(path, "");
                using var withNull = CertificateManager.LoadCertificateWithoutKey(path, null);

                Assert.AreEqual(thumbprint, withEmpty.Thumbprint);
                Assert.AreEqual(thumbprint, withNull.Thumbprint);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod, Description("A PFX cannot be read with the wrong password")]
        public void LoadCertificateWithoutKey_RejectsWrongPassword()
        {
            var (path, _) = CreatePfx("secret", modernAlgorithms: true);

            try
            {
                Assert.ThrowsExactly<CryptographicException>(() => CertificateManager.LoadCertificateWithoutKey(path, "wrong"));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
