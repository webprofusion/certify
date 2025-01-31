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
            await PerformAuth();

            var responseVersion = await _clientWithAuthorizedAccess.GetSystemVersionAsync();

            // Assert
            var expectedVersion = typeof(Certify.Models.AppVersion).GetTypeInfo().Assembly.GetName().Version;
            Assert.AreEqual(expectedVersion.ToString(), responseVersion);

        }
    }
}
