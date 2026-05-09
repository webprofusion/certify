using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Certify.Management;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    [TestClass]
    public class CertificateIntermediateStoreTests
    {
        private const string TestPassword = "";

        [TestMethod, Description("Extracting intermediates from a PFX excludes the end entity certificate")]
        public void GetIntermediateCertificatesFromPfx_ExcludesEndEntityCertificate()
        {
            var pfxPath = "Assets/dummycert.pfx";
            Assert.IsTrue(File.Exists(pfxPath), $"Test PFX file not found: {pfxPath}");

            using var certificate = CertificateManager.LoadCertificate(pfxPath, TestPassword, throwOnError: true, ephemeralKeySet: true);

            var intermediates = CertificateManager.GetIntermediateCertificatesFromPfx(pfxPath, TestPassword, certificate.Thumbprint);

            Assert.IsFalse(intermediates.Any(c => c.Thumbprint == certificate.Thumbprint), "End entity certificate should not be returned as an intermediate.");

            foreach (var intermediate in intermediates)
            {
                intermediate.Dispose();
            }
        }

        [TestMethod, Description("Extracting intermediates from a PFX only returns CA certificates and skips self-signed roots")]
        public void GetIntermediateCertificatesFromPfx_ReturnsOnlyNonRootCaCertificates()
        {
            var pfxPath = "Assets/dummycert.pfx";
            Assert.IsTrue(File.Exists(pfxPath), $"Test PFX file not found: {pfxPath}");

            using var certificate = CertificateManager.LoadCertificate(pfxPath, TestPassword, throwOnError: true, ephemeralKeySet: true);

            var intermediates = CertificateManager.GetIntermediateCertificatesFromPfx(pfxPath, TestPassword, certificate.Thumbprint);

            foreach (var intermediate in intermediates)
            {
                try
                {
                    Assert.AreNotEqual(intermediate.Subject, intermediate.Issuer, "Self-signed roots should not be returned as intermediates.");
                    Assert.IsTrue(intermediate.Extensions.OfType<X509BasicConstraintsExtension>().Any(e => e.CertificateAuthority), "Intermediate certificates should be CA certificates.");
                }
                finally
                {
                    intermediate.Dispose();
                }
            }
        }

        [TestMethod, Description("Extracting intermediates from a PFX returns unique certificates by thumbprint")]
        public void GetIntermediateCertificatesFromPfx_ReturnsUniqueCertificates()
        {
            var pfxPath = "Assets/dummycert.pfx";
            Assert.IsTrue(File.Exists(pfxPath), $"Test PFX file not found: {pfxPath}");

            using var certificate = CertificateManager.LoadCertificate(pfxPath, TestPassword, throwOnError: true, ephemeralKeySet: true);

            var intermediates = CertificateManager.GetIntermediateCertificatesFromPfx(pfxPath, TestPassword, certificate.Thumbprint);

            try
            {
                Assert.AreEqual(intermediates.Count, intermediates.Select(c => c.Thumbprint).Distinct().Count(), "Intermediates should be unique by thumbprint.");
            }
            finally
            {
                foreach (var intermediate in intermediates)
                {
                    intermediate.Dispose();
                }
            }
        }
    }
}
