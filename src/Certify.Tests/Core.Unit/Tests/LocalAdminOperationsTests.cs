using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Management;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Services.LocalAdmin;
using Certify.Tests.Core.Unit.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// The hub's local admin channel recovers a hub nobody can sign in to, acting as the system for an operating system
    /// administrator on the hub machine. These cover the operations behind it.
    /// </summary>
    [TestClass]
    public class LocalAdminOperationsTests
    {
        private const string Caller = "test-os-admin";
        private const string ExistingUserId = "user_01";
        private const string ExistingUsername = "operator";

        private string _testDir = string.Empty;
        private string _settingsPath = string.Empty;
        private AccessControl _access = null!;

        [TestInitialize]
        public async Task TestInitialize()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "certify-local-admin-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _settingsPath = Path.Combine(_testDir, "hubservice.json");
            File.WriteAllText(_settingsPath, "{}");

            _access = new AccessControl(new Loggy(NullLogger.Instance), new MemoryObjectStore());

            var standard = await AccessControlConfig.UpdateStandardAccessConfig(_access);
            Assert.IsEmpty(standard.Failures, "test setup should succeed");

            Assert.IsTrue(await _access.AddSecurityPrincipal(StandardSecurityPrincipals.System, new SecurityPrincipal
            {
                Id = ExistingUserId,
                Username = ExistingUsername,
                Password = "original-password",
                PrincipalType = SecurityPrincipalType.User
            }, bypassIntegrityCheck: true), "test setup should succeed");

            Assert.IsTrue(await _access.AddSecurityPrincipal(StandardSecurityPrincipals.System, new SecurityPrincipal
            {
                Id = AccessControlConfig.ManagedInstanceSecurityPrincipalId,
                Title = "Managed Instances Service Principal",
                PrincipalType = SecurityPrincipalType.Application
            }, bypassIntegrityCheck: true), "test setup should succeed");
        }

        [TestCleanup]
        public void TestCleanup()
        {
            try
            {
                Directory.Delete(_testDir, recursive: true);
            }
            catch (IOException)
            {
                // the configuration file watcher may still hold the directory briefly
            }
        }

        private LocalAdminOperations CreateOperations(IConfiguration? configuration = null)
        {
            var manager = new Mock<ICertifyManager>();
            manager.Setup(m => m.GetCurrentAccessControl()).ReturnsAsync(_access);

            return new LocalAdminOperations(
                manager.Object,
                configuration ?? new ConfigurationBuilder().Build(),
                _settingsPath,
                activityRecorder: null,
                NullLogger.Instance);
        }

        [TestMethod]
        public async Task ResetPasswordByUsernameGeneratesAWorkingPassword()
        {
            var result = await CreateOperations().ResetPassword(new LocalAdminPrincipalRequest { Principal = ExistingUsername.ToUpperInvariant() }, Caller);

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.AreEqual(ExistingUserId, result.PrincipalId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.GeneratedPassword), "A reset should return the generated password.");

            var stored = await _access.GetSecurityPrincipal(StandardSecurityPrincipals.System, ExistingUserId, includePassword: true);
            Assert.IsTrue(_access.IsPasswordValid(result.GeneratedPassword!, stored.Password), "The generated password should be the one in effect.");
            Assert.IsFalse(_access.IsPasswordValid("original-password", stored.Password), "The previous password should no longer be valid.");
        }

        [TestMethod]
        public async Task OnlyUsersCanBeResetOrGrantedAdministrator()
        {
            var operations = CreateOperations();
            var request = new LocalAdminPrincipalRequest { Principal = AccessControlConfig.ManagedInstanceSecurityPrincipalId };

            var reset = await operations.ResetPassword(request, Caller);
            Assert.IsFalse(reset.IsSuccess, "An application principal's password must not be reset here.");
            Assert.IsNull(reset.GeneratedPassword);

            var grant = await operations.GrantAdministrator(request, Caller);
            Assert.IsFalse(grant.IsSuccess, "An application principal must not be made an administrator here.");
            Assert.IsFalse(await _access.IsPrincipalInRole(AccessControlConfig.ManagedInstanceSecurityPrincipalId, StandardRoles.Administrator.Id));
        }

        [TestMethod]
        public async Task GrantAdministratorIsIdempotent()
        {
            var operations = CreateOperations();
            var request = new LocalAdminPrincipalRequest { Principal = ExistingUserId };

            Assert.IsTrue((await operations.GrantAdministrator(request, Caller)).IsSuccess);
            Assert.IsTrue((await operations.GrantAdministrator(request, Caller)).IsSuccess, "Granting a role already held should succeed without change.");

            Assert.IsTrue(await _access.IsPrincipalInRole(ExistingUserId, StandardRoles.Administrator.Id));

            var assigned = await _access.GetAssignedRoles(StandardSecurityPrincipals.System, ExistingUserId);
            Assert.AreEqual(1, assigned.Count(a => a.RoleId == StandardRoles.Administrator.Id), "A repeated grant must not add a second assignment.");

            var listed = (await operations.GetPrincipals()).Single(p => p.Id == ExistingUserId);
            Assert.IsTrue(listed.IsAdministrator);
        }

        [TestMethod]
        public async Task CreateAdministratorRejectsAnExistingUsername()
        {
            var result = await CreateOperations().CreateAdministrator(new LocalAdminCreateAdministratorRequest { Username = ExistingUsername.ToUpperInvariant() }, Caller);

            Assert.IsFalse(result.IsSuccess, "Two users sharing a username could not both sign in.");
            Assert.IsNull(result.GeneratedPassword);
            Assert.HasCount(2, await _access.GetSecurityPrincipals(StandardSecurityPrincipals.System));
        }

        [TestMethod]
        public async Task CreateAdministratorAddsAnAdministratorWithAGeneratedPassword()
        {
            var result = await CreateOperations().CreateAdministrator(new LocalAdminCreateAdministratorRequest { Username = "recovery" }, Caller);

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.IsNotNull(result.PrincipalId);
            Assert.IsTrue(await _access.IsPrincipalInRole(result.PrincipalId, StandardRoles.Administrator.Id));

            var check = await _access.CheckSecurityPrincipalPassword(StandardSecurityPrincipals.System, new SecurityPrincipalPasswordCheck { Username = "recovery", Password = result.GeneratedPassword! });
            Assert.IsTrue(check.IsSuccess, "The new administrator should be able to sign in with the generated password.");
        }

        [TestMethod]
        public async Task EnablePasswordLoginKeepsCommentsWhenTheSettingIsInTheFile()
        {
            File.WriteAllText(_settingsPath, """
                {
                  // kept comment
                  "AuthSettings": { "enablePasswordLogin": false },
                  "Other": "value"
                }
                """);

            var configuration = new ConfigurationBuilder().AddJsonFile(_settingsPath, optional: false, reloadOnChange: true).Build();
            Assert.IsFalse(Server.Hub.Api.Services.AuthSettings.IsPasswordLoginEnabled(configuration), "test setup should disable password login");

            var result = await CreateOperations(configuration).EnablePasswordLogin(Caller);

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.IsTrue(Server.Hub.Api.Services.AuthSettings.IsPasswordLoginEnabled(configuration), "The hub should apply the change without a restart.");

            var content = File.ReadAllText(_settingsPath);
            StringAssert.Contains(content, "// kept comment");
            StringAssert.Contains(content, "\"Other\": \"value\"");
        }

        [TestMethod]
        public async Task EnablePasswordLoginOverridesAnotherConfigurationSource()
        {
            File.WriteAllText(_settingsPath, """{ "Other": "value" }""");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AuthSettings:enablePasswordLogin"] = "false" })
                .AddJsonFile(_settingsPath, optional: false, reloadOnChange: true)
                .Build();

            var result = await CreateOperations(configuration).EnablePasswordLogin(Caller);

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.IsTrue(Server.Hub.Api.Services.AuthSettings.IsPasswordLoginEnabled(configuration));
            Assert.AreEqual("value", configuration["Other"], "Other settings in the file must be kept.");
            Assert.IsTrue(File.Exists(_settingsPath + ".bak"), "Rewriting the file should keep a copy of the previous one.");
        }
    }
}
