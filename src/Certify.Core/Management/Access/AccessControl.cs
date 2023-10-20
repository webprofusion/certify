using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Certify.Models.Config.AccessControl;
using Certify.Models.Providers;
using Certify.Providers;

namespace Certify.Core.Management.Access
{

    public class AccessControl : IAccessControl
    {
        private IAccessControlStore _store;
        private ILog _log;

        public AccessControl(ILog log, IAccessControlStore store)
        {
            _store = store;
            _log = log;
        }

        public async Task<List<Role>> GetSystemRoles()
        {
            return await Task.FromResult(new List<Role>
            {
                StandardRoles.Administrator,
                StandardRoles.IdentifierController,
                StandardRoles.CertificateConsumer
            });
        }

        public async Task<List<SecurityPrinciple>> GetSecurityPrinciples(string contextUserId)
        {
            return await _store.GetItems<SecurityPrinciple>(nameof(SecurityPrinciple));
        }

        public async Task<bool> AddSecurityPrinciple(string contextUserId, SecurityPrinciple principle, bool bypassIntegrityCheck = false)
        {
            if (!bypassIntegrityCheck && !await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                _log?.Warning($"User {contextUserId} attempted to use AddSecurityPrinciple [{principle?.Id}] without being in required role.");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(principle.Password))
            {
                principle.Password = HashPassword(principle.Password);
            }

            await _store.Add<SecurityPrinciple>(nameof(SecurityPrinciple), principle);

            _log?.Information($"User {contextUserId} added security principle [{principle?.Id}] {principle?.Username}");
            return true;
        }

        public async Task<bool> UpdateSecurityPrinciple(string contextUserId, SecurityPrinciple principle)
        {

            if (!await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                _log?.Warning($"User {contextUserId} attempted to use UpdateSecurityPrinciple [{principle?.Id}] without being in required role.");
                return false;
            }

            var updated = _store.Update<SecurityPrinciple>(nameof(SecurityPrinciple), principle);

            if (updated.IsCompleted != true)
            {
                _log?.Warning($"User {contextUserId} attempted to use UpdateSecurityPrinciple [{principle?.Id}], but was not successful");
                return false;
            }

            _log?.Information($"User {contextUserId} updated security principle [{principle?.Id}] {principle?.Username}");
            return true;
        }

        /// <summary>
        /// delete a single security principle
        /// </summary>
        /// <param name="contextUserId"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<bool> DeleteSecurityPrinciple(string contextUserId, string id, bool allowSelfDelete = false)
        {
            if (!await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                _log?.Warning($"User {contextUserId} attempted to use DeleteSecurityPrinciple [{id}] without being in required role.");
                return false;
            }

            if (!allowSelfDelete && id == contextUserId)
            {
                _log?.Information($"User {contextUserId} tried to delete themselves.");
                return false;
            }

            var existing = await GetSecurityPrinciple(contextUserId, id);

            var deleted = await _store.Delete<SecurityPrinciple>(nameof(SecurityPrinciple), id);

            if (deleted != true)
            {
                _log?.Warning($"User {contextUserId} attempted to delete security principle [{id}] {existing?.Username}, but was not successful");
                return false;
            }
            // TODO: remove assigned roles

            _log?.Information($"User {contextUserId} deleted security principle [{id}] {existing?.Username}");

            return true;
        }

        public async Task<SecurityPrinciple> GetSecurityPrinciple(string contextUserId, string id)
        {
            return await _store.Get<SecurityPrinciple>(nameof(SecurityPrinciple), id);
        }

