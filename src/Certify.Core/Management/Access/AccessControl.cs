using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models.Providers;

namespace Certify.Core.Management.Access
{
    public enum SecurityPrincipleType
    {
        User = 1,
        Application = 2
    }

    public class SecurityPrinciple
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Email { get; set; }
        public string Description { get; set; }

        /// <summary>
        /// If true, user is a mapping to an external AD/LDAP group or user
        /// </summary>
        public bool IsDirectoryMapping { get; set; }

        public List<string> SystemRoleIds { get; set; }

        public SecurityPrincipleType PrincipleType { get; set; }

        public string AuthKey { get; set; }
    }

    public class StandardRoles
    {
        public static Role Administrator { get; } = new Role("sysadmin", "Administrator", "Certify Server Administrator");
        public static Role DomainOwner { get; } = new Role("domain_owner", "Domain Owner", "Controls certificate access for a given domain");
        public static Role DomainRequestor { get; } = new Role("subdomain_requestor", "Subdomain Requestor", "Can request new certs for subdomains on a given domain");
        public static Role CertificateConsumer { get; } = new Role("cert_consumer", "Certificate Consumer", "User of a given certificate");

    }

    public class ResourceTypes
    {
        public static string System { get; } = "system";
        public static string Domain { get; } = "domain";
    }

    public class Role
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }

        public Role() { }
        public Role(string id, string title, string description)
        {
            Id = id;
            Title = title;
            Description = description;
        }
    }

    public class ResourceAssignedRole
    {
        public string PrincipleId { get; set; }
        public string RoleId { get; set; }
    }
    /// <summary>
    /// Define a domain or resource and who the controlling users are
    /// </summary>
    public class ResourceProfile
    {
        public string Id { get; set; } = new Guid().ToString();
        public string ResourceType { get; set; }
        public string Identifier { get; set; }
        public List<ResourceAssignedRole> AssignedRoles { get; set; }
        // public List<Certify.Models.CertRequestChallengeConfig> DefaultChallenges { get; set; }
    }

    public interface IObjectStore
    {
        Task<bool> Save<T>(string id, object item);
        Task<T> Load<T>(string id);
    }

    public class AccessControl
    {
        IObjectStore _store;
        ILog _log;

        public AccessControl(ILog log, IObjectStore store)
        {
            _store = store;
            _log = log;
        }

        public async Task<List<Role>> GetSystemRoles()
        {

            return await Task.FromResult(new List<Role>
            {
                StandardRoles.Administrator,
                StandardRoles.DomainOwner,
                StandardRoles.CertificateConsumer
            });
        }

        public async Task<List<SecurityPrinciple>> GetSecurityPrinciples()
        {
            return await _store.Load<List<SecurityPrinciple>>("principles");
        }

        public async Task<bool> AddSecurityPrinciple(SecurityPrinciple principle, string contextUserId, bool bypassIntegrityCheck = false)
        {
            if (!await IsPrincipleInRole(contextUserId, StandardRoles.Administrator.Id, contextUserId) && !bypassIntegrityCheck)
            {
                _log?.Warning($"User {contextUserId} attempted to use AddSecurityPrinciple [{principle?.Id}] without being in required role.");
                return false;
            }

            var principles = await GetSecurityPrinciples();
            principles.Add(principle);
            await _store.Save<List<SecurityPrinciple>>("principles", principles);

            _log?.Information($"User {contextUserId} added security principle [{principle.Id}] {principle.Username}");
            return true;
        }

        public async Task<bool> UpdateSecurityPrinciple(SecurityPrinciple principle, string contextUserId)
        {

            if (!await IsPrincipleInRole(contextUserId, StandardRoles.Administrator.Id, contextUserId))
            {
                _log?.Warning($"User {contextUserId} attempted to use UpdateSecurityPrinciple [{principle?.Id}] without being in required role.");
                return false;
            }

            var principles = await GetSecurityPrinciples();

            var existing = principles.Find(p => p.Id == principle.Id);
            if (existing != null)
            {
                principles.Remove(existing);
            }

            principles.Add(principle);
            await _store.Save<List<SecurityPrinciple>>("principles", principles);

            _log?.Information($"User {contextUserId} updated security principle [{principle.Id}] {principle.Username}");
            return true;
        }

        /// <summary>
        /// delete a single security principle
        /// </summary>
        /// <param name="id"></param>
        /// <param name="contextUserId"></param>
        /// <returns></returns>
        public async Task<bool> DeleteSecurityPrinciple(string id, string contextUserId)
        {
            if (!await IsPrincipleInRole(contextUserId, StandardRoles.Administrator.Id, contextUserId))
            {
                _log?.Warning($"User {contextUserId} attempted to use DeleteSecurityPrinciple [{id}] without being in required role.");
                return false;
            }

            if (id == contextUserId)
            {
                _log?.Information($"User {contextUserId} tried to delete themselves.");
                return false;
            }

            var principles = await GetSecurityPrinciples();

            var existing = principles.Find(p => p.Id == id);
            if (existing != null)
            {
                principles.Remove(existing);
            }

            await _store.Save<List<SecurityPrinciple>>("principles", principles);

            // TODO: remove assigned roles within all resource profiles

            var allResourceProfiles = await GetResourceProfiles(id, contextUserId);
            foreach (var r in allResourceProfiles)
            {
                if (r.AssignedRoles.Any(ro => ro.PrincipleId == id))
                {
                    var newAssignedRoles = r.AssignedRoles.Where(ra => ra.PrincipleId != id).ToList();
                    r.AssignedRoles = newAssignedRoles;
                }
            }
            await _store.Save<List<SecurityPrinciple>>("resourceprofiles", allResourceProfiles);

            _log?.Information($"User {contextUserId} deleted security principle [{id}] {existing?.Username}");

            return true;
        }

        public async Task<bool> IsAuthorised(string principleId, string roleId, string resourceType, string identifier, string contextUserId)
        {
            var resourceProfiles = await GetResourceProfiles(principleId, contextUserId);

            if (resourceProfiles.Any(r => r.ResourceType == resourceType && r.Identifier == identifier && r.AssignedRoles.Any(a => a.PrincipleId == principleId && a.RoleId == roleId)))
            {
                // principle has an exactly matching role granted for this resource
                return true;
            }

            if (resourceType == ResourceTypes.Domain && !identifier.Trim().StartsWith("*") && identifier.Contains("."))
            {
                // get wildcard for respective domain identifier
                var identifierComponents = identifier.Split('.');

                var wildcard = "*." + string.Join(".", identifierComponents.Skip(1));

                if (resourceProfiles.Any(r => r.ResourceType == resourceType && r.Identifier == wildcard && r.AssignedRoles.Any(a => a.PrincipleId == principleId && a.RoleId == roleId)))
                {
                    // principle has an matching role granted for this resource as a wildcard
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check security principle is in a given role at the system level
        /// </summary>
        /// <param name="id"></param>
        /// <param name="roleId"></param>
        /// <param name="contextUserId"></param>
        /// <returns></returns>
        public async Task<bool> IsPrincipleInRole(string id, string roleId, string contextUserId)
        {
            var resourceProfiles = await GetResourceProfiles(id, contextUserId);

            if (resourceProfiles.Any(r => r.ResourceType == ResourceTypes.System && r.AssignedRoles.Any(a => a.PrincipleId == id && a.RoleId == roleId)))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// return list of resources this user has some access to
        /// </summary>
        /// <param name="contextUserId"></param>
        /// <returns></returns>
        public async Task<List<ResourceProfile>> GetResourceProfiles(string userId, string contextUserId)
        {
            var allResourceProfiles = await _store.Load<List<ResourceProfile>>("resourceprofiles");

            if (userId != null)
            {
                var filteredprofiles = allResourceProfiles.Where(r => r.AssignedRoles.Any(ra => ra.PrincipleId == userId));

                foreach (var f in filteredprofiles)
                {
                    f.AssignedRoles = f.AssignedRoles.Where(a => a.PrincipleId == userId).ToList();
                }

                return filteredprofiles.ToList();
            }
            else
            {
                return allResourceProfiles;
            }
        }

        public async Task<bool> AddResourceProfile(ResourceProfile resourceProfile, string contextUserId, bool bypassIntegrityCheck = false)
        {
            if (!await IsPrincipleInRole(contextUserId, StandardRoles.Administrator.Id, contextUserId) && !bypassIntegrityCheck)
            {
                _log?.Warning($"User {contextUserId} attempted to use AddResourceProfile [{resourceProfile.Identifier}] without being in required role.");
                return false;
            }

            var profiles = await GetResourceProfiles(null, contextUserId);
            profiles.Add(resourceProfile);
            await _store.Save<List<SecurityPrinciple>>("resourceprofiles", profiles);

            _log?.Information($"User {contextUserId} added resource profile [{resourceProfile.Identifier}]");
            return true;
        }
    }
}
