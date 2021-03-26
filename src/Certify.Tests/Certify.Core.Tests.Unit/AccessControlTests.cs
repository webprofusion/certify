using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;

namespace Certify.Core.Tests.Unit
{

    public class MemoryObjectStore : IObjectStore
    {
        ConcurrentDictionary<string, object> _store = new ConcurrentDictionary<string, object>();

        public Task<T> Load<T>(string id)
        {
            if (_store.TryGetValue(id, out object value))
            {
                return Task.FromResult((T)value);
            }
            else
            {
                var empty = (T)Activator.CreateInstance(typeof(T));

                return Task.FromResult(empty);
            }
        }

        public Task<bool> Save<T>(string id, object item)
        {
            _ = _store.AddOrUpdate(id, item, (key, oldVal) => item);
            return Task.FromResult(true);
        }
    }

    [TestClass]
    public class AccessControlTests
    {

        private List<SecurityPrinciple> GetTestSecurityPrinciples()
        {

            return new List<SecurityPrinciple> {
                new SecurityPrinciple {
                    Id = "admin_01",
                    Username = "admin",
                    Description = "Administrator account",
                    Email="info@test.com", Password="ABCDEFG",
                    PrincipleType= SecurityPrincipleType.User,
                    SystemRoleIds=new List<string>{ StandardRoles.Administrator.Id
    }
},
                new SecurityPrinciple
                {
                    Id = "domain_owner_01",
                    Username = "demo_owner",
                    Description = "Example domain owner",
                    Email = "domains@test.com",
                    Password = "ABCDEFG",
                    PrincipleType = SecurityPrincipleType.User,
                    SystemRoleIds = new List<string> { StandardRoles.DomainOwner.Id }
                },
                 new SecurityPrinciple
                 {
                     Id = "devops_user_01",
                     Username = "devops_01",
                     Description = "Example devops user",
                     Email = "devops01@test.com",
                     Password = "ABCDEFG",
                     PrincipleType = SecurityPrincipleType.User,
                     SystemRoleIds = new List<string> { StandardRoles.CertificateConsumer.Id, StandardRoles.DomainRequestor.Id }
                 },
                  new SecurityPrinciple
                  {
                      Id = "devops_app_01",
                      Username = "devapp_01",
                      Description = "Example devops app domain consumer",
                      Email = "dev_app01@test.com",
                      Password = "ABCDEFG",
                      PrincipleType = SecurityPrincipleType.User,
                      SystemRoleIds = new List<string> { StandardRoles.CertificateConsumer.Id }
                  }
            };
        }

        public List<ResourceProfile> GetTestResourceProfiles()
        {
            return new List<ResourceProfile> {
                new ResourceProfile {
                    ResourceType = ResourceTypes.System,
                    AssignedRoles = new List<ResourceAssignedRole>{
                        new ResourceAssignedRole{ RoleId=StandardRoles.Administrator.Id, PrincipleId = "admin_01" },
                        new ResourceAssignedRole{ RoleId=StandardRoles.CertificateConsumer.Id, PrincipleId = "devops_user_01" },
                        new ResourceAssignedRole{ RoleId=StandardRoles.DomainRequestor.Id, PrincipleId = "devops_user_01" }
                    }
                },
                new ResourceProfile {
                    ResourceType = ResourceTypes.Domain,
                    Identifier = "example.com",
                    AssignedRoles= new List<ResourceAssignedRole>{
                        new ResourceAssignedRole{ RoleId=StandardRoles.CertificateConsumer.Id, PrincipleId = "devops_user_01" },
                        new ResourceAssignedRole{ RoleId=StandardRoles.DomainRequestor.Id, PrincipleId = "devops_user_01" }
                    }
                },
                  new ResourceProfile {
                    ResourceType = ResourceTypes.Domain,
                    Identifier = "www.example.com",
                    AssignedRoles= new List<ResourceAssignedRole>{
                        new ResourceAssignedRole{ RoleId=StandardRoles.CertificateConsumer.Id, PrincipleId = "devops_user_01" },
                        new ResourceAssignedRole{ RoleId=StandardRoles.DomainRequestor.Id, PrincipleId = "devops_user_01" }
                    }
                },
                new ResourceProfile {
                    ResourceType = ResourceTypes.Domain,
                    Identifier = "*.microsoft.com",
                    AssignedRoles= new List<ResourceAssignedRole>{
                        new ResourceAssignedRole{ RoleId=StandardRoles.CertificateConsumer.Id, PrincipleId = "devops_user_01" }
                    }
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
                _ = await access.AddSecurityPrinciple(p, contextUserId, bypassIntegrityCheck: true);
            }

            // assign resource roles per principle
            var allResourceProfiles = GetTestResourceProfiles();
            foreach (var r in allResourceProfiles)
            {
                _ = await access.AddResourceProfile(r, contextUserId, bypassIntegrityCheck: true);
            }

            // assert

            var hasAccess = await access.IsPrincipleInRole("admin_01", StandardRoles.Administrator.Id, contextUserId);
            Assert.IsTrue(hasAccess, "User should be in role");

            hasAccess = await access.IsPrincipleInRole("admin_02", StandardRoles.Administrator.Id, contextUserId);
            Assert.IsFalse(hasAccess, "User should not be in role");

            // check user can consume a cert for a given domain 
            var isAuthorised = await access.IsAuthorised("devops_user_01", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "www.example.com", contextUserId);
            Assert.IsTrue(isAuthorised, "User should be a cert consumer for this domain");

            // check user can't consume a cert for a subdomain they haven't been granted
            isAuthorised = await access.IsAuthorised("devops_user_01", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "secure.example.com", contextUserId);
            Assert.IsFalse(isAuthorised, "User should not be a cert consumer for this domain");

            // check user can consume any subdomain via a granted wildcard
            isAuthorised = await access.IsAuthorised("devops_user_01", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "random.microsoft.com", contextUserId);
            Assert.IsTrue(isAuthorised, "User should be a cert consumer for this subdomain via wildcard");

            // check user can't consume a random wildcard
            isAuthorised = await access.IsAuthorised("devops_user_01", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "*  lkjhasdf98862364", contextUserId);
            Assert.IsFalse(isAuthorised, "User should not be a cert consumer for random wildcard");

            // check user can't consume a random wildcard
            isAuthorised = await access.IsAuthorised("devops_user_01", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "  lkjhasdf98862364.*.microsoft.com", contextUserId);
            Assert.IsFalse(isAuthorised, "User should not be a cert consumer for random wildcard");

            // random user should not be authorised
            isAuthorised = await access.IsAuthorised("randomuser", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "random.microsoft.com", contextUserId);
            Assert.IsFalse(isAuthorised, "Unknown user should not be a cert consumer for this subdomain via wildcard");
        }
    }
}