        public async Task<bool> IsAuthorised(string contextUserId, string principleId, string roleId, string resourceType, string actionId, string identifier)
        {
            // to determine is a principle has access to perform a particular action
            // for each group the principle is part of

            var allAssignedRoles = await _store.GetItems<AssignedRole>(nameof(AssignedRole));

            var spAssigned = allAssignedRoles.Where(a => a.SecurityPrincipleId == principleId);

            var allRoles = await _store.GetItems<Role>(nameof(Role));

            var spAssignedRoles = allRoles.Where(r => spAssigned.Any(t => t.RoleId == r.Id));

            var spSpecificAssignedRoles = spAssigned.Where(a => spAssignedRoles.Any(r => r.Id == a.RoleId));

            var allPolicies = await _store.GetItems<ResourcePolicy>(nameof(ResourcePolicy));

            var spAssignedPolicies = allPolicies.Where(r => spAssignedRoles.Any(p => p.Policies.Contains(r.Id)));

            if (spAssignedPolicies.Any(a => a.ResourceActions.Contains(actionId)))
            {
                // if any of the service principles assigned roles are restricted by the type of resource type, check for identifier matches (e.g. role assignment restricted on domains )
                if (spSpecificAssignedRoles.Any(a => a.IncludedResources.Any(r => r.ResourceType == resourceType)))
                {
                    var allIncludedResources = spSpecificAssignedRoles.SelectMany(a => a.IncludedResources).Distinct();

                    if (resourceType == ResourceTypes.Domain && !identifier.Trim().StartsWith("*") && identifier.Contains("."))
                    {
                        // get wildcard for respective domain identifier
                        var identifierComponents = identifier.Split('.');

                        var wildcard = "*." + string.Join(".", identifierComponents.Skip(1));

                        // search for matching identifier

                        foreach (var includedResource in allIncludedResources)
                        {
                            if (includedResource.ResourceType == resourceType && includedResource.Identifier == wildcard)
                            {
                                return true;
                            }
                            else if (includedResource.ResourceType == resourceType && includedResource.Identifier == identifier)
                            {
                                return true;
                            }
                        }
                    }

                    // no match
                    return false;
                }
                else
                {
                    return true;
                }
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Check security principle is in a given role at the system level
        /// </summary>
        /// <param name="contextUserId"></param>
        /// <param name="id"></param>
        /// <param name="roleId"></param>
        /// <returns></returns>
        public async Task<bool> IsPrincipleInRole(string contextUserId, string id, string roleId)
        {
            var assignedRoles = await _store.GetItems<AssignedRole>(nameof(AssignedRole));

            if (assignedRoles.Any(a => a.RoleId == roleId && a.SecurityPrincipleId == id))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public async Task<bool> AddResourcePolicy(string contextUserId, ResourcePolicy resourceProfile, bool bypassIntegrityCheck = false)
        {
            if (!bypassIntegrityCheck && !await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                _log?.Warning($"User {contextUserId} attempted to use AddResourcePolicy [{resourceProfile.Id}] without being in required role.");
                return false;
            }

            await _store.Add(nameof(ResourcePolicy), resourceProfile);

            _log?.Information($"User {contextUserId} added resource policy [{resourceProfile.Id}]");
            return true;
        }

        public async Task<bool> UpdateSecurityPrinciplePassword(string contextUserId, string id, string oldpassword, string newpassword)
        {
            if (id != contextUserId && !await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                _log?.Warning("User {contextUserId} attempted to use updated password for [{id}] without being in required role.", contextUserId, id);
                return false;
            }

            var updated = false;

            var principle = await GetSecurityPrinciple(contextUserId, id);

            if (IsPasswordValid(oldpassword, principle.Password))
            {
                principle.Password = HashPassword(newpassword);
                updated = await UpdateSecurityPrinciple(contextUserId, principle);
            }
            else
            {
                _log?.Information("Previous password did not match while updating security principle password", contextUserId, principle.Username, principle.Id);
            }

            if (updated)
            {
                _log?.Information("User {contextUserId} updated password for [{username} - {id}]", contextUserId, principle.Username, principle.Id);
            }
            else
            {

                _log?.Warning("User {contextUserId} failed to update password for [{username} - {id}]", contextUserId, principle.Username, principle.Id);
            }

            return updated;
        }

        public bool IsPasswordValid(string password, string currentHash)
        {
            var components = currentHash.Split('.');

            // hash provided password with same salt to compare result
            return currentHash == HashPassword(password, components[1]);
        }

        /// <summary>
        /// Hash password, optionally using the provided salt or generating new salt
        /// </summary>
        /// <param name="password"></param>
        /// <param name="saltString"></param>
        /// <returns></returns>
        public string HashPassword(string password, string saltString = null)
        {
            var iterations = 600000;
            var salt = new byte[24];

            if (saltString == null)
            {
                RandomNumberGenerator.Create().GetBytes(salt);
            }
            else
            {
                salt = Convert.FromBase64String(saltString);
            }
#if NET8_0_OR_GREATER
            var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA512);
#else
            var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations);
#endif

            var hash = pbkdf2.GetBytes(24);

            var hashed = $"v1.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";

            return hashed;
        }

        public async Task AddRole(Role r)
        {
            await _store.Add(nameof(Role), r);
        }

        public async Task AddAssignedRole(AssignedRole r)
        {
            await _store.Add(nameof(AssignedRole), r);
        }

        public async Task AddAction(ResourceAction action)
        {
            await _store.Add(nameof(ResourceAction), action);
        }

        public async Task<List<AssignedRole>> GetAssignedRoles(string contextUserId, string id)
        {

            if (id != contextUserId && !await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                _log?.Warning("User {contextUserId} attempted to read assigned role for [{id}] without being in required role.", contextUserId, id);
                return new List<AssignedRole>();
            }

            var assignedRoles = await _store.GetItems<AssignedRole>(nameof(AssignedRole));

            return assignedRoles.Where(r => r.SecurityPrincipleId == id).ToList();
        }
    }
}
