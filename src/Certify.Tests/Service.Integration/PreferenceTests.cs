using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Service.Integration;

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
        Assert.AreEqual(69, prefs.MaxRenewalRequests, "Pref value updated and confirmed");

        prefs.MaxRenewalRequests = 14;
        await _client.SetPreferences(prefs);
    }
}
