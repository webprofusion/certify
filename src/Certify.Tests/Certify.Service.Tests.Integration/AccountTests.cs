using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.Service.Tests.Integration
{
    [TestClass]
    public class AccountTests
    {
        private Client.CertifyServiceClient _client = null;

        [TestInitialize]
        public void Setup()
        {
            _client = new Certify.Client.CertifyServiceClient();
        }

        [TestMethod]
        public async Task TestAddContact()
        {
            var result = await _client.AddAccount(new Models.ContactRegistration {  
                EmailAddress="testing@webprofusion.com",
                CertificateAuthorityId=StandardCertAuthorities.LETS_ENCRYPT, 
                AgreedToTermsAndConditions=true,  
                IsStaging=true
            });

            Assert.IsNotNull(result);

            Assert.IsTrue(result.IsSuccess);
        }

        [TestMethod]
        public async Task TestGetAccounts()
        {
            var result = await _client.GetAccounts();

            Assert.IsNotNull(result);
        }

        [TestMethod]
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task TestUpdateAccount()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            throw new NotImplementedException("Implement Update Account");
            //var result = await _client.Update(new Models.ContactRegistration { EmailAddress = "certify@certifytheweb.com", AgreedToTermsAndConditions = true });

           
        }
    }
}
