using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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
    }
}
