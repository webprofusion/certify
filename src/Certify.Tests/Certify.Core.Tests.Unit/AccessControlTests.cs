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
        private ConcurrentDictionary<string, object> _store = new ConcurrentDictionary<string, object>();

        public Task<T> Load<T>(string id)
        {
            if (_store.TryGetValue(id, out var value))
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

        private SecurityPrinciple GetTestAdminSecurityPrinciple()
        {
            return new SecurityPrinciple
            {
                Id = "admin_01",
                Username = "admin",
                Description = "Administrator account",
                Email = "info@test.com",
                Password = "ABCDEFG",
                PrincipleType = SecurityPrincipleType.User,
                SystemRoleIds = new List<string>{ StandardRoles.Administrator.Id }
            };
        }

        private SecurityPrinciple GetTestDomainOwnerSecurityPrinciple()
        {
            return new SecurityPrinciple
            {
                Id = "domain_owner_01",
                Username = "demo_owner",
                Description = "Example domain owner",
                Email = "domains@test.com",
                Password = "ABCDEFG",
                PrincipleType = SecurityPrincipleType.User,
                SystemRoleIds = new List<string> { StandardRoles.DomainOwner.Id }
            };
        }

        private SecurityPrinciple GetTestDevopsUserSecurityPrinciple()
        {
            return new SecurityPrinciple
            {
                Id = "devops_user_01",
                Username = "devops_01",
                Description = "Example devops user",
                Email = "devops01@test.com",
                Password = "ABCDEFG",
                PrincipleType = SecurityPrincipleType.User,
                SystemRoleIds = new List<string> { StandardRoles.CertificateConsumer.Id, StandardRoles.DomainRequestor.Id }
            };
        }

        private SecurityPrinciple GetTestAppDomainConsumerSecurityPrinciple()
        {
            return new SecurityPrinciple
            {
                Id = "devops_app_01",
                Username = "devapp_01",
                Description = "Example devops app domain consumer",
                Email = "dev_app01@test.com",
                Password = "ABCDEFG",
                PrincipleType = SecurityPrincipleType.User,
                SystemRoleIds = new List<string> { StandardRoles.CertificateConsumer.Id }
            };
        }

        public ResourceProfile GetSystemResourceProfile()
        {
            return new ResourceProfile
            {
                ResourceType = ResourceTypes.System,
                AssignedRoles = new List<ResourceAssignedRole>{
                    new ResourceAssignedRole{ RoleId=StandardRoles.Administrator.Id, PrincipleId = "admin_01" },
                    new ResourceAssignedRole{ RoleId=StandardRoles.CertificateConsumer.Id, PrincipleId = "devops_user_01" },
                    new ResourceAssignedRole{ RoleId=StandardRoles.DomainRequestor.Id, PrincipleId = "devops_user_01" }
                }
            };
        }

        public ResourceProfile GetDomainResourceProfile()
        {
            return new ResourceProfile
            {
                ResourceType = ResourceTypes.Domain,
                Identifier = "example.com",
                AssignedRoles = new List<ResourceAssignedRole>{
                    new ResourceAssignedRole{ RoleId=StandardRoles.CertificateConsumer.Id, PrincipleId = "devops_user_01" },
                    new ResourceAssignedRole{ RoleId=StandardRoles.DomainRequestor.Id, PrincipleId = "devops_user_01" }
                }
            };
        }

        public ResourceProfile GetLongDomainResourceProfile()
        {
            return new ResourceProfile
            {
                ResourceType = ResourceTypes.Domain,
                Identifier = "www.example.com",
                AssignedRoles = new List<ResourceAssignedRole>{
                    new ResourceAssignedRole{ RoleId=StandardRoles.CertificateConsumer.Id, PrincipleId = "devops_user_01" },
                    new ResourceAssignedRole{ RoleId=StandardRoles.DomainRequestor.Id, PrincipleId = "devops_user_01" }
                }
            };
        }

        public ResourceProfile GetWildcardDomainResourceProfile()
        {
            return new ResourceProfile
            {
                ResourceType = ResourceTypes.Domain,
                Identifier = "*.microsoft.com",
                AssignedRoles = new List<ResourceAssignedRole>{
                    new ResourceAssignedRole{ RoleId=StandardRoles.CertificateConsumer.Id, PrincipleId = "devops_user_01" }
                }
            };
        }

        [TestMethod]
        public async Task TestAccessControlAdminIsPrincipleRole()
        {
            var log = new LoggerConfiguration()
                   .WriteTo.Debug()
                   .CreateLogger();
            var loggy = new Loggy(log);
            var access = new AccessControl(loggy, new MemoryObjectStore());
            var contextUserId = "[test]";

            // Add test security principle
            var adminPrinciple = this.GetTestAdminSecurityPrinciple();
            _ = await access.AddSecurityPrinciple(adminPrinciple, contextUserId, bypassIntegrityCheck: true);

            // Assign resource role per principle
            var systemResourceProfile = GetSystemResourceProfile();
            _ = await access.AddResourceProfile(systemResourceProfile, contextUserId, bypassIntegrityCheck: true);

            // Validate specified admin user is a principle role
            var hasAccess = await access.IsPrincipleInRole("admin_01", StandardRoles.Administrator.Id, contextUserId);
            Assert.IsTrue(hasAccess, "User should be in role");

            // Validate specified admin user is not a principle role
            hasAccess = await access.IsPrincipleInRole("admin_02", StandardRoles.Administrator.Id, contextUserId);
            Assert.IsFalse(hasAccess, "User should not be in role");
        }

        [TestMethod]
        public async Task TestAccessControlCertificateConsumerDomain()
        {
            var log = new LoggerConfiguration()
                   .WriteTo.Debug()
                   .CreateLogger();
            var loggy = new Loggy(log);
            var access = new AccessControl(loggy, new MemoryObjectStore());
            var contextUserId = "[test]";

            // Add test security principle
            var devopsPrinciple = this.GetTestDevopsUserSecurityPrinciple();
            _ = await access.AddSecurityPrinciple(devopsPrinciple, contextUserId, bypassIntegrityCheck: true);

            // Assign resource role per principle
            var domainResourceProfile = GetLongDomainResourceProfile();
            _ = await access.AddResourceProfile(domainResourceProfile, contextUserId, bypassIntegrityCheck: true);

            // Validate user can consume a cert for a given domain 
            var isAuthorised = await access.IsAuthorised("devops_user_01", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "www.example.com", contextUserId);
            Assert.IsTrue(isAuthorised, "User should be a cert consumer for this domain");

            // Validate user can't consume a cert for a subdomain they haven't been granted
            isAuthorised = await access.IsAuthorised("devops_user_01", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "secure.example.com", contextUserId);
            Assert.IsFalse(isAuthorised, "User should not be a cert consumer for this domain");
        }

        [TestMethod]
        public async Task TestAccessControlCertificateConsumerWildcardDomain()
        {
            var log = new LoggerConfiguration()
                   .WriteTo.Debug()
                   .CreateLogger();
            var loggy = new Loggy(log);
            var access = new AccessControl(loggy, new MemoryObjectStore());
            var contextUserId = "[test]";

            // Add test security principle
            var devopsPrinciple = this.GetTestDevopsUserSecurityPrinciple();
            _ = await access.AddSecurityPrinciple(devopsPrinciple, contextUserId, bypassIntegrityCheck: true);

            // Assign resource role per principle
            var wildcardResourceProfile = GetWildcardDomainResourceProfile();
            _ = await access.AddResourceProfile(wildcardResourceProfile, contextUserId, bypassIntegrityCheck: true);

            // Validate user can consume any subdomain via a granted wildcard
            var isAuthorised = await access.IsAuthorised("devops_user_01", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "random.microsoft.com", contextUserId);
            Assert.IsTrue(isAuthorised, "User should be a cert consumer for this subdomain via wildcard");

            // Validate user can't consume a random wildcard
            isAuthorised = await access.IsAuthorised("devops_user_01", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "*  lkjhasdf98862364", contextUserId);
            Assert.IsFalse(isAuthorised, "User should not be a cert consumer for random wildcard");

            // Validate user can't consume a random wildcard
            isAuthorised = await access.IsAuthorised("devops_user_01", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "  lkjhasdf98862364.*.microsoft.com", contextUserId);
            Assert.IsFalse(isAuthorised, "User should not be a cert consumer for random wildcard");
        }

        [TestMethod]
        public async Task TestAccessControlRandomUser()
        {
            var log = new LoggerConfiguration()
                   .WriteTo.Debug()
                   .CreateLogger();
            var loggy = new Loggy(log);
            var access = new AccessControl(loggy, new MemoryObjectStore());
            var contextUserId = "[test]";

            // Add test security principle
            var adminPrinciple = this.GetTestAdminSecurityPrinciple();
            _ = await access.AddSecurityPrinciple(adminPrinciple, contextUserId, bypassIntegrityCheck: true);

            // Assign resource role per principle
            var wildcardResourceProfile = GetWildcardDomainResourceProfile();
            _ = await access.AddResourceProfile(wildcardResourceProfile, contextUserId, bypassIntegrityCheck: true);

            // Validate that random user should not be authorised
            var isAuthorised = await access.IsAuthorised("randomuser", StandardRoles.CertificateConsumer.Id, ResourceTypes.Domain, "random.microsoft.com", contextUserId);
            Assert.IsFalse(isAuthorised, "Unknown user should not be a cert consumer for this subdomain via wildcard");
        }

        [TestMethod]
        public async Task TestAccessControlUpdateSecurityPrinciple()
        {
            var log = new LoggerConfiguration()
                   .WriteTo.Debug()
                   .CreateLogger();
            var loggy = new Loggy(log);
            var access = new AccessControl(loggy, new MemoryObjectStore());
            var contextUserId = "[test]";

            // Add test security principle
            var adminPrinciple = this.GetTestAdminSecurityPrinciple();
            _ = await access.AddSecurityPrinciple(adminPrinciple, contextUserId, bypassIntegrityCheck: true);

            // Assign resource role per principle
            var systemResourceProfile = GetSystemResourceProfile();
            _ = await access.AddResourceProfile(systemResourceProfile, contextUserId, bypassIntegrityCheck: true);

            // Validate stored security principle email before update
            var storedPrinciple = (await access.GetSecurityPrinciples()).Find(p => p.Id == adminPrinciple.Id);
            Assert.AreEqual(storedPrinciple.Email, adminPrinciple.Email, $"Stored security principle email address should be {adminPrinciple.Email}");

            // Update stored security principle
            adminPrinciple.Email = "admin@test.com";
            _ = await access.UpdateSecurityPrinciple(adminPrinciple, contextUserId);
            storedPrinciple = (await access.GetSecurityPrinciples()).Find(p => p.Id == adminPrinciple.Id);

            // Validate stored security principle email after update
            Assert.AreEqual(storedPrinciple.Email, adminPrinciple.Email, $"Stored security principle email address should be {adminPrinciple.Email}");
        }

        [TestMethod]
        public async Task TestAccessControlDeleteSecurityPrinciple()
        {
            var log = new LoggerConfiguration()
                   .WriteTo.Debug()
                   .CreateLogger();
            var loggy = new Loggy(log);
            var access = new AccessControl(loggy, new MemoryObjectStore());
            var contextUserId = "[test]";

            // Add test security principle
            var adminPrinciple = this.GetTestAdminSecurityPrinciple();
            _ = await access.AddSecurityPrinciple(adminPrinciple, contextUserId, bypassIntegrityCheck: true);

            // Assign resource role per principle
            var systemResourceProfile = GetSystemResourceProfile();
            _ = await access.AddResourceProfile(systemResourceProfile, contextUserId, bypassIntegrityCheck: true);

            // Validate stored security principle exists before delete
            var storedPrinciple = (await access.GetSecurityPrinciples()).Find(p => p.Id == adminPrinciple.Id);
            Assert.IsNotNull(storedPrinciple, $"Stored security principle {adminPrinciple.Id} should exist");

            // Delete stored security principle
            _ = await access.DeleteSecurityPrinciple(adminPrinciple.Id, contextUserId);
            storedPrinciple = (await access.GetSecurityPrinciples()).Find(p => p.Id == adminPrinciple.Id);

            // Validate stored security principle does not exist after delete
            Assert.IsNull(storedPrinciple, $"Stored security principle {adminPrinciple.Id} should be deleted");
        }
    }
}
