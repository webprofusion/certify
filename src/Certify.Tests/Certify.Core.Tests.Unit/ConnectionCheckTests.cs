using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class ConenctionCheckTests
    {
        [TestMethod, Description("Ensure that an http service is available")]
        public async Task TestDNSTests()
        {
            var net = new NetworkUtils(enableProxyValidationAPI: true);

            var logImp = new LoggerConfiguration()
                .WriteTo.Debug()
                .CreateLogger();

            var log = new Loggy(logImp);

            var result = await net.CheckServiceConnection("webprofusion.com", 80);

            Assert.IsTrue(result.IsSuccess, "hostname should connect ok on port 80");
        }
    }
}
