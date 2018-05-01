using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class BindingMatchTests
    {
        public List<BindingInfo> _allSites { get; set; }

        [TestInitialize]
        public void Setup()
        {
            _allSites = new List<BindingInfo>
            {
                new BindingInfo{ Name="TestDotCom", Host="", IP="192.168.1.1", HasCertificate=true, Protocol="https", Port=443, Id="1"},
                 new BindingInfo{ Name="TestDotCom", Host="", IP="", HasCertificate=true, Protocol="https", Port=443, Id="2"}
            };
        }

        [TestMethod, Description("Ensure binding add/update decisions are correct based on deployment criteria")]
        public async Task TestDNSTests()
        {
        }
    }
}
