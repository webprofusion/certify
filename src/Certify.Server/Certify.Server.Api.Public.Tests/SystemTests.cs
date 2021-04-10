using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Service.Api.Tests
{
    [TestClass]
    public class SystemTests : APITestBase
    {
        [TestMethod]
        public async Task GetSystemVersionTest()
        {

            // Act
            var response = await _clientWithAnonymousAccess.GetAsync("/api/v1/system/version");
            response.EnsureSuccessStatusCode();
            var responseString = await response.Content.ReadAsStringAsync();

            // Assert
            var expectedVersion = typeof(Certify.Models.AppVersion).GetTypeInfo().Assembly.GetName().Version.ToString();
            Assert.AreEqual(expectedVersion, responseString);

        }
    }
}
