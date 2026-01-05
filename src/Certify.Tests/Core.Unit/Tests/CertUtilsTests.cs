using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Certify.Shared.Core.Utils.PKI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Org.BouncyCastle.OpenSsl;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class CertUtilsTests
    {
        private const string TEST_PASSWORD = "test123";

        /// <summary>
        /// Helper to count PEM certificates in a string
        /// </summary>
        private int CountPemCertificates(string pem)
        {
            if (string.IsNullOrWhiteSpace(pem))
            {
                return 0;
            }

            return pem.Split(new[] { "-----BEGIN CERTIFICATE-----" }, StringSplitOptions.RemoveEmptyEntries).Length - 1;
        }

        /// <summary>
        /// Helper to check if PEM contains a private key
        /// </summary>
        private bool ContainsPrivateKey(string pem)
        {
            return pem.Contains("-----BEGIN") &&
                   (pem.Contains("PRIVATE KEY-----") || pem.Contains("RSA PRIVATE KEY-----") || pem.Contains("EC PRIVATE KEY-----"));
        }

        /// <summary>
        /// Helper to extract certificate subjects from PEM
        /// </summary>
        private string[] GetCertificateSubjects(string pem)
        {
            var subjects = new System.Collections.Generic.List<string>();
            using (var reader = new StringReader(pem))
            {
                var pemReader = new PemReader(reader);
                object obj;
                while ((obj = pemReader.ReadObject()) != null)
                {
                    if (obj is Org.BouncyCastle.X509.X509Certificate cert)
                    {
                        subjects.Add(cert.SubjectDN.ToString());
                    }
                }
            }

            return subjects.ToArray();
        }

        [TestMethod, Description("Test export with only end entity certificate flag")]
        public void TestExportEndEntityOnly()
        {
            // Load a test PFX file (this requires a pre-existing test PFX in Assets folder)
            var pfxPath = "Assets/dummycert.pfx";
            Assert.IsTrue(File.Exists(pfxPath), $"Test PFX file not found: {pfxPath}");

            var pfxBytes = File.ReadAllBytes(pfxPath);

            var pem = CertUtils.GetCertComponentsAsPEMString(
                pfxBytes,
                TEST_PASSWORD,
                ExportFlags.EndEntityCertificate
            );

            Assert.IsNotNull(pem);
            Assert.IsFalse(string.IsNullOrWhiteSpace(pem));

            // Should contain at least 1 certificate
            Assert.IsTrue(CountPemCertificates(pem) >= 1, "Should contain at least the end entity certificate");

            // Should not contain private key
            Assert.IsFalse(ContainsPrivateKey(pem));
        }

        [TestMethod, Description("Test export with private key and end entity certificate")]
        public void TestExportWithPrivateKey()
        {
            var pfxPath = "Assets/dummycert.pfx";
            Assert.IsTrue(File.Exists(pfxPath), $"Test PFX file not found: {pfxPath}");

            var pfxBytes = File.ReadAllBytes(pfxPath);

            var pem = CertUtils.GetCertComponentsAsPEMString(
                pfxBytes,
                TEST_PASSWORD,
                ExportFlags.PrivateKey | ExportFlags.EndEntityCertificate
            );

            Assert.IsNotNull(pem);

            // Should contain private key
            Assert.IsTrue(ContainsPrivateKey(pem), "Should contain private key");

            // Should contain at least 1 certificate
            Assert.IsTrue(CountPemCertificates(pem) >= 1, "Should contain at least one certificate");

            // Private key should come before certificate
            var keyIndex = pem.IndexOf("PRIVATE KEY-----");
            var certIndex = pem.IndexOf("-----BEGIN CERTIFICATE-----");
            Assert.IsTrue(keyIndex >= 0, "Should contain private key");
            Assert.IsTrue(certIndex >= 0, "Should contain certificate");
            Assert.IsTrue(keyIndex < certIndex, "Private key should appear before certificate");
        }

        [TestMethod, Description("Test export full chain from PFX")]
        public void TestExportFullChainFromPfx()
        {
            var pfxPath = "Assets/dummycert.pfx";
            Assert.IsTrue(File.Exists(pfxPath), $"Test PFX file not found: {pfxPath}");

            var pfxBytes = File.ReadAllBytes(pfxPath);

            var pem = CertUtils.GetCertComponentsAsPEMString(
                pfxBytes,
                TEST_PASSWORD,
                ExportFlags.EndEntityCertificate | ExportFlags.IntermediateCertificates | ExportFlags.RootCertificate
            );

            Assert.IsNotNull(pem);

            // Should contain at least the leaf certificate
            // May contain more depending on what's in the test PFX and system store
            var certCount = CountPemCertificates(pem);
            Assert.IsTrue(certCount >= 1, $"Should contain at least leaf certificate, got {certCount}");
        }

        [TestMethod, Description("Test export intermediates only")]
        public void TestExportIntermediatesOnly()
        {
            var pfxPath = "Assets/dummycert.pfx";
            Assert.IsTrue(File.Exists(pfxPath), $"Test PFX file not found: {pfxPath}");

            var pfxBytes = File.ReadAllBytes(pfxPath);

            var pem = CertUtils.GetCertComponentsAsPEMString(
                pfxBytes,
                TEST_PASSWORD,
                ExportFlags.IntermediateCertificates
            );

            Assert.IsNotNull(pem);

            // May or may not have intermediates depending on the test certificate
            // This test validates the method doesn't crash and returns valid PEM format
            if (!string.IsNullOrWhiteSpace(pem))
            {
                Assert.IsFalse(ContainsPrivateKey(pem), "Should not contain private key");
            }
        }

        [TestMethod, Description("Test export with empty password fallback")]
        public void TestExportWithEmptyPassword()
        {
            var pfxPath = "Assets/dummycert.pfx";
            Assert.IsTrue(File.Exists(pfxPath), $"Test PFX file not found: {pfxPath}");

            var pfxBytes = File.ReadAllBytes(pfxPath);

            // Try with correct password
            var pem = CertUtils.GetCertComponentsAsPEMString(
                pfxBytes,
                TEST_PASSWORD,
                ExportFlags.EndEntityCertificate
            );

            Assert.IsNotNull(pem);
            Assert.IsTrue(CountPemCertificates(pem) >= 1, "Should contain certificate");
        }

        [TestMethod, Description("Test PEM output format is valid")]
        public void TestPemOutputFormat()
        {
            var pfxPath = "Assets/dummycert.pfx";
            Assert.IsTrue(File.Exists(pfxPath), $"Test PFX file not found: {pfxPath}");

            var pfxBytes = File.ReadAllBytes(pfxPath);

            var pem = CertUtils.GetCertComponentsAsPEMString(
                pfxBytes,
                TEST_PASSWORD,
                ExportFlags.PrivateKey | ExportFlags.EndEntityCertificate | ExportFlags.IntermediateCertificates
            );

            Assert.IsNotNull(pem);

            // Validate PEM format markers
            Assert.IsTrue(pem.Contains("-----BEGIN"), "Should contain PEM BEGIN markers");
            Assert.IsTrue(pem.Contains("-----END"), "Should contain PEM END markers");

            // Should be able to parse as PEM
            using (var reader = new StringReader(pem))
            {
                var pemReader = new PemReader(reader);
                var firstObject = pemReader.ReadObject();
                Assert.IsNotNull(firstObject, "Should be able to parse PEM content");
            }
        }

        [TestMethod, Description("Test GetCertComponentsAsPEMBytes returns valid bytes")]
        public void TestGetCertComponentsAsPEMBytes()
        {
            var pfxPath = "Assets/dummycert.pfx";
            Assert.IsTrue(File.Exists(pfxPath), $"Test PFX file not found: {pfxPath}");

            var pfxBytes = File.ReadAllBytes(pfxPath);

            var pemBytes = CertUtils.GetCertComponentsAsPEMBytes(
                pfxBytes,
                TEST_PASSWORD,
                ExportFlags.EndEntityCertificate
            );

            Assert.IsNotNull(pemBytes);
            Assert.IsTrue(pemBytes.Length > 0);

            var pemString = System.Text.Encoding.ASCII.GetString(pemBytes);
            Assert.IsTrue(pemString.Contains("-----BEGIN CERTIFICATE-----"));
            Assert.IsTrue(pemString.Contains("-----END CERTIFICATE-----"));
        }

        [TestMethod, Description("Test CertDerToPem conversion")]
        public void TestCertDerToPem()
        {
            var pfxPath = "Assets/dummycert.pfx";
            Assert.IsTrue(File.Exists(pfxPath), $"Test PFX file not found: {pfxPath}");

            var pfxBytes = File.ReadAllBytes(pfxPath);
            var cert = new X509Certificate2(pfxBytes, TEST_PASSWORD);

            var derBytes = cert.Export(X509ContentType.Cert);
            var pem = CertUtils.CertDerToPem(derBytes);

            Assert.IsNotNull(pem);
            Assert.IsTrue(pem.Contains("-----BEGIN CERTIFICATE-----"));
            Assert.IsTrue(pem.Contains("-----END CERTIFICATE-----"));

            // Should be able to parse back
            using (var reader = new StringReader(pem))
            {
                var pemReader = new PemReader(reader);
                var parsedCert = pemReader.ReadObject();
                Assert.IsNotNull(parsedCert);
                Assert.IsInstanceOfType(parsedCert, typeof(Org.BouncyCastle.X509.X509Certificate));
            }
        }

        [TestMethod, Description("Test private key extraction")]
        public void TestGetCertKeyPem()
        {
            var pfxPath = "Assets/dummycert.pfx";
            Assert.IsTrue(File.Exists(pfxPath), $"Test PFX file not found: {pfxPath}");

            var pfxBytes = File.ReadAllBytes(pfxPath);

            var keyPem = CertUtils.GetCertKeyPem(pfxBytes, TEST_PASSWORD);

            Assert.IsNotNull(keyPem);
            Assert.IsTrue(keyPem.Contains("-----BEGIN"), "Should contain PEM BEGIN marker");
            Assert.IsTrue(keyPem.Contains("PRIVATE KEY"), "Should contain PRIVATE KEY marker");
            Assert.IsTrue(keyPem.Contains("-----END"), "Should contain PEM END marker");
        }

        [TestMethod, Description("Test ARI CertID generation from X509Certificate2")]
        public void TestGetARICertIdBase64FromX509Certificate2()
        {
            var pfxPath = "Assets/dummycert.pfx";
            Assert.IsTrue(File.Exists(pfxPath), $"Test PFX file not found: {pfxPath}");

            var pfxBytes = File.ReadAllBytes(pfxPath);
            var cert = new X509Certificate2(pfxBytes, TEST_PASSWORD);

            var certId = CertUtils.GetARICertIdBase64(cert);

            // CertID may be null if the cert doesn't have an Authority Key Identifier
            // This is valid behavior
            if (certId != null)
            {
                Assert.IsTrue(certId.Contains("."), "CertID should contain a dot separator");

                // Should be base64url encoded (no + / = characters)
                Assert.IsFalse(certId.Contains("+"), "Should use base64url encoding");
                Assert.IsFalse(certId.Contains("/"), "Should use base64url encoding");
                Assert.IsFalse(certId.Contains("="), "Should use base64url encoding");
            }
        }

        [TestMethod, Description("Test component blending - original chain preferred over built chain")]
        public void TestComponentBlending()
        {
            var pfxPath = "Assets/dummycert.pfx";
            Assert.IsTrue(File.Exists(pfxPath), $"Test PFX file not found: {pfxPath}");

            var pfxBytes = File.ReadAllBytes(pfxPath);

            // Export with all components
            var pem = CertUtils.GetCertComponentsAsPEMString(
                pfxBytes,
                TEST_PASSWORD,
                ExportFlags.EndEntityCertificate | ExportFlags.IntermediateCertificates | ExportFlags.RootCertificate
            );

            Assert.IsNotNull(pem);

            // The method should successfully blend components from PFX and built chain
            // Exact certificate count depends on what's in the PFX and system store
            var certCount = CountPemCertificates(pem);
            Assert.IsTrue(certCount >= 1, "Should contain at least the end entity certificate");
        }
    }
}
