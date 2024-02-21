using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class CertificateOperationTests
    {
        [TestMethod, Description("Test self signed cert")]
        public void TestSelfSignedCertCreate()
        {

            var cert = CertificateManager.GenerateSelfSignedCertificate("test.com", new DateTime(1934, 01, 01), new DateTime(1934, 03, 01), suffix: "[Certify](test)");
            Assert.IsNotNull(cert);
        }

        [TestMethod, Description("Test self signed cert storage")]
        public void TestSelfSignedCertCreateAndStore()
        {

            var cert = CertificateManager.GenerateSelfSignedCertificate("test.com", new DateTime(1934, 01, 01), new DateTime(1934, 03, 01), suffix: "[Certify](test)");
            Assert.IsNotNull(cert);

            CertificateManager.StoreCertificate(cert, CertificateManager.DEFAULT_STORE_NAME);

            var storedCert = CertificateManager.GetCertificateByThumbprint(cert.Thumbprint, CertificateManager.DEFAULT_STORE_NAME);
            Assert.IsNotNull(storedCert);

            CertificateManager.RemoveCertificate(storedCert, CertificateManager.DEFAULT_STORE_NAME);
        }

        [TestMethod, Description("Test localhost cert")]
        public void TestSelfSignedLocalhostCertCreateAndStore()
        {

            var cert = CertificateManager.GenerateSelfSignedCertificate("localhost", DateTime.UtcNow, DateTime.UtcNow.AddDays(30), suffix: "[Certify](test)");
            Assert.IsNotNull(cert);

            CertificateManager.StoreCertificate(cert, CertificateManager.DEFAULT_STORE_NAME);

            var storedCert = CertificateManager.GetCertificateByThumbprint(cert.Thumbprint, CertificateManager.DEFAULT_STORE_NAME);
            Assert.IsNotNull(storedCert);

            CertificateManager.RemoveCertificate(storedCert, CertificateManager.DEFAULT_STORE_NAME);
        }

        [TestMethod, Description("Test get cert RSA private key file path")]
        public void TestGetRSAPrivateKeyPath()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Debug.WriteLine("Test only valid on Windows, skipping");
                return;
            }

            var cert = CertificateManager.GenerateSelfSignedCertificate("localhost", DateTime.UtcNow, DateTime.UtcNow.AddDays(30), suffix: "[Certify](test)", keyType: StandardKeyTypes.RSA256);

            CertificateManager.StoreCertificate(cert, CertificateManager.DEFAULT_STORE_NAME);

            var storedCert = CertificateManager.GetCertificateByThumbprint(cert.Thumbprint, CertificateManager.DEFAULT_STORE_NAME);
            Assert.IsNotNull(storedCert);

            try
            {
                var path = CertificateManager.GetCertificatePrivateKeyPath(storedCert);
                Assert.IsNotNull(path);
            }
            finally
            {
                CertificateManager.RemoveCertificate(storedCert, CertificateManager.DEFAULT_STORE_NAME);
            }
        }

        [TestMethod, Description("Test get cert ECDSA private key file path")]
        public void TestGetECDSAPrivateKeyPath()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Debug.WriteLine("Test only valid on Windows, skipping");
                return;
            }

            var cert = CertificateManager.GenerateSelfSignedCertificate("localhost", DateTime.UtcNow, DateTime.UtcNow.AddDays(30), suffix: "[Certify](test)", keyType: StandardKeyTypes.ECDSA256);

            CertificateManager.StoreCertificate(cert, CertificateManager.DEFAULT_STORE_NAME);

            var storedCert = CertificateManager.GetCertificateByThumbprint(cert.Thumbprint, CertificateManager.DEFAULT_STORE_NAME);
            Assert.IsNotNull(storedCert);

            try
            {
                var path = CertificateManager.GetCertificatePrivateKeyPath(storedCert);
                Assert.IsNotNull(path);
            }
            finally
            {
                CertificateManager.RemoveCertificate(storedCert, CertificateManager.DEFAULT_STORE_NAME);
            }
        }
    }
}
