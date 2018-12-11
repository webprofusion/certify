using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Management.Servers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests
{
    [TestClass]
    public class CertificateStoreCleanup : IntegrationTestBase
    {
        [TestMethod]
        public async Task TestCertCleanupAtExpiry()
        {
            // create and store a number of test certificates
            var cert1 = CreateAndStoreTestCertificate("cert-test1.example.com", new DateTime(1935, 01, 01), new DateTime(1935, 03, 01));
            var cert2 = CreateAndStoreTestCertificate("cert-test2.example.com", new DateTime(1934, 01, 01), new DateTime(1934, 03, 01));
            var cert3 = CreateAndStoreTestCertificate("cert-test3.example.com", new DateTime(1936, 01, 01), new DateTime(1936, 07, 01));
            var cert4 = CreateAndStoreTestCertificate("cert-test4.example.com", new DateTime(1936, 01, 01), new DateTime(1936, 05, 15));

            // create test site for bindings, add bindings
            var iisManager = new ServerProviderIIS();

            var testSiteDomain = "cert-test.example.com";
            if (await iisManager.SiteExists(testSiteDomain))
            {
                await iisManager.DeleteSite(testSiteDomain);
            }
            var site = await iisManager.CreateSite(testSiteDomain, testSiteDomain, PrimaryIISRoot, "DefaultAppPool");

            await iisManager.AddOrUpdateSiteBinding(
                new Models.BindingInfo
                {
                    SiteId = site.Id.ToString(),
                    Host = testSiteDomain,
                    CertificateHash = cert2.Thumbprint,
                    CertificateStore = "MY",
                    IsSNIEnabled = false,
                    Port = 443,
                    Protocol = "https"
                }, addNew: true
                );

            // run cleanup process, removes certs which have expired for over a month and have
            // [Certify] in the friendly name
            CertificateManager.PerformCertificateStoreCleanup(Models.CertificateCleanupMode.AfterExpiry, new DateTime(1936, 06, 01), null, null);

            // check the correct certificates have been removed
            try
            {
                // check cert test 1 removed (expired)
                Assert.IsFalse(CertificateManager.IsCertificateInStore(cert1), "Cert 1 Should Be Removed");

                // check cert test 2 removed (expired)
                Assert.IsFalse(CertificateManager.IsCertificateInStore(cert2), "Cert 2 Should Be Removed");

                // check cert test 3 exists (not expired)
                Assert.IsTrue(CertificateManager.IsCertificateInStore(cert3), "Cert 3 Should Not Be Removed");

                // check cert test 4 removed (expired, but for less than 1 month)
                Assert.IsFalse(CertificateManager.IsCertificateInStore(cert4), "Cert 4 Should  Be Removed");
            }
            finally
            {
                // clean up after test
                await iisManager.DeleteSite(site.Name);

                CertificateManager.RemoveCertificate(cert1);
                CertificateManager.RemoveCertificate(cert2);
                CertificateManager.RemoveCertificate(cert3);
                CertificateManager.RemoveCertificate(cert4);
            }
        }

        [TestMethod]
        public async Task TestCertCleanupByThumbprint()
        {
            // create and store a number of test certificates
            var cert1 = CreateAndStoreTestCertificate("cert-test1.example.com", new DateTime(1935, 01, 01), new DateTime(1935, 03, 01));
            var cert2 = CreateAndStoreTestCertificate("cert-test2.example.com", new DateTime(1934, 01, 01), new DateTime(1934, 03, 01));
            var cert3 = CreateAndStoreTestCertificate("cert-test2.example.com", new DateTime(1936, 01, 01), new DateTime(1938, 03, 01));

            // create test site for bindings, add bindings
            var iisManager = new ServerProviderIIS();

            var testSiteDomain = "cert-test.example.com";
            if (await iisManager.SiteExists(testSiteDomain))
            {
                await iisManager.DeleteSite(testSiteDomain);
            }
            var site = await iisManager.CreateSite(testSiteDomain, testSiteDomain, PrimaryIISRoot, "DefaultAppPool");

            await iisManager.AddOrUpdateSiteBinding(
                new Models.BindingInfo
                {
                    SiteId = site.Id.ToString(),
                    Host = testSiteDomain,
                    CertificateHash = cert2.Thumbprint,
                    CertificateStore = "MY",
                    IsSNIEnabled = false,
                    Port = 443,
                    Protocol = "https"
                }, addNew: true
                );

            // run cleanup process, removes certs by name, excluding the given thumbprints
            CertificateManager.PerformCertificateStoreCleanup(
                Models.CertificateCleanupMode.AfterRenewal, 
                new DateTime(1936, 06, 01), 
                matchingName:"cert-test2.example.com", 
                excludedThumbprints: new List<string> { cert3.Thumbprint });

            // check the correct certificates have been removed
            try
            {
                // check cert test 1 not removed (does not match)
                Assert.IsTrue(CertificateManager.IsCertificateInStore(cert1), "Cert 1 Should Not Be Removed");

                // check cert test 2 removed (does match)
                Assert.IsFalse(CertificateManager.IsCertificateInStore(cert2), "Cert 2 Should Be Removed");

                // check cert test 3 exists (matches but is excluded by thumbprint)
                Assert.IsTrue(CertificateManager.IsCertificateInStore(cert3), "Cert 3 Should Not Be Removed");

            }
            finally
            {
                // clean up after test
                await iisManager.DeleteSite(site.Name);

                CertificateManager.RemoveCertificate(cert1);
                CertificateManager.RemoveCertificate(cert2);
                CertificateManager.RemoveCertificate(cert3);
            }
        }


        [TestMethod]
        public async Task TestCertCleanupFull()
        {
            // create and store a number of test certificates
            var cert1 = CreateAndStoreTestCertificate("cert-test1.example.com", new DateTime(1935, 01, 01), new DateTime(1935, 03, 01));
            var cert2 = CreateAndStoreTestCertificate("cert-test2.example.com", new DateTime(1934, 01, 01), new DateTime(1934, 03, 01));
            var cert3 = CreateAndStoreTestCertificate("cert-test2.example.com", new DateTime(1936, 01, 01), new DateTime(1938, 03, 01));

            // create test site for bindings, add bindings
            var iisManager = new ServerProviderIIS();

            var testSiteDomain = "cert-test.example.com";
            if (await iisManager.SiteExists(testSiteDomain))
            {
                await iisManager.DeleteSite(testSiteDomain);
            }
            var site = await iisManager.CreateSite(testSiteDomain, testSiteDomain, PrimaryIISRoot, "DefaultAppPool");

            await iisManager.AddOrUpdateSiteBinding(
                new Models.BindingInfo
                {
                    SiteId = site.Id.ToString(),
                    Host = testSiteDomain,
                    CertificateHash = cert2.Thumbprint,
                    CertificateStore = "MY",
                    IsSNIEnabled = false,
                    Port = 443,
                    Protocol = "https"
                }, addNew: true
                );

            // run cleanup process, removes certs by name, excluding the given thumbprints

            CertificateManager.PerformCertificateStoreCleanup(
                Models.CertificateCleanupMode.FullCleanup,
                new DateTime(1936, 06, 01),
                matchingName: "cert-test2.example.com",
                excludedThumbprints: new List<string> { cert3.Thumbprint });

            // check the correct certificates have been removed
            try
            {
                // check cert test 1 not removed (does not match)
                Assert.IsTrue(CertificateManager.IsCertificateInStore(cert1), "Cert 1 Should Not Be Removed");

                // check cert test 2 removed (does match)
                Assert.IsFalse(CertificateManager.IsCertificateInStore(cert2), "Cert 2 Should Be Removed");

                // check cert test 3 exists (matches but is excluded by thumbprint)
                Assert.IsTrue(CertificateManager.IsCertificateInStore(cert3), "Cert 3 Should Not Be Removed");

            }
            finally
            {
                // clean up after test
                await iisManager.DeleteSite(site.Name);

                CertificateManager.RemoveCertificate(cert1);
                CertificateManager.RemoveCertificate(cert2);
                CertificateManager.RemoveCertificate(cert3);
            }
        }

        public X509Certificate2 CreateAndStoreTestCertificate(string domain, DateTime dateFrom, DateTime dateTo)
        {
            var cert = CertificateManager.GenerateSelfSignedCertificate(domain, dateFrom, dateTo);
            CertificateManager.StoreCertificate(cert);
            return cert;
        }
    }
}
