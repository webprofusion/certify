using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Models;
using Certify.Models.Config.AccessControl;
using Certify.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;

namespace Certify.Core.Tests.Unit
{
    public class MemoryObjectStore : IAccessControlStore
    {
        private ConcurrentDictionary<string, AccessStoreItem> _store = new ConcurrentDictionary<string, AccessStoreItem>();

        public Task Add<T>(string itemType, AccessStoreItem item)
        {
            item.ItemType = itemType;
            return Task.FromResult(_store.TryAdd(item.Id, item));
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
            var o = item as AccessStoreItem;
            o.ItemType = itemType;
            return Task.FromResult(_store.TryAdd(o.Id, o));
        }

        public Task Update<T>(string itemType, T item)
        {
            var o = item as AccessStoreItem;
            return Task.FromResult(_store.TryUpdate(o.Id, o, o));
        }
    }

    [TestClass]
    public class AccessControlTests
    {
        private List<SecurityPrinciple> GetTestSecurityPrinciples()
        {
            return new List<SecurityPrinciple>
            {
                new SecurityPrinciple
                {
                    Id = "admin_01",
                    Username = "admin",
                    Description = "Administrator account",
                    Email = "info@test.com",
                    Password = "ABCDEFG",
                    PrincipleType = SecurityPrincipleType.User
                },
                new SecurityPrinciple
                {
                    Id = "domain_owner_01",
                    Username = "demo_owner",
                    Description = "Example domain owner",
                    Email = "domains@test.com",
                    Password = "ABCDEFG",
                    PrincipleType = SecurityPrincipleType.User
                },
                new SecurityPrinciple
                {
                    Id = "devops_user_01",
                    Username = "devops_01",
                    Description = "Example devops user",
                    Email = "devops01@test.com",
                    Password = "ABCDEFG",
                    PrincipleType = SecurityPrincipleType.User
                },
                new SecurityPrinciple
                {
                    Id = "devops_app_01",
                    Username = "devapp_01",
                    Description = "Example devops app domain consumer",
                    Email = "dev_app01@test.com",
                    Password = "ABCDEFG",
                    PrincipleType = SecurityPrincipleType.User
                },
                    new SecurityPrinciple
                {
                    Id = "[test]",
                    Username = "test administrator",
                    Description = "Example test administrator used as context user during test",
                    Email = "test_admin@test.com",
                    Password = "ABCDEFG",
                    PrincipleType = SecurityPrincipleType.User
                }
            };
        }

        [TestMethod]
        public async Task TestAccessControlChecks()
        {
            var log = new LoggerConfiguration()
                .WriteTo.Debug()
                .CreateLogger();

            var loggy = new Loggy(log);

            var access = new AccessControl(loggy, new MemoryObjectStore());

            var contextUserId = "[test]";

            // add test security principles
            var allPrinciples = GetTestSecurityPrinciples();
            foreach (var p in allPrinciples)
            {
                _ = await access.AddSecurityPrinciple(contextUserId, p, bypassIntegrityCheck: true);
            }

            // setup known actions
            var actions = Policies.GetStandardResourceActions();

            foreach (var action in actions)
            {
                await access.AddAction(action);
            }

            // setup policies with actions

            var policies = Policies.GetStandardPolicies();

            // add policies to store
            foreach (var r in policies)
            {
                _ = await access.AddResourcePolicy(contextUserId, r, bypassIntegrityCheck: true);
            }

            // setup roles with policies
            var roles = await access.GetSystemRoles();

            foreach (var r in roles)
            {
                // add roles and policy assignments to store
                await access.AddRole(r);
            }

            // assign security principles to roles
            var assignedRoles = new List<AssignedRole> {
                 // test administrator
                new AssignedRole{
                    RoleId=StandardRoles.Administrator.Id,
                    SecurityPrincipleId="[test]"
                },
                // administrator
                new AssignedRole{
                    RoleId=StandardRoles.Administrator.Id,
                    SecurityPrincipleId="admin_01"
                },
                // devops user in consumer role for a couple of specific domains
                new AssignedRole{
                    RoleId=StandardRoles.CertificateConsumer.Id,
                    SecurityPrincipleId="devops_user_01",
                    IncludedResources=new List<Resource>{
                        new Resource{ ResourceType=ResourceTypes.Domain, Identifier="www.example.com" },
                        new Resource{ ResourceType=ResourceTypes.Domain, Identifier="*.microsoft.com" }
                    }
                }
            };

            foreach (var r in assignedRoles)
            {
                // add roles and policy assignments to store
                await access.AddAssignedRole(r);
            }

            // assert

            var adminAssignedRoles = await access.GetAssignedRoles(contextUserId, "admin_01");
            Assert.AreEqual(1, adminAssignedRoles.Count);

            var hasAccess = await access.IsPrincipleInRole(contextUserId, "admin_01", StandardRoles.Administrator.Id);
            Assert.IsTrue(hasAccess, "User should be in role");

            hasAccess = await access.IsPrincipleInRole(contextUserId, "admin_02", StandardRoles.Administrator.Id);
            Assert.IsFalse(hasAccess, "User should not be in role");

            // check user can consume a cert for a given domain 
            var isAuthorised = await access.IsAuthorised(contextUserId, "devops_user_01", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "certificate_download", "www.example.com");
            Assert.IsTrue(isAuthorised, "User should be a cert consumer for this domain");

            // check user can't consume a cert for a subdomain they haven't been granted
            isAuthorised = await access.IsAuthorised(contextUserId, "devops_user_01", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "certificate_download", "secure.example.com");
            Assert.IsFalse(isAuthorised, "User should not be a cert consumer for this domain");

            // check user can consume any subdomain via a granted wildcard
            isAuthorised = await access.IsAuthorised(contextUserId, "devops_user_01", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "certificate_download", "random.microsoft.com");
            Assert.IsTrue(isAuthorised, "User should be a cert consumer for this subdomain via wildcard");

            // check user can't consume a random wildcard
            isAuthorised = await access.IsAuthorised(contextUserId, "devops_user_01", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "certificate_download", "*  lkjhasdf98862364");
            Assert.IsFalse(isAuthorised, "User should not be a cert consumer for random wildcard");

            // check user can't consume a random wildcard
            isAuthorised = await access.IsAuthorised(contextUserId, "devops_user_01", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "certificate_download", "lkjhasdf98862364.*.microsoft.com");
            Assert.IsFalse(isAuthorised, "User should not be a cert consumer for random wildcard");

            // random user should not be authorised
            isAuthorised = await access.IsAuthorised(contextUserId, "randomuser", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "certificate_download", "random.microsoft.com");
            Assert.IsFalse(isAuthorised, "Unknown user should not be a cert consumer for this subdomain via wildcard");
        }
    }
}
