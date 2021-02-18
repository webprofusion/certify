using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Service.Tests.Integration
{
    [TestClass]
    public class PreferenceTests : ServiceTestBase
    {
        [TestMethod]
        public void TestGetPreferences()
        {
            var result = _client.GetPreferences().Result;

            Assert.IsNotNull(result, "Prefs available");
        }

        [TestMethod]
        public async Task TestSetPreferences()
        {
            var prefs = await _client.GetPreferences();

            prefs.MaxRenewalRequests = 69;

            var result = await _client.SetPreferences(prefs);

            Assert.IsTrue(result, "Prefs updates");

            prefs = await _client.GetPreferences();
            Assert.IsTrue(prefs.MaxRenewalRequests == 69, "Pref value updated and confirmed");

            prefs.MaxRenewalRequests = 14;
            await _client.SetPreferences(prefs);
        }
    }
}
