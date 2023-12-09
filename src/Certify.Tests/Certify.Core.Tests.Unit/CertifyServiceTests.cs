using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class CertifyServiceTests
    {
        [TestMethod, Description("Validate that Service Program Main() does not start with bad args")]
        public async Task TestProgramMainFails()
        {
            var exitCode = Certify.Service.Program.Main(null);

            await Task.Delay(5000);

            Assert.AreEqual(exitCode, 1067, "Unexpected exit code");
        }
    }
}
