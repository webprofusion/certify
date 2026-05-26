using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Certify.Management;
using Certify.Models;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
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

        [TestMethod, Description("Manual deployment rerun should treat a completed certificate request as successful even if a deployment task later failed")]
        public void TestDeploymentTaskRerunUsesCertificateRequestSuccessWhenDeploymentPreviouslyFailed()
        {
            var managedCertificate = new ManagedCertificate
            {
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Error },
                LastRenewalStatus = RequestState.Error,
                DateRenewed = DateTimeOffset.UtcNow,
                DateExpiry = DateTimeOffset.UtcNow.AddDays(30),
                CertificateThumbprintHash = "thumbprint"
            };

            var result = InvokeWasLastCertificateRequestSuccessful(managedCertificate);

            Assert.IsTrue(result, "Manual task reruns should not be blocked when certificate issuance succeeded and only deployment failed.");
        }

        [TestMethod, Description("Manual deployment rerun should treat an explicit primary request success as authoritative")]
        public void TestDeploymentTaskRerunUsesExplicitPrimaryRequestSuccess()
        {
            var managedCertificate = new ManagedCertificate
            {
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success },
                LastRenewalStatus = RequestState.Error,
                DateRenewed = null,
                CertificateThumbprintHash = null,
                CertificatePath = null
            };

            var result = InvokeWasLastCertificateRequestSuccessful(managedCertificate);

            Assert.IsTrue(result, "Explicit primary request success should allow manual deployment reruns even without relying on fallback certificate state.");
        }

        [TestMethod, Description("Manual deployment rerun should remain blocked when there is no evidence of a successful certificate request")]
        public void TestDeploymentTaskRerunStaysBlockedWithoutSuccessfulCertificateRequest()
        {
            var managedCertificate = new ManagedCertificate
            {
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Error },
                LastRenewalStatus = RequestState.Error,
                DateRenewed = null,
                CertificateThumbprintHash = null,
                CertificatePath = null
            };

            var result = InvokeWasLastCertificateRequestSuccessful(managedCertificate);

            Assert.IsFalse(result, "Manual task reruns should remain blocked when the last certificate request did not complete successfully.");
        }

        [TestMethod, Description("Manual deployment rerun should remain blocked when the last primary request failed and the existing certificate is expired")]
        public void TestDeploymentTaskRerunStaysBlockedWhenFallbackCertificateExpired()
        {
            var managedCertificate = new ManagedCertificate
            {
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Error },
                LastRenewalStatus = RequestState.Error,
                DateStart = DateTimeOffset.UtcNow.AddDays(-90),
                DateRenewed = DateTimeOffset.UtcNow.AddDays(-90),
                DateExpiry = DateTimeOffset.UtcNow.AddDays(-1),
                CertificateThumbprintHash = "thumbprint"
            };

            var result = InvokeWasLastCertificateRequestSuccessful(managedCertificate);

            Assert.IsFalse(result, "Manual task reruns should not rely on fallback certificate state once the available certificate is expired.");
        }

        [TestMethod, Description("Applying certificate request result changes should clone primary request status instead of sharing mutable state")]
        public void TestCertificateRequestResultApplyChangesClonesPrimaryRequest()
        {
            var target = new CertificateRequestResult(new ManagedCertificate());
            var source = new CertificateRequestResult(new ManagedCertificate())
            {
                PrimaryRequest = new RequestStageStatus
                {
                    Status = RequestState.Success,
                    Message = "Primary request succeeded"
                }
            };

            target.ApplyChanges(source);
            source.PrimaryRequest.Status = RequestState.Error;
            source.PrimaryRequest.Message = "Mutated";

            Assert.IsNotNull(target.PrimaryRequest, "Primary request status should be copied during ApplyChanges.");
            Assert.AreEqual(RequestState.Success, target.PrimaryRequest.Status, "Primary request status should be cloned rather than aliased.");
            Assert.AreEqual("Primary request succeeded", target.PrimaryRequest.Message, "Primary request message should be cloned rather than aliased.");
        }

        [TestMethod, Description("Overall renewal status should not treat a current primary request failure as success just because an older certificate still exists")]
        public void TestOverallRenewalStatusUsesExplicitCurrentPrimaryRequestFailure()
        {
            var managedCertificate = new ManagedCertificate
            {
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Error },
                DateRenewed = DateTimeOffset.UtcNow.AddDays(-10),
                DateExpiry = DateTimeOffset.UtcNow.AddDays(20),
                CertificateThumbprintHash = "thumbprint"
            };

            var requestResult = new CertificateRequestResult(managedCertificate)
            {
                PrimaryRequest = new RequestStageStatus { Status = RequestState.Error, Message = "DNS credentials invalid." }
            };

            var result = InvokeIsPrimaryCertificateRequestSuccessful(managedCertificate, requestResult);

            Assert.IsFalse(result, "A current failed primary request should remain failed even if a previous certificate is still present.");
        }

        [TestMethod, Description("Overall renewal status should still fall back to existing certificate state when no explicit primary request status exists")]
        public void TestOverallRenewalStatusFallsBackWhenPrimaryRequestStatusMissing()
        {
            var managedCertificate = new ManagedCertificate
            {
                LastPrimaryRequest = null,
                DateRenewed = DateTimeOffset.UtcNow.AddDays(-10),
                DateExpiry = DateTimeOffset.UtcNow.AddDays(20),
                CertificateThumbprintHash = "thumbprint"
            };

            var requestResult = new CertificateRequestResult(managedCertificate)
            {
                PrimaryRequest = null
            };

            var result = InvokeIsPrimaryCertificateRequestSuccessful(managedCertificate, requestResult);

            Assert.IsTrue(result, "Fallback certificate state should still support older items when no explicit primary request status has been recorded.");
        }

        [TestMethod, Description("Overall renewal status should not fall back to expired certificate state when no explicit primary request status exists")]
        public void TestOverallRenewalStatusDoesNotFallbackToExpiredCertificate()
        {
            var managedCertificate = new ManagedCertificate
            {
                LastPrimaryRequest = null,
                DateRenewed = DateTimeOffset.UtcNow.AddDays(-90),
                DateExpiry = DateTimeOffset.UtcNow.AddDays(-1),
                CertificateThumbprintHash = "thumbprint"
            };

            var requestResult = new CertificateRequestResult(managedCertificate)
            {
                PrimaryRequest = null
            };

            var result = InvokeIsPrimaryCertificateRequestSuccessful(managedCertificate, requestResult);

            Assert.IsFalse(result, "Fallback certificate state should only imply success when the existing certificate is still usable.");
        }

        private static bool InvokeWasLastCertificateRequestSuccessful(ManagedCertificate managedCertificate)
        {
            var method = typeof(CertifyManager).GetMethod("WasLastCertificatePrimaryRequestSuccessful", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method, "Could not find deployment task success inference method.");

            return (bool)method.Invoke(null, [managedCertificate]);
        }

        private static bool InvokeIsPrimaryCertificateRequestSuccessful(ManagedCertificate managedCertificate, CertificateRequestResult requestResult)
        {
            var method = typeof(CertifyManager).GetMethod("IsPrimaryCertificateRequestSuccessful", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method, "Could not find overall renewal primary request success method.");

            return (bool)method.Invoke(null, [managedCertificate, requestResult]);
        }
    }
}
