using System.Threading.Tasks;
using Certify.Models;
using Certify.Shared.Core.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class ConnectionCheckTests
    {
        [TestMethod, Description("Ensure that an http service is available")]
        public async Task TestPortConnection()
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
