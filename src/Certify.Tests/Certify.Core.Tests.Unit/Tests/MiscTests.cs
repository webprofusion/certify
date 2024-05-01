using System;
using System.Threading.Tasks;
using Certify.Models.API;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class MiscTests
    {
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

        [TestMethod, Description("Test log parser using array of strings")]
        public void TestLogParser()
        {
            var testLog = new string[]
            {
                "2023-06-14 13:00:30.480 +08:00 [WRN] ARI Update Renewal Info Failed[MGAwDQYJYIZIAWUDBAIBBQAEIDfbgj - 5Rkkn0NG7u0eFv_M1omHdEwY_mIQn6QxbuJ68BCA9ROYZMeqCkxyMzaMePORi17Gc9xSbp8XkoE1Ub0IPrwILBm8t23CUKQnarrc] Fail to load resource from 'https://acme-staging-v02.api.letsencrypt.org/draft-ietf-acme-ari-01/renewalInfo/'." ,
                "urn:ietf:params:acme: error: malformed: Certificate not found" ,
                "2023-06-14 13:01:11.139 +08:00 [INF] Performing Certificate Request: SporkDemo[zerossl][2390d803 - e036 - 4bf5 - 8fa5 - 590497392c35: 7]"
            };

            var items = LogParser.Parse(testLog);

            Assert.AreEqual(2, items.Length);

            Assert.AreEqual("WRN", items[0].LogLevel);
            Assert.AreEqual("INF", items[1].LogLevel);

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
            var certAKIbytes = Convert.FromHexString("69:88:5B:6B:87:46:40:41:E1:B3:7B:84:7B:A0:AE:2C:DE:01:C8:D4".Replace(":",""));
            var certSerialBytes= Convert.FromHexString("00:87:65:43:21".Replace(":",""));

            var certId = Certify.Management.Util.ToUrlSafeBase64String(certAKIbytes) 
                + "." 
                + Certify.Management.Util.ToUrlSafeBase64String(certSerialBytes);

           Assert.AreEqual("aYhba4dGQEHhs3uEe6CuLN4ByNQ.AIdlQyE", certId);
        }
#endif
    }
}
