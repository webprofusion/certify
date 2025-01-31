using System.Threading.Tasks;
using Certify.Models.API;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Service.Api.Tests
{
    [TestClass]
    public class AccessTests : APITestBase
    {

        [TestMethod]
        public async Task AddDeleteSecurityPrinciple()
        {
            // Act
            await PerformAuth();

            var sp = new Models.Config.AccessControl.SecurityPrinciple
            {
                Id = "apitest_user",
                Password = "test"
            };

            var add = await _clientWithAuthorizedAccess.AddSecurityPrincipleAsync(sp);

            Assert.IsTrue(add.IsSuccess);

            var delete = await _clientWithAuthorizedAccess.DeleteSecurityPrincipleAsync(sp.Id);

            Assert.IsTrue(delete.IsSuccess);
        }

        [TestMethod]
        public async Task AddUpdateDeleteSecurityPrinciple()
        {
            // Act
            await PerformAuth();

            var sp = new Models.Config.AccessControl.SecurityPrinciple
            {
                Id = "apitest_user",
                Password = "test"
            };

            var add = await _clientWithAuthorizedAccess.AddSecurityPrincipleAsync(sp);

            Assert.IsTrue(add.IsSuccess);

            var validation = await _clientWithAuthorizedAccess.ValidateSecurityPrinciplePasswordAsync(new SecurityPrinciplePasswordCheck(sp.Id, sp.Password));

            Assert.IsTrue(validation.IsSuccess);

            var update = await _clientWithAuthorizedAccess.UpdateSecurityPrinciplePasswordAsync(new SecurityPrinciplePasswordUpdate(sp.Id, sp.Password, "newpwd"));

            Assert.IsTrue(update.IsSuccess);

            var updateValidation = await _clientWithAuthorizedAccess.ValidateSecurityPrinciplePasswordAsync(new SecurityPrinciplePasswordCheck(sp.Id, "newpwd"));

            Assert.IsTrue(updateValidation.IsSuccess);

            var delete = await _clientWithAuthorizedAccess.DeleteSecurityPrincipleAsync(sp.Id);

            Assert.IsTrue(delete.IsSuccess);
        }
    }
}
