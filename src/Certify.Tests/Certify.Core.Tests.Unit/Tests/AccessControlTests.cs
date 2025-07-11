using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Certify.Core.Tests.Unit
{
    public class MemoryObjectStore : IConfigurationStore
    {
        private ConcurrentDictionary<string, ConfigurationStoreItem> _store = new ConcurrentDictionary<string, ConfigurationStoreItem>();

        public Task Add<T>(string itemType, ConfigurationStoreItem item)
        {
            item.ItemType = itemType;

            // clone the item to avoid reference issue mutating the same object, as we are using an in-memory store
            var clonedItem = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(item)) as ConfigurationStoreItem;
            return Task.FromResult(_store.TryAdd(clonedItem.Id, clonedItem));
        }

        public Task<bool> Delete<T>(string itemType, string id)
        {
            return Task.FromResult((_store.TryRemove(id, out _)));
        }

        public Task<List<T>> GetItems<T>(string itemType)
        {
            var items = _store.Values
                    .Where((s => s.ItemType == itemType))
                    .Select(s => (T)Convert.ChangeType(s, typeof(T)));

            return Task.FromResult((items.ToList()));
        }

        public Task<T> Get<T>(string itemType, string id)
        {
            _store.TryGetValue(id, out var value);
            return Task.FromResult((T)Convert.ChangeType(value, typeof(T)));
        }

        public Task Add<T>(string itemType, T item)
        {
            var o = item as ConfigurationStoreItem;
            o.ItemType = itemType;

            var clonedItem = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(o)) as ConfigurationStoreItem;
            return Task.FromResult(_store.TryAdd(clonedItem.Id, clonedItem));
        }

        public Task Update<T>(string itemType, T item)
        {
            var o = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(item)) as ConfigurationStoreItem;

            _store.TryGetValue(o.Id, out var value);
            var c = Task.FromResult((T)Convert.ChangeType(value, typeof(T))).Result as ConfigurationStoreItem;
            var r = Task.FromResult(_store.TryUpdate(o.Id, o, c));
            if (r.Result == false)
            {
                throw new Exception("Could not store item type");
            }

            return r;
        }
    }

    public class TestAssignedRoles
    {
        public static AssignedRole TestAdmin { get; } = new AssignedRole
        {
            // test administrator
            RoleId = StandardRoles.Administrator.Id,
            SecurityPrincipalId = TestSecurityPrincipals.TestAdmin.Id
        };
        public static AssignedRole Admin { get; } = new AssignedRole
        {
            // administrator
            RoleId = StandardRoles.Administrator.Id,
            SecurityPrincipalId = TestSecurityPrincipals.Admin.Id
        };
        public static AssignedRole DevopsUserDomainConsumer { get; } = new AssignedRole
        {
            // devops user in consumer role for a specific domain
            RoleId = StandardRoles.CertificateConsumer.Id,
            SecurityPrincipalId = TestSecurityPrincipals.DevopsAppDomainConsumer.Id,
            IncludedResources = new List<Resource>{
                new Resource{ ResourceType=ResourceTypes.Domain, Identifier="www.example.com" },
            }
        };
        public static AssignedRole DevopsUserWildcardDomainConsumer { get; } = new AssignedRole
        {
            // devops user in consumer role for a wildcard domain
            RoleId = StandardRoles.CertificateConsumer.Id,
            SecurityPrincipalId = TestSecurityPrincipals.DevopsUser.Id,
            IncludedResources = new List<Resource>{
                new Resource{ ResourceType=ResourceTypes.Domain, Identifier="*.microsoft.com" },
            }
        };
    }

    public class TestSecurityPrincipals
    {
        public static SecurityPrincipal TestAdmin => new SecurityPrincipal
        {
            Id = "[test]",
            Username = "test administrator",
            Description = "Example test administrator used as context user during test",
            Email = "test_admin@test.com",
            Password = "ABCDEFG",
            PrincipalType = SecurityPrincipalType.User
        };
        public static SecurityPrincipal Admin => new SecurityPrincipal
        {
            Id = "admin_01",
            Username = "admin",
            Description = "Administrator account",
            Email = "info@test.com",
            Password = "ABCDEFG",
            PrincipalType = SecurityPrincipalType.User,
        };
        public static SecurityPrincipal DomainOwner => new SecurityPrincipal
        {
            Id = "domain_owner_01",
            Username = "demo_owner",
            Description = "Example domain owner",
            Email = "domains@test.com",
            Password = "ABCDEFG",
            PrincipalType = SecurityPrincipalType.User,
        };
        public static SecurityPrincipal DevopsUser => new SecurityPrincipal
        {
            Id = "devops_user_01",
            Username = "devops_01",
            Description = "Example devops user",
            Email = "devops01@test.com",
            Password = "ABCDEFG",
            PrincipalType = SecurityPrincipalType.User,
        };
        public static SecurityPrincipal DevopsAppDomainConsumer => new SecurityPrincipal
        {
            Id = "devops_app_01",
            Username = "devapp_01",
            Description = "Example devops app domain consumer",
            Email = "dev_app01@test.com",
            Password = "ABCDEFG",
            PrincipalType = SecurityPrincipalType.User,
        };
    }

    [TestClass]
    public class AccessControlTests
    {
        private Loggy loggy;
        private AccessControl access;
        private const string contextUserId = "[test]";

        [TestInitialize]
        public async Task TestInitialize()
        {
            this.loggy = new Loggy(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<AccessControlTests>());

            this.access = new AccessControl(loggy, new MemoryObjectStore());
        }

        [TestMethod]
        public async Task TestAddGetSecurityPrincipals()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };
            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            // Get stored security principals
            var storedSecurityPrincipals = await access.GetSecurityPrincipals(contextUserId);

            // Validate SecurityPrincipal list returned by AccessControl.GetSecurityPrincipals()
            Assert.IsNotNull(storedSecurityPrincipals, "Expected list returned by AccessControl.GetSecurityPrincipals() to not be null");
            Assert.AreEqual(2, storedSecurityPrincipals.Count, "Expected list returned by AccessControl.GetSecurityPrincipals() to have 2 SecurityPrincipal objects");
            foreach (var passedPrincipal in adminSecurityPrincipals)
            {
                Assert.IsNotNull(storedSecurityPrincipals.Find(x => x.Id == passedPrincipal.Id), $"Expected a SecurityPrincipal returned by GetSecurityPrincipals() to match Id '{passedPrincipal.Id}' of SecurityPrincipal passed into AddSecurityPrincipal()");
            }
        }

        [TestMethod]
        public async Task TestGetSecurityPrincipalsNoRoles()
        {
            // Add test security principals
            var securityPrincipalAdded = await access.AddSecurityPrincipal(contextUserId, TestSecurityPrincipals.TestAdmin);

            // Get stored security principals
            Assert.IsFalse(securityPrincipalAdded, $"Expected AddSecurityPrincipal() to be unsuccessful without roles defined for {contextUserId}");
        }

        [TestMethod]
        public async Task TestAddGetSecurityPrincipal()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };
            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            foreach (var securityPrincipal in adminSecurityPrincipals)
            {
                // Get stored security principal
                var storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, securityPrincipal.Id);

                // Validate SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal()
                Assert.IsNotNull(storedSecurityPrincipal, "Expected object returned by AccessControl.GetSecurityPrincipal() to not be null");
                Assert.AreEqual(storedSecurityPrincipal.Id, securityPrincipal.Id, $"Expected SecurityPrincipal returned by GetSecurityPrincipal() to match Id '{securityPrincipal.Id}' of SecurityPrincipal passed into AddSecurityPrincipal()");
            }
        }

        [TestMethod]
        public async Task TestAddGetAssignedRoles()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };
            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            // Setup security principal actions
            var actions = Policies.GetStandardResourceActions().FindAll(a => a.ResourceType == ResourceTypes.System);
            actions.ForEach(async a => await access.AddResourceAction(contextUserId, a));

            // Setup policy with actions and add policy to store
            var policy = Policies.GetStandardPolicies().Find(p => p.Id == StandardPolicies.AccessAdmin);
            var addPolicy = await access.AddResourcePolicy(contextUserId, policy, bypassIntegrityCheck: true);

            Assert.IsTrue(addPolicy, "Expected to add role");

            // Setup and add roles and policy assignments to store
            var role = Policies.GetStandardRoles().Find(r => r.Id == StandardRoles.Administrator.Id);
            var addedRole = await access.AddRole(contextUserId, role, bypassIntegrityCheck: true);

            Assert.IsTrue(addedRole, "Expected to add role");

            // Assign security principals to roles and add roles and policy assignments to store
            var assignedRoles = new List<AssignedRole> { TestAssignedRoles.Admin, TestAssignedRoles.TestAdmin };
            assignedRoles.ForEach(async r => await access.AddAssignedRole(contextUserId, r, bypassIntegrityCheck: true));

            // Validate AssignedRole list returned by AccessControl.GetAssignedRoles()
            foreach (var assignedRole in assignedRoles)
            {
                var adminAssignedRoles = await access.GetAssignedRoles(contextUserId, assignedRole.SecurityPrincipalId);
                Assert.IsNotNull(adminAssignedRoles, "Expected list returned by AccessControl.GetAssignedRoles() to not be null");
                Assert.AreEqual(1, adminAssignedRoles.Count, "Expected list returned by AccessControl.GetAssignedRoles() to have 1 AssignedRole object");
                Assert.AreEqual(assignedRole.SecurityPrincipalId, adminAssignedRoles[0].SecurityPrincipalId, "Expected AssignedRole returned by GetAssignedRoles() to match SecurityPrincipalId of AssignedRole passed into AddAssignedRole()");
            }
        }

        [TestMethod]
        public async Task TestGetAssignedRolesNoRoles()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };
            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            // assigned admin role to TestAdmin (also the contextUserId) so they can check roles for the other admin user
            await access.AddAssignedRole(TestSecurityPrincipals.TestAdmin.Id, TestAssignedRoles.TestAdmin, bypassIntegrityCheck: true);

            // Validate AssignedRole list returned by AccessControl.GetAssignedRoles()
            var adminAssignedRoles = await access.GetAssignedRoles(contextUserId, adminSecurityPrincipals[0].Id);
            Assert.IsNotNull(adminAssignedRoles, "Expected list returned by AccessControl.GetAssignedRoles() to not be null");
            Assert.AreEqual(0, adminAssignedRoles.Count, "Expected list returned by AccessControl.GetAssignedRoles() to have no AssignedRole objects");
        }

        [TestMethod]
        public async Task TestAddResourcePolicyNoRoles()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };
            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            // Setup security principal actions
            var actions = Policies.GetStandardResourceActions().FindAll(a => a.ResourceType == ResourceTypes.System);
            actions.ForEach(async a => await access.AddResourceAction(contextUserId, a));

            // Setup policy with actions and add policy to store
            var policy = Policies.GetStandardPolicies().Find(p => p.Id == StandardPolicies.AccessAdmin);
            var addedResourcePolicy = await access.AddResourcePolicy(contextUserId, policy);

            // Validate that AddResourcePolicy() failed when no roles are defined
            Assert.IsFalse(addedResourcePolicy, $"Unable to add a resource policy using {contextUserId} when roles are undefined");
        }

        [TestMethod]
        public async Task TestUpdateSecurityPrincipal()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };

            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            // Setup security principal actions
            var actions = Policies.GetStandardResourceActions().FindAll(a => a.ResourceType == ResourceTypes.System);
            actions.ForEach(async a => await access.AddResourceAction(contextUserId, a));

            // Setup policy with actions and add policy to store
            var policy = Policies.GetStandardPolicies().Find(p => p.Id == StandardPolicies.AccessAdmin);
            _ = await access.AddResourcePolicy(contextUserId, policy, bypassIntegrityCheck: true);

            // Setup and add roles and policy assignments to store
            var role = Policies.GetStandardRoles().Find(r => r.Id == StandardRoles.Administrator.Id);
            await access.AddRole(contextUserId, role);

            // Assign security principals to roles and add roles and policy assignments to store
            var assignedRoles = new List<AssignedRole> { TestAssignedRoles.Admin, TestAssignedRoles.TestAdmin };
            assignedRoles.ForEach(async r => await access.AddAssignedRole(contextUserId, r, bypassIntegrityCheck: true));

            // Validate email of SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() before update
            var storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, adminSecurityPrincipals[0].Id);
            Assert.AreEqual(storedSecurityPrincipal.Email, adminSecurityPrincipals[0].Email, $"Expected SecurityPrincipal returned by GetSecurityPrincipal() to match Email '{adminSecurityPrincipals[0].Email}' of SecurityPrincipal passed into AddSecurityPrincipal()");

            // Update security principal in AccessControl with a new principal object of the same Id, but different email
            var updateSecurityPrincipal = new SecurityPrincipal
            {
                Id = TestSecurityPrincipals.Admin.Id,
                Username = TestSecurityPrincipals.Admin.Username,
                Description = TestSecurityPrincipals.Admin.Description,
                Email = "new_test_email@test.com"
            };

            var securityPrincipalUpdated = await access.UpdateSecurityPrincipal(contextUserId, updateSecurityPrincipal);
            Assert.IsTrue(securityPrincipalUpdated, $"Expected security principal update for {updateSecurityPrincipal.Id} to succeed");

            // Validate email of SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() after update
            storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, updateSecurityPrincipal.Id);
            Assert.AreNotEqual(storedSecurityPrincipal.Email, adminSecurityPrincipals[0].Email, $"Expected SecurityPrincipal returned by GetSecurityPrincipal() to not match previous Email '{adminSecurityPrincipals[0].Email}' of SecurityPrincipal passed into AddSecurityPrincipal()");
            Assert.AreEqual(storedSecurityPrincipal.Email, updateSecurityPrincipal.Email, $"Expected SecurityPrincipal returned by GetSecurityPrincipal() to match updated Email '{updateSecurityPrincipal.Email}' of SecurityPrincipal passed into AddSecurityPrincipal()");
        }

        [TestMethod]
        public async Task TestUpdateSecurityPrincipalNoRoles()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };
            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            // Validate email of SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() before update
            var storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, adminSecurityPrincipals[0].Id);
            Assert.AreEqual(storedSecurityPrincipal.Email, adminSecurityPrincipals[0].Email, $"Expected SecurityPrincipal returned by GetSecurityPrincipal() to match Email '{adminSecurityPrincipals[0].Email}' of SecurityPrincipal passed into AddSecurityPrincipal()");

            // Update security principal in AccessControl with a new principal object of the same Id, but different email, with roles undefined
            var newSecurityPrincipal = TestSecurityPrincipals.Admin;
            newSecurityPrincipal.Email = "new_test_email@test.com";

            var securityPrincipalUpdated = await access.UpdateSecurityPrincipal(contextUserId, newSecurityPrincipal);
            Assert.IsFalse(securityPrincipalUpdated, $"Expected security principal update for {newSecurityPrincipal.Id} to be unsuccessful without roles defined");
        }

        [TestMethod]
        public async Task TestUpdateSecurityPrincipalBadUpdate()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };
            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            // Setup security principal actions
            var actions = Policies.GetStandardResourceActions().FindAll(a => a.ResourceType == ResourceTypes.System);
            actions.ForEach(async a => await access.AddResourceAction(contextUserId, a));

            // Setup policy with actions and add policy to store
            var policy = Policies.GetStandardPolicies().Find(p => p.Id == StandardPolicies.AccessAdmin);
            _ = await access.AddResourcePolicy(contextUserId, policy, bypassIntegrityCheck: true);

            // Setup and add roles and policy assignments to store
            var role = Policies.GetStandardRoles().Find(r => r.Id == StandardRoles.Administrator.Id);
            await access.AddRole(contextUserId, role, bypassIntegrityCheck: true);

            // Assign security principals to roles and add roles and policy assignments to store
            var assignedRoles = new List<AssignedRole> { TestAssignedRoles.Admin, TestAssignedRoles.TestAdmin };
            assignedRoles.ForEach(async r => await access.AddAssignedRole(contextUserId, r, bypassIntegrityCheck: true));

            // Validate email of SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() before update
            var storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, adminSecurityPrincipals[0].Id);
            Assert.AreEqual(storedSecurityPrincipal.Email, adminSecurityPrincipals[0].Email, $"Expected SecurityPrincipal returned by GetSecurityPrincipal() to match Email '{adminSecurityPrincipals[0].Email}' of SecurityPrincipal passed into AddSecurityPrincipal()");

            // Update security principal in AccessControl with a new principal object with a bad Id name and different email
            var newSecurityPrincipal = TestSecurityPrincipals.Admin;
            newSecurityPrincipal.Email = "new_test_email@test.com";
            newSecurityPrincipal.Id = "missing_username";
            var securityPrincipalUpdated = await access.UpdateSecurityPrincipal(contextUserId, newSecurityPrincipal);

            Assert.IsFalse(securityPrincipalUpdated, $"Expected security principal update for {newSecurityPrincipal.Id} to be unsuccessful with bad update data (Id does not already exist in store)");
        }

        [TestMethod]
        public async Task TestUpdateSecurityPrincipalPassword()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };
            var firstPassword = adminSecurityPrincipals[0].Password;
            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            // Setup security principal actions
            var actions = Policies.GetStandardResourceActions().FindAll(a => a.ResourceType == ResourceTypes.System);
            actions.ForEach(async a => await access.AddResourceAction(contextUserId, a));

            // Setup policy with actions and add policy to store
            var policy = Policies.GetStandardPolicies().Find(p => p.Id == StandardPolicies.AccessAdmin);
            _ = await access.AddResourcePolicy(contextUserId, policy, bypassIntegrityCheck: true);

            // Setup and add roles and policy assignments to store
            var role = Policies.GetStandardRoles().Find(r => r.Id == StandardRoles.Administrator.Id);
            await access.AddRole(contextUserId, role, bypassIntegrityCheck: true);

            // Assign security principals to roles and add roles and policy assignments to store
            var assignedRoles = new List<AssignedRole> { TestAssignedRoles.Admin, TestAssignedRoles.TestAdmin };
            assignedRoles.ForEach(async r => await access.AddAssignedRole(contextUserId, r, bypassIntegrityCheck: true));

            // Validate password of SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() before update
            var storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, adminSecurityPrincipals[0].Id, includePassword: true);
            var firstPasswordHashed = access.HashPassword(firstPassword, storedSecurityPrincipal.Password.Split('.')[1]);
            Assert.AreEqual(storedSecurityPrincipal.Password, firstPasswordHashed, $"Expected SecurityPrincipal returned by GetSecurityPrincipal() to match Password '{firstPasswordHashed}' of SecurityPrincipal passed into AddSecurityPrincipal()");

            // Update security principal in AccessControl with a new password
            var newPassword = "GFEDCBA";
            var securityPrincipalUpdated = await access.UpdateSecurityPrincipalPassword(contextUserId, new Models.Hub.SecurityPrincipalPasswordUpdate(adminSecurityPrincipals[0].Id, firstPassword, newPassword));
            Assert.IsTrue(securityPrincipalUpdated, $"Expected security principal password update for {adminSecurityPrincipals[0].Id} to succeed");

            // Validate password of SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() after update
            storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, adminSecurityPrincipals[0].Id, includePassword: true);
            var newPasswordHashed = access.HashPassword(newPassword, storedSecurityPrincipal.Password.Split('.')[1]);

            Assert.AreNotEqual(storedSecurityPrincipal.Password, firstPasswordHashed, $"Expected SecurityPrincipal returned by GetSecurityPrincipal() to not match previous Password '{firstPasswordHashed}' of SecurityPrincipal passed into AddSecurityPrincipal()");
            Assert.AreEqual(storedSecurityPrincipal.Password, newPasswordHashed, $"Expected SecurityPrincipal returned by GetSecurityPrincipal() to match updated Password '{newPasswordHashed}' of SecurityPrincipal passed into AddSecurityPrincipal()");
        }

        [TestMethod]
        public async Task TestUpdateSecurityPrincipalPasswordNoRoles()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };
            var firstPassword = adminSecurityPrincipals[0].Password;
            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            // Update security principal in AccessControl with a new password
            var newPassword = "GFEDCBA";
            var securityPrincipalUpdated = await access.UpdateSecurityPrincipalPassword(contextUserId, new Models.Hub.SecurityPrincipalPasswordUpdate(adminSecurityPrincipals[0].Id, firstPassword, newPassword));
            Assert.IsFalse(securityPrincipalUpdated, $"Expected security principal password update for {adminSecurityPrincipals[0].Id} to fail without roles");

            // Validate password of SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() after failed update
            var storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, adminSecurityPrincipals[0].Id, includePassword: true);
            var firstPasswordHashed = access.HashPassword(firstPassword, storedSecurityPrincipal.Password.Split('.')[1]);

            Assert.AreEqual(storedSecurityPrincipal.Password, firstPasswordHashed, $"Expected SecurityPrincipal returned by GetSecurityPrincipal() to match Password '{firstPasswordHashed}' of SecurityPrincipal passed into AddSecurityPrincipal()");
        }

        [TestMethod]
        public async Task TestUpdateSecurityPrincipalPasswordBadPassword()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };
            var firstPassword = adminSecurityPrincipals[0].Password;
            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            // Setup security principal actions
            var actions = Policies.GetStandardResourceActions().FindAll(a => a.ResourceType == ResourceTypes.System);
            actions.ForEach(async a => await access.AddResourceAction(contextUserId, a));

            // Setup policy with actions and add policy to store
            var policy = Policies.GetStandardPolicies().Find(p => p.Id == StandardPolicies.AccessAdmin);
            _ = await access.AddResourcePolicy(contextUserId, policy, bypassIntegrityCheck: true);

            // Setup and add roles and policy assignments to store
            var role = Policies.GetStandardRoles().Find(r => r.Id == StandardRoles.Administrator.Id);
            await access.AddRole(contextUserId, role);

            // Assign security principals to roles and add roles and policy assignments to store
            var assignedRoles = new List<AssignedRole> { TestAssignedRoles.Admin, TestAssignedRoles.TestAdmin };
            assignedRoles.ForEach(async r => await access.AddAssignedRole(contextUserId, r));

            // Update security principal in AccessControl with a new password, but wrong original password
            var newPassword = "GFEDCBA";
            var securityPrincipalUpdated = await access.UpdateSecurityPrincipalPassword(contextUserId, new Models.Hub.SecurityPrincipalPasswordUpdate(adminSecurityPrincipals[0].Id, firstPassword.ToLower(), newPassword));
            Assert.IsFalse(securityPrincipalUpdated, $"Expected security principal password update for {adminSecurityPrincipals[0].Id} to fail with wrong password");

            // Validate password of SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() after failed update
            var storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, adminSecurityPrincipals[0].Id, includePassword: true);
            var firstPasswordHashed = access.HashPassword(firstPassword, storedSecurityPrincipal.Password.Split('.')[1]);
            Assert.AreEqual(storedSecurityPrincipal.Password, firstPasswordHashed, $"Expected SecurityPrincipal returned by GetSecurityPrincipal() to match Password '{firstPasswordHashed}' of SecurityPrincipal passed into AddSecurityPrincipal()");
        }

        [TestMethod]
        public async Task TestDeleteSecurityPrincipal()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };
            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            // Setup security principal actions
            var actions = Policies.GetStandardResourceActions().FindAll(a => a.ResourceType == ResourceTypes.System);
            actions.ForEach(async a => await access.AddResourceAction(contextUserId, a));

            // Setup policy with actions and add policy to store
            var policy = Policies.GetStandardPolicies().Find(p => p.Id == StandardPolicies.AccessAdmin);
            _ = await access.AddResourcePolicy(contextUserId, policy, bypassIntegrityCheck: true);

            // Setup and add roles and policy assignments to store
            var role = Policies.GetStandardRoles().Find(r => r.Id == StandardRoles.Administrator.Id);
            await access.AddRole(contextUserId, role, bypassIntegrityCheck: true);

            // Assign security principals to roles and add roles and policy assignments to store
            var assignedRoles = new List<AssignedRole> { TestAssignedRoles.Admin, TestAssignedRoles.TestAdmin };
            assignedRoles.ForEach(async r => await access.AddAssignedRole(contextUserId, r, bypassIntegrityCheck: true));

            // Validate SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() before delete is not null
            var storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, adminSecurityPrincipals[0].Id);
            Assert.IsNotNull(storedSecurityPrincipal, "Expected object returned by AccessControl.GetSecurityPrincipal() to not be null");
            Assert.AreEqual(storedSecurityPrincipal.Id, adminSecurityPrincipals[0].Id, $"Expected SecurityPrincipal returned by GetSecurityPrincipal() to match Id '{adminSecurityPrincipals[0].Id}' of SecurityPrincipal passed into AddSecurityPrincipal()");

            // Delete first security principal in adminSecurityPrincipals list from AccessControl store
            var securityPrincipalDeleted = await access.DeleteSecurityPrincipal(contextUserId, adminSecurityPrincipals[0].Id);
            Assert.IsTrue(securityPrincipalDeleted, $"Expected security principal deletion for {adminSecurityPrincipals[0].Id} to succeed");

            // Validate SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() after delete is null
            storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, adminSecurityPrincipals[0].Id);
            Assert.IsNull(storedSecurityPrincipal, $"Expected SecurityPrincipal for '{adminSecurityPrincipals[0].Id}' to be null from GetSecurityPrincipal()");
        }

        [TestMethod]
        public async Task TestDeleteSecurityPrincipalNoRoles()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };
            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            // Validate SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() before delete is not null
            var storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, adminSecurityPrincipals[0].Id);
            Assert.IsNotNull(storedSecurityPrincipal, "Expected object returned by AccessControl.GetSecurityPrincipal() to not be null");
            Assert.AreEqual(storedSecurityPrincipal.Id, adminSecurityPrincipals[0].Id, $"Expected SecurityPrincipal returned by GetSecurityPrincipal() to match Id '{adminSecurityPrincipals[0].Id}' of SecurityPrincipal passed into AddSecurityPrincipal()");

            // Try to delete first security principal in adminSecurityPrincipals list from AccessControl store
            var securityPrincipalDeleted = await access.DeleteSecurityPrincipal(contextUserId, adminSecurityPrincipals[0].Id);
            Assert.IsFalse(securityPrincipalDeleted, $"Expected security principal deletion for {adminSecurityPrincipals[0].Id} to fail without roles defined");

            // Validate SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() after delete is not null
            storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, adminSecurityPrincipals[0].Id);
            Assert.IsNotNull(storedSecurityPrincipal, $"Expected SecurityPrincipal for '{adminSecurityPrincipals[0].Id}' to not be null from GetSecurityPrincipal()");
        }

        [TestMethod]
        public async Task TestDeleteSecurityPrincipalSelfDeletion()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };
            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            // Setup security principal actions
            var actions = Policies.GetStandardResourceActions().FindAll(a => a.ResourceType == ResourceTypes.System);
            actions.ForEach(async a => await access.AddResourceAction(contextUserId, a));

            // Setup policy with actions and add policy to store
            var policy = Policies.GetStandardPolicies().Find(p => p.Id == StandardPolicies.AccessAdmin);
            _ = await access.AddResourcePolicy(contextUserId, policy, bypassIntegrityCheck: true);

            // Setup and add roles and policy assignments to store
            var role = Policies.GetStandardRoles().Find(r => r.Id == StandardRoles.Administrator.Id);
            await access.AddRole(contextUserId, role);

            // Assign security principals to roles and add roles and policy assignments to store
            var assignedRoles = new List<AssignedRole> { TestAssignedRoles.Admin, TestAssignedRoles.TestAdmin };
            assignedRoles.ForEach(async r => await access.AddAssignedRole(contextUserId, r));

            // Validate SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() before delete is not null
            var storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, adminSecurityPrincipals[1].Id);
            Assert.IsNotNull(storedSecurityPrincipal, "Expected object returned by AccessControl.GetSecurityPrincipal() to not be null");
            Assert.AreEqual(storedSecurityPrincipal.Id, adminSecurityPrincipals[1].Id, $"Expected SecurityPrincipal returned by GetSecurityPrincipal() to match Id '{adminSecurityPrincipals[1].Id}' of SecurityPrincipal passed into AddSecurityPrincipal()");

            // Try to delete second security principal in adminSecurityPrincipals list from AccessControl store
            var securityPrincipalDeleted = await access.DeleteSecurityPrincipal(contextUserId, contextUserId);
            Assert.IsFalse(securityPrincipalDeleted, $"Expected security principal self deletion for {contextUserId} to fail");

            // Validate SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() after delete is not null
            storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, adminSecurityPrincipals[1].Id);
            Assert.IsNotNull(storedSecurityPrincipal, $"Expected SecurityPrincipal for '{adminSecurityPrincipals[1].Id}' to not be null from GetSecurityPrincipal()");
        }

        [TestMethod]
        public async Task TestDeleteSecurityPrincipalBadId()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };
            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            // Setup security principal actions
            var actions = Policies.GetStandardResourceActions().FindAll(a => a.ResourceType == ResourceTypes.System);
            actions.ForEach(async a => await access.AddResourceAction(contextUserId, a));

            // Setup policy with actions and add policy to store
            var policy = Policies.GetStandardPolicies().Find(p => p.Id == StandardPolicies.AccessAdmin);
            _ = await access.AddResourcePolicy(contextUserId, policy, bypassIntegrityCheck: true);

            // Setup and add roles and policy assignments to store
            var role = Policies.GetStandardRoles().Find(r => r.Id == StandardRoles.Administrator.Id);
            await access.AddRole(contextUserId, role);

            // Assign security principals to roles and add roles and policy assignments to store
            var assignedRoles = new List<AssignedRole> { TestAssignedRoles.Admin, TestAssignedRoles.TestAdmin };
            assignedRoles.ForEach(async r => await access.AddAssignedRole(contextUserId, r));

            // Validate SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() before delete is not null
            var storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, adminSecurityPrincipals[1].Id);
            Assert.IsNotNull(storedSecurityPrincipal, "Expected object returned by AccessControl.GetSecurityPrincipal() to not be null");
            Assert.AreEqual(storedSecurityPrincipal.Id, adminSecurityPrincipals[1].Id, $"Expected SecurityPrincipal returned by GetSecurityPrincipal() to match Id '{adminSecurityPrincipals[1].Id}' of SecurityPrincipal passed into AddSecurityPrincipal()");

            // Try to delete second security principal in adminSecurityPrincipals list from AccessControl store
            var securityPrincipalDeleted = await access.DeleteSecurityPrincipal(contextUserId, contextUserId.ToUpper());
            Assert.IsFalse(securityPrincipalDeleted, $"Expected security principal deletion for {contextUserId.ToUpper()} to fail");

            // Validate SecurityPrincipal object returned by AccessControl.GetSecurityPrincipal() after delete is not null
            storedSecurityPrincipal = await access.GetSecurityPrincipal(contextUserId, adminSecurityPrincipals[1].Id);
            Assert.IsNotNull(storedSecurityPrincipal, $"Expected SecurityPrincipal for '{adminSecurityPrincipals[1].Id}' to not be null from GetSecurityPrincipal()");
        }

        [TestMethod]
        public async Task TestIsPrincipalInRole()
        {
            // Add test security principals
            var adminSecurityPrincipals = new List<SecurityPrincipal> { TestSecurityPrincipals.Admin, TestSecurityPrincipals.TestAdmin };
            adminSecurityPrincipals.ForEach(async p => await access.AddSecurityPrincipal(contextUserId, p, bypassIntegrityCheck: true));

            // Setup security principal actions
            var actions = Policies.GetStandardResourceActions().FindAll(a => a.ResourceType == ResourceTypes.System);
            actions.ForEach(async a => await access.AddResourceAction(contextUserId, a, bypassIntegrityCheck: true));

            // Setup policy with actions and add policy to store
            var policy = Policies.GetStandardPolicies().Find(p => p.Id == StandardPolicies.AccessAdmin);
            _ = await access.AddResourcePolicy(contextUserId, policy, bypassIntegrityCheck: true);

            // Setup and add roles and policy assignments to store
            var role = Policies.GetStandardRoles().Find(r => r.Id == StandardRoles.Administrator.Id);
            await access.AddRole(contextUserId, role, bypassIntegrityCheck: true);

            // Assign security principals to roles and add roles and policy assignments to store
            var assignedRoles = new List<AssignedRole> { TestAssignedRoles.Admin, TestAssignedRoles.TestAdmin };
            assignedRoles.ForEach(async r => await access.AddAssignedRole(contextUserId, r, bypassIntegrityCheck: true));

            // Validate specified admin user is a principal role
            bool hasAccess;
            foreach (var assignedRole in assignedRoles)
            {
                hasAccess = await access.IsPrincipalInRole(contextUserId, assignedRole.SecurityPrincipalId, StandardRoles.Administrator.Id);
                Assert.IsTrue(hasAccess, $"User '{assignedRole.SecurityPrincipalId}' should be in role");
            }

            // Validate fake admin user is not a principal role
            hasAccess = await access.IsPrincipalInRole(contextUserId, "admin_02", StandardRoles.Administrator.Id);
            Assert.IsFalse(hasAccess, "User should not be in role");
        }

        [TestMethod]
        public async Task TestDomainAuth()
        {
            // Add test devops user security principal
            _ = await access.AddSecurityPrincipal(contextUserId, TestSecurityPrincipals.DevopsUser, bypassIntegrityCheck: true);

            // Setup security principal actions
            await access.AddResourceAction(contextUserId, Policies.GetStandardResourceActions().Find(r => r.Id == StandardResourceActions.CertificateDownload));

            // Setup policy with actions and add policy to store
            var policy = Policies.GetStandardPolicies().Find(p => p.Id == StandardPolicies.CertificateConsumer);
            _ = await access.AddResourcePolicy(contextUserId, policy, bypassIntegrityCheck: true);

            // Setup and add roles and policy assignments to store
            var role = Policies.GetStandardRoles().Find(r => r.Id == StandardRoles.CertificateConsumer.Id);
            await access.AddRole(contextUserId, role, bypassIntegrityCheck: true);

            // Assign security principals to roles and add roles and policy assignments to store
            await access.AddAssignedRole(contextUserId, TestAssignedRoles.DevopsUserDomainConsumer, true); // devops user in consumer role for a specific domain

            // Validate user can consume a cert for a given domain 
            var isAuthorised = await access.IsSecurityPrincipalAuthorised(contextUserId, new AccessCheck(TestSecurityPrincipals.DevopsAppDomainConsumer.Id, ResourceTypes.Domain, StandardResourceActions.CertificateDownload, identifier: "www.example.com"));
            Assert.IsTrue(isAuthorised, "User should be a cert consumer for this domain");

            // Validate user can't consume a cert for a subdomain they haven't been granted
            isAuthorised = await access.IsSecurityPrincipalAuthorised(contextUserId, new AccessCheck(TestSecurityPrincipals.DevopsAppDomainConsumer.Id, ResourceTypes.Domain, StandardResourceActions.CertificateDownload, identifier: "secure.example.com"));
            Assert.IsFalse(isAuthorised, "User should not be a cert consumer for this domain");
        }

        [TestMethod]
        public async Task TestWildcardDomainAuth()
        {
            // Add test devops user security principal
            _ = await access.AddSecurityPrincipal(contextUserId, TestSecurityPrincipals.DevopsUser, bypassIntegrityCheck: true);

            // Setup security principal actions
            await access.AddResourceAction(contextUserId, Policies.GetStandardResourceActions().Find(r => r.Id == StandardResourceActions.CertificateDownload));

            // Setup policy with actions and add policy to store
            var policy = Policies.GetStandardPolicies().Find(p => p.Id == StandardPolicies.CertificateConsumer);
            _ = await access.AddResourcePolicy(contextUserId, policy, bypassIntegrityCheck: true);

            // Setup and add roles and policy assignments to store
            var role = Policies.GetStandardRoles().Find(r => r.Id == StandardRoles.CertificateConsumer.Id);
            await access.AddRole(contextUserId, role, bypassIntegrityCheck: true);

            // Assign security principals to roles and add roles and policy assignments to store
            await access.AddAssignedRole(contextUserId, TestAssignedRoles.DevopsUserWildcardDomainConsumer, bypassIntegrityCheck: true); // devops user in consumer role for a wildcard domain

            // Validate user can consume any subdomain via a granted wildcard
            var isAuthorised = await access.IsSecurityPrincipalAuthorised(contextUserId, new AccessCheck(TestSecurityPrincipals.DevopsUser.Id, ResourceTypes.Domain, StandardResourceActions.CertificateDownload, identifier: "random.microsoft.com"));
            Assert.IsTrue(isAuthorised, "User should be a cert consumer for this subdomain via wildcard");

            // Validate user can't consume a random wildcard
            isAuthorised = await access.IsSecurityPrincipalAuthorised(contextUserId, new AccessCheck(TestSecurityPrincipals.DevopsUser.Id, ResourceTypes.Domain, StandardResourceActions.CertificateDownload, identifier: "*  lkjhasdf98862364"));
            Assert.IsFalse(isAuthorised, "User should not be a cert consumer for random wildcard");

            // Validate user can't consume a random wildcard
            isAuthorised = await access.IsSecurityPrincipalAuthorised(contextUserId, new AccessCheck(TestSecurityPrincipals.DevopsUser.Id, ResourceTypes.Domain, StandardResourceActions.CertificateDownload, identifier: "lkjhasdf98862364.*.microsoft.com"));
            Assert.IsFalse(isAuthorised, "User should not be a cert consumer for random wildcard");
        }

        [TestMethod]
        public async Task TestRandomUserAuth()
        {
            // Add test devops user security principal
            _ = await access.AddSecurityPrincipal(contextUserId, TestSecurityPrincipals.DevopsUser, bypassIntegrityCheck: true);

            // Setup security principal actions
            await access.AddResourceAction(contextUserId, Policies.GetStandardResourceActions().Find(r => r.Id == StandardResourceActions.CertificateDownload));

            // Setup policy with actions and add policy to store
            var policy = Policies.GetStandardPolicies().Find(p => p.Id == StandardPolicies.CertificateConsumer);
            _ = await access.AddResourcePolicy(contextUserId, policy, bypassIntegrityCheck: true);

            // Setup and add roles and policy assignments to store
            var role = Policies.GetStandardRoles().Find(r => r.Id == StandardRoles.CertificateConsumer.Id);
            await access.AddRole(contextUserId, role);

            // Assign security principals to roles and add roles and policy assignments to store
            await access.AddAssignedRole(contextUserId, TestAssignedRoles.DevopsUserWildcardDomainConsumer); // devops user in consumer role for a wildcard domain

            // Validate that random user should not be authorised
            var isAuthorised = await access.IsSecurityPrincipalAuthorised(contextUserId, new AccessCheck("randomuser", ResourceTypes.Domain, StandardResourceActions.CertificateDownload, identifier: "random.microsoft.com"));
            Assert.IsFalse(isAuthorised, "Unknown user should not be a cert consumer for this subdomain via wildcard");
        }

        [TestMethod]
        public async Task TestSecurityPrincipalPwdValid()
        {
            // Add test devops user security principal
            _ = await access.AddSecurityPrincipal(contextUserId, TestSecurityPrincipals.DevopsUser, bypassIntegrityCheck: true);
            var check = await access.CheckSecurityPrincipalPassword(contextUserId, new Models.Hub.SecurityPrincipalPasswordCheck(TestSecurityPrincipals.DevopsUser.Id, TestSecurityPrincipals.DevopsUser.Password));

            Assert.IsTrue(check.IsSuccess, "Password should be valid");
        }

        [TestMethod]
        public async Task TestSecurityPrincipalPwdInvalid()
        {
            // Add test devops user security principal
            _ = await access.AddSecurityPrincipal(contextUserId, TestSecurityPrincipals.DevopsUser, bypassIntegrityCheck: true);
            var check = await access.CheckSecurityPrincipalPassword(contextUserId, new Models.Hub.SecurityPrincipalPasswordCheck(TestSecurityPrincipals.DevopsUser.Id, "INVALID_PWD"));

            Assert.IsFalse(check.IsSuccess, "Password should not be valid");
        }

        [TestMethod]
        public async Task TestUserAPIToken()
        {
            // setup a test security principal, add them to the certificate consumer role, assign an API token then test if they are authorized based on the API token

            // allow test admin to perform access checks
            var assignedRoles = new List<AssignedRole> { TestAssignedRoles.TestAdmin };
            assignedRoles.ForEach(async r => await access.AddAssignedRole(contextUserId, r, bypassIntegrityCheck: true));

            // Add test devops user security principal
            _ = await access.AddSecurityPrincipal(contextUserId, TestSecurityPrincipals.DevopsUser, bypassIntegrityCheck: true);

            // Setup security principal actions
            await access.AddResourceAction(contextUserId, Policies.GetStandardResourceActions().Find(r => r.Id == StandardResourceActions.CertificateDownload));

            // Setup policy with actions and add policy to store
            var policy = Policies.GetStandardPolicies().Find(p => p.Id == StandardPolicies.CertificateConsumer);
            _ = await access.AddResourcePolicy(contextUserId, policy, bypassIntegrityCheck: true);

            // Setup and add roles and policy assignments to store
            var role = Policies.GetStandardRoles().Find(r => r.Id == StandardRoles.CertificateConsumer.Id);
            await access.AddRole(contextUserId, role);

            // Assign security principals to roles and add roles and policy assignments to store
            await access.AddAssignedRole(contextUserId, TestAssignedRoles.DevopsUserWildcardDomainConsumer); // devops user in consumer role for a wildcard domain

            var assignedRolesForDevopsUser = await access.GetAssignedRoles(contextUserId, TestSecurityPrincipals.DevopsUser.Id);

            // create and assign a new API token
            var apiToken = new AccessToken { ClientId = TestSecurityPrincipals.DevopsUser.Id, Secret = Guid.NewGuid().ToString(), TokenType = AccessTokenTypes.Simple, Description = "An example API token" };
            var apiExpiredToken = new AccessToken { ClientId = TestSecurityPrincipals.DevopsUser.Id, Secret = Guid.NewGuid().ToString(), TokenType = AccessTokenTypes.Simple, Description = "An example expired API token", DateExpiry = DateTimeOffset.UtcNow.AddDays(-1) };
            var apiRevokedToken = new AccessToken { ClientId = TestSecurityPrincipals.DevopsUser.Id, Secret = Guid.NewGuid().ToString(), TokenType = AccessTokenTypes.Simple, Description = "An example revoked API token", DateRevoked = DateTimeOffset.UtcNow.AddDays(-1) };
            var apiTokenBad = new AccessToken { ClientId = TestSecurityPrincipals.DomainOwner.Id, Secret = Guid.NewGuid().ToString(), TokenType = AccessTokenTypes.Simple, Description = "An example bad API token (invalid client id)" };
            var assignedToken = new AssignedAccessToken
            {
                AccessTokens = [apiToken, apiExpiredToken, apiRevokedToken],
                SecurityPrincipalId = TestSecurityPrincipals.DevopsUser.Id,
                Title = "test token",
                ScopedAssignedRoles = [assignedRolesForDevopsUser.First(r => r.RoleId == StandardRoles.CertificateConsumer.Id).Id]
            };

            await access.AddAssignedAccessToken(contextUserId, assignedToken);

            var isAuthorized = await access.IsAccessTokenAuthorised(contextUserId, apiToken, new AccessCheck(null, ResourceTypes.Domain, StandardResourceActions.CertificateDownload, identifier: "random.microsoft.com"));
            Assert.IsTrue(isAuthorized.IsSuccess, "Token should have access");

            isAuthorized = await access.IsAccessTokenAuthorised(contextUserId, apiToken, new AccessCheck(null, ResourceTypes.Domain, StandardResourceActions.CertificateDownload, identifier: "random.test.com"));
            Assert.IsFalse(isAuthorized.IsSuccess, "Token should not have access (wrong domain identifier resource)");

            isAuthorized = await access.IsAccessTokenAuthorised(contextUserId, apiTokenBad, new AccessCheck(null, ResourceTypes.Domain, StandardResourceActions.CertificateDownload, identifier: "random.microsoft.com"));
            Assert.IsFalse(isAuthorized.IsSuccess, "Token should not have access (bad token)");

            isAuthorized = await access.IsAccessTokenAuthorised(contextUserId, apiExpiredToken, new AccessCheck(null, ResourceTypes.Domain, StandardResourceActions.CertificateDownload, identifier: "random.microsoft.com"));
            Assert.IsFalse(isAuthorized.IsSuccess, "Token should not have access (expired)");

            isAuthorized = await access.IsAccessTokenAuthorised(contextUserId, apiRevokedToken, new AccessCheck(null, ResourceTypes.Domain, StandardResourceActions.CertificateDownload, identifier: "random.microsoft.com"));
            Assert.IsFalse(isAuthorized.IsSuccess, "Token should not have access (revoked)");

        }

        [TestMethod]
        public void TestAllStandardResourceActionsAreAllowedByAtLeastOneRole()
        {
            var actions = Policies.GetStandardResourceActions();
            var policies = Policies.GetStandardPolicies();
            var roles = Policies.GetStandardRoles();

            var policyIdToRoles = roles
                .SelectMany(role => role.Policies.Select(policyId => new { role, policyId }))
                .GroupBy(x => x.policyId, x => x.role)
                .ToDictionary(g => g.Key, g => g.ToList());

            var actionToAllowedRoles = new Dictionary<string, List<Role>>();

            foreach (var action in actions)
            {
                var allowedRoles = new List<Role>();
                foreach (var policy in policies)
                {
                    if (policy.ResourceActions.Contains(action.Id))
                    {
                        if (policyIdToRoles.TryGetValue(policy.Id, out var rolesForPolicy))
                        {
                            allowedRoles.AddRange(rolesForPolicy);
                        }
                    }
                }

                actionToAllowedRoles[action.Id] = allowedRoles.Distinct().ToList();
            }

            var actionsWithoutRoles = actionToAllowedRoles.Where(kvp => kvp.Value.Count == 0).Select(kvp => kvp.Key).ToList();

            Assert.IsTrue(actionsWithoutRoles.Count == 0, $"The following {actionsWithoutRoles.Count} actions are not allowed by any role: {string.Join(", \r\n", actionsWithoutRoles)}");

            // Additional assertion: Administrator role is allowed to perform each action
            var adminRole = roles.FirstOrDefault(r => r.Id == StandardRoles.Administrator.Id);
            Assert.IsNotNull(adminRole, "Administrator role must exist in standard roles.");

            var actionsNotAllowedByAdmin = actionToAllowedRoles
                .Where(kvp => !kvp.Value.Any(r => r.Id == adminRole.Id))
                .Select(kvp => kvp.Key)
                .ToList();

            // Remove actions that are not applicable to the administrator role
            actionsNotAllowedByAdmin.RemoveAll(a => a == StandardResourceActions.StoredCredentialReadSecret);
            actionsNotAllowedByAdmin.RemoveAll(a => a == StandardResourceActions.ManagementHubInstanceJoin);
            actionsNotAllowedByAdmin.RemoveAll(a => a == StandardResourceActions.ManagedChallengeRequest);
            actionsNotAllowedByAdmin.RemoveAll(a => a == StandardResourceActions.ManagedChallengeCleanup);

            Assert.IsTrue(actionsNotAllowedByAdmin.Count == 0, $"Administrator role is not allowed to perform the following actions: {string.Join(", \r\n", actionsNotAllowedByAdmin)}");
        }
    }
}
