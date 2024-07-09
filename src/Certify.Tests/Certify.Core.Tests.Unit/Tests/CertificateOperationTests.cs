using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Certify.Management;
using Certify.Models;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;

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

        [TestMethod, Description("Test private key set ACL")]
        [DataTestMethod]
        [DataRow("NT AUTHORITY\\LOCAL SERVICE", StandardKeyTypes.RSA256, "read", true, "RSA Key Type, Read")]
        [DataRow("NT AUTHORITY\\LOCAL SERVICE", StandardKeyTypes.RSA256, "fullcontrol", true, "RSA Key Type, Full Control")]
        [DataRow("NT AUTHORITY\\LOCAL SERVICE", StandardKeyTypes.ECDSA256, "read", true, "ECDSA Key Type, Read")]
        [DataRow("NT AUTHORITY\\LOCAL SERVICE", StandardKeyTypes.ECDSA256, "fullcontrol", true, "ECDSA Key Type, Full Control")]
        [DataRow("NT AUTHORITY\\MadeUpUser", StandardKeyTypes.ECDSA256, "fullcontrol", false, "ECDSA Key Type, Full Control, Invalid User")]
        public void TestSetACLOnPrivateKey(string account, string keyType, string fileSystemRights, bool isUserValid, string testDescription)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Debug.WriteLine("Test only valid on Windows, skipping");
                return;
            }

            var log = new Loggy(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<CertificateOperationTests>());

            var cert = CertificateManager.GenerateSelfSignedCertificate("localhost", DateTime.UtcNow, DateTime.UtcNow.AddDays(30), suffix: "[Certify](test)", keyType: keyType);

            CertificateManager.StoreCertificate(cert, CertificateManager.DEFAULT_STORE_NAME);

            var storedCert = CertificateManager.GetCertificateByThumbprint(cert.Thumbprint, CertificateManager.DEFAULT_STORE_NAME);
            Assert.IsNotNull(storedCert);

            try
            {

                var success = CertificateManager.GrantUserAccessToCertificatePrivateKey(storedCert, account, fileSystemRights: fileSystemRights, log);

                if (isUserValid)
                {
                    Assert.IsTrue(success, "Updating the ACL for the private key should succeed");

                    var hasAccess = CertificateManager.HasUserAccessToCertificatePrivateKey(storedCert, account, fileSystemRights: fileSystemRights, log);
                    Assert.IsTrue(hasAccess, "User should have the required access on the private key");
                }
                else
                {
                    Assert.IsFalse(success, "Updating the ACL for the private key should fail due to invalid user specified");
                }
            }
            finally
            {
                CertificateManager.RemoveCertificate(storedCert, CertificateManager.DEFAULT_STORE_NAME);
            }
        }
    }
}
