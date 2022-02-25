using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Server.Api.Public.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Service.Api.Tests
{
    [TestClass]
    public class CertificateTests : APITestBase
    {
        [TestMethod]
        public async Task GetCertificates_UnauthorizedTest()
        {
            // Act
            var response = await _clientWithAnonymousAccess.GetAsync(_apiBaseUri + "/certificate");

            // Assert
            Assert.IsFalse(response.IsSuccessStatusCode);

            Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [TestMethod]
        public async Task GetCertificates_AuthorizedTest()
        {
            // Act
            await PerformAuth();

            var response = await _clientWithAuthorizedAccess.GetAsync(_apiBaseUri + "/certificate");
            var responseString = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(response.IsSuccessStatusCode);

            Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);

            var managedCerts = System.Text.Json.JsonSerializer.Deserialize<List<ManagedCertificateSummary>>(responseString, _defaultJsonSerializerOptions);

            Assert.IsNotNull(managedCerts);

            Assert.IsNotNull(managedCerts[0].Id);
        }

        [TestMethod]
        public async Task GetCertificateDownload_AuthorizedTest()
        {
            // Act
            await PerformAuth();

            var response = await _clientWithAuthorizedAccess.GetAsync(_apiBaseUri + "/certificate");

            Assert.IsTrue(response.IsSuccessStatusCode, "Certificate query should be successful");

            var responseString = await response.Content.ReadAsStringAsync();
            var managedCerts = System.Text.Json.JsonSerializer.Deserialize<List<ManagedCertificateSummary>>(responseString, _defaultJsonSerializerOptions);

            var itemWithCert = managedCerts.First(c => c.DateRenewed != null);

            // get cert /certificate/{managedCertId}/download/{format?}

            var certDownloadResponse = await _clientWithAuthorizedAccess.GetAsync(_apiBaseUri + "/certificate/" + itemWithCert.Id + "/download/");

            // Assert
            Assert.IsTrue(certDownloadResponse.IsSuccessStatusCode);

            var certResponseBytes = await certDownloadResponse.Content.ReadAsByteArrayAsync();

            var cert = new X509Certificate2(certResponseBytes);

            Assert.IsTrue(cert.HasPrivateKey, "Downloaded PFX has private key");

            Assert.AreEqual(cert.Subject, "CN=" + itemWithCert.PrimaryIdentifier.Value, "Primary domain of cert should match primary domain of managed item");
        }
    }
}
