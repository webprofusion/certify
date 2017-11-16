using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Certify.Service.Tests.Integration
{
    [TestClass]
    public class ContactTests
    {
        private Client.CertifyServiceClient _client = null;

        [TestInitialize]
        public void Setup()
        {
            _client = new Certify.Client.CertifyServiceClient();
        }

        [TestMethod]
        public async Task TestGetPrimaryContact()
        {
            string result = await _client.GetPrimaryContact();

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task TestUpdatePrimaryContact()
        {
            var result = await _client.SetPrimaryContact(new Models.ContactRegistration { EmailAddress = "certify@certifytheweb.com", AgreedToTermsAndConditions = true });

            Assert.IsTrue(result);
        }
    }
}