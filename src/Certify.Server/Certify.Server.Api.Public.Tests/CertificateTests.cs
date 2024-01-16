using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.API.Public;
using Certify.Models;
using Certify.Models.API;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Org.BouncyCastle.Utilities;

namespace Certify.Service.Api.Tests
{
    [TestClass]
    public class CertificateTests : APITestBase
    {
        [TestMethod]
        public async Task GetCertificates_UnauthorizedTest()
        {
            await Assert.ThrowsExceptionAsync<ApiException>(async () => await _clientWithAnonymousAccess.GetManagedCertificatesAsync("", 0, 10));

        }

        [TestMethod]
        public async Task GetCertificates_AuthorizedTest()
        {
            // Act
            await PerformAuth();

            var response = await _clientWithAuthorizedAccess.GetManagedCertificatesAsync("", 0, 10);

            // Assert
            Assert.IsTrue(response.TotalResults > 0);

            Assert.IsNotNull(response.Results);

            Assert.IsNotNull(response.Results.First().Id);
        }

        [TestMethod]
        public async Task GetCertificateDownload_AuthorizedTest()
        {
            // Act
            await PerformAuth();

            var response = await _clientWithAuthorizedAccess.GetManagedCertificatesAsync("", 0, 10);

            Assert.IsTrue(response.TotalResults > 0, "Certificate query should be successful");

            var itemWithCert = response.Results.Last(c => c.HasCertificate && c.Identifiers.Any(d=>d.IdentifierType== CertIdentifierType.Dns));

            // get cert /certificate/{managedCertId}/download/{format?}

           var file =  await _clientWithAuthorizedAccess.DownloadAsync(itemWithCert.Id, "pfx","fullchain");

            // Assert
            using (var memoryStream = new MemoryStream())
            {
                file.Stream.CopyTo(memoryStream);
                var certResponseBytes = memoryStream.ToArray();

                try
                {
                    var cert = new X509Certificate2(certResponseBytes);

                    Assert.IsTrue(cert.HasPrivateKey, "Downloaded PFX has private key");

                    Assert.AreEqual(cert.Subject, "CN=" + itemWithCert.PrimaryIdentifier.Value, "Primary domain of cert should match primary domain of managed item");
                } catch (System.Security.Cryptography.CryptographicException)
                {
                    // pfx has a password set
                }
            }
        }
    }
}
