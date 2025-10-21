using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Shared.Core.Utils.PKI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class MiscTests
    {

        public MiscTests()
        {

        }

        [TestMethod, Description("Test null/blank coalesce of string")]
        public void TestNullOrBlankCoalesce()
        {
            string testValue = null;

            var result = testValue.WithDefault("ok");
            Assert.AreEqual(result, "ok");

            testValue = "test";
            result = testValue.WithDefault("ok");
            Assert.AreEqual(result, "test");

            var ca = new Models.CertificateAuthority();
            ca.Description = null;
            result = ca.Description.WithDefault("default");
            Assert.AreEqual(result, "default");

            ca = null;
            result = ca?.Description.WithDefault("default");
            Assert.AreEqual(result, null);
        }

        [TestMethod, Description("Test ntp check")]
        public async Task TestNtp()
        {
            var check = await Certify.Management.Util.CheckTimeServer();

            var timeDiff = check - DateTimeOffset.UtcNow;

            if (Math.Abs(timeDiff.Value.TotalSeconds) > 50)
            {
                Assert.Fail("NTP Time Difference Failed");
            }
        }
#if NET7_0_OR_GREATER
        [TestMethod, Description("Test ARI CertID encoding example")]
        public void TestARICertIDEncoding()
        {
            // https://letsencrypt.org/2024/04/25/guide-to-integrating-ari-into-existing-acme-clients
            var certAKIbytes = Convert.FromHexString("69:88:5B:6B:87:46:40:41:E1:B3:7B:84:7B:A0:AE:2C:DE:01:C8:D4".Replace(":", ""));
            var certSerialBytes = Convert.FromHexString("00:87:65:43:21".Replace(":", ""));

            var certId = Certify.Management.Util.ToUrlSafeBase64String(certAKIbytes)
                + "."
                + Certify.Management.Util.ToUrlSafeBase64String(certSerialBytes);

            Assert.AreEqual("aYhba4dGQEHhs3uEe6CuLN4ByNQ.AIdlQyE", certId);
        }
#endif

        [TestMethod, Description("Test loading X509 does not leave RSA keys behind on disk")]
        public void TestX509Load()
        {

            //count number of RSA key files under C:\ProgramData\Microsoft\Crypto\RSA\MachineKeys
            var rsaCount = System.IO.Directory.GetFiles(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Crypto", "RSA", "MachineKeys")).Length;

#if NET9_0_OR_GREATER
            var x509Cert2 = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(item.CertificatePath, await GetPfxPassword(item));
#else
            var x509Cert2 = new System.Security.Cryptography.X509Certificates.X509Certificate2("Assets/dummycert_rsa.pfx","", X509KeyStorageFlags.MachineKeySet);
#endif
            var ariCertId =  Certify.Shared.Core.Utils.PKI.CertUtils.GetARICertIdBase64(x509Cert2);
            Assert.AreEqual("9kMDkS9mJa-FJd3kZNFpfsuiHNk.LISueX2pFKa-v-iddDPp2xJA", ariCertId);
            //cleanup cert so temp RSA keys get removed on disk
            x509Cert2?.Dispose();
            x509Cert2 = null;
 
            var updatedRsaKeyCount = System.IO.Directory.GetFiles(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Crypto", "RSA", "MachineKeys")).Length;

            Assert.AreEqual(rsaCount, updatedRsaKeyCount, "RSA Key file count should be unchanged after loading X509Certificate2");

            CertificateManager.LoadCertificate("Assets/dummycert_rsa.pfx");

            updatedRsaKeyCount = System.IO.Directory.GetFiles(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Crypto", "RSA", "MachineKeys")).Length;

            Assert.AreEqual(rsaCount, updatedRsaKeyCount, "RSA Key file count should be unchanged after loading X509Certificate2");

        }
    }
}
