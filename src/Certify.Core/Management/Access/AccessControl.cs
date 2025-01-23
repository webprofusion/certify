using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Models.Providers;
using Certify.Providers;

namespace Certify.Core.Management.Access
{

    public class AccessControl : IAccessControl
    {
        private IConfigurationStore _store;
        private ILog _log;

        public AccessControl(ILog log, IConfigurationStore store)
        {
            _store = store;
            _log = log;
        }

        public async Task AuditWarning(string template, params object[] propertyvalues)
        {
            _log?.Warning(template, propertyvalues);
        }

        public async Task AuditError(string template, params object[] propertyvalues)
        {
            _log?.Error(template, propertyvalues);
        }

        public async Task AuditInformation(string template, params object[] propertyvalues)
        {
            _log?.Information(template, propertyvalues);
        }

        /// <summary>
        /// Check if the system has been initialized with a security principle
        /// </summary>
        /// <returns></returns>
        public async Task<bool> IsInitialized()
        {
            var list = await GetSecurityPrinciples("system");
            if (list.Count != 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public async Task<List<Role>> GetRoles(string contextUserId)
        {
            return await _store.GetItems<Role>(nameof(Role));
        }

        public async Task<List<SecurityPrinciple>> GetSecurityPrinciples(string contextUserId)
        {
            return await _store.GetItems<SecurityPrinciple>(nameof(SecurityPrinciple));
        }

        public async Task<bool> AddSecurityPrinciple(string contextUserId, SecurityPrinciple principle, bool bypassIntegrityCheck = false)
        {
            if (!bypassIntegrityCheck && !await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to use AddSecurityPrinciple [{principleId}] without being in required role.", contextUserId, principle?.Id);
                return false;
            }

            var existing = await GetSecurityPrinciple(contextUserId, principle.Id);
            if (existing != null)
            {
                await AuditWarning("User {contextUserId} attempted to use AddSecurityPrinciple [{principleId}] which already exists.", contextUserId, principle?.Id);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(principle.Password))
            {
                principle.Password = HashPassword(principle.Password);
            }
            else
            {
                principle.Password = HashPassword(Guid.NewGuid().ToString());
            }

            principle.AvatarUrl = GetAvatarUrlForPrinciple(principle);

            await _store.Add<SecurityPrinciple>(nameof(SecurityPrinciple), principle);

            await AuditInformation("User {contextUserId} added security principle [{principleId}] {username}", contextUserId, principle?.Id, principle?.Username);
            return true;
        }

        public string GetAvatarUrlForPrinciple(SecurityPrinciple principle)
        {
            return string.IsNullOrWhiteSpace(principle.Email) ? "https://gravatar.com/avatar/00000000000000000000000000000000" : $"https://gravatar.com/avatar/{GetSHA256Hash(principle.Email.Trim().ToLower())}";
        }

        public async Task<bool> UpdateSecurityPrinciple(string contextUserId, SecurityPrinciple principle)
        {

            if (!await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to use UpdateSecurityPrinciple [{principleId}] without being in required role.", contextUserId, principle?.Id);
                return false;
            }

            try
            {
                var updateSp = await _store.Get<SecurityPrinciple>(nameof(SecurityPrinciple), principle.Id);
                updateSp.Email = principle.Email;
                updateSp.Description = principle.Description;
                updateSp.Title = principle.Title;

                updateSp.AvatarUrl = GetAvatarUrlForPrinciple(principle);

                await _store.Update<SecurityPrinciple>(nameof(SecurityPrinciple), updateSp);
            }
            catch
            {
                await AuditWarning("User {contextUserId} attempted to use UpdateSecurityPrinciple [{principleId}], but was not successful", contextUserId, principle?.Id);
                return false;
            }

            await AuditInformation("User {contextUserId} updated security principle [{principleId}] {principleUsername}", contextUserId, principle?.Id, principle?.Username);
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
                await AuditWarning("User {contextUserId} attempted to use DeleteSecurityPrinciple [{id}] without being in required role.", contextUserId, id);
                return false;
            }

            if (!allowSelfDelete && id == contextUserId)
            {
                await AuditWarning("User {contextUserId} tried to delete themselves.", contextUserId);
                return false;
            }

            var existing = await GetSecurityPrinciple(contextUserId, id);

            var deleted = await _store.Delete<SecurityPrinciple>(nameof(SecurityPrinciple), id);

            if (deleted != true)
            {
                await AuditWarning("User {contextUserId} attempted to delete security principle [{id}] {existingUsername}, but was not successful", contextUserId, id, existing?.Username);
                return false;
            }

            var assignedRoles = await GetAssignedRoles(contextUserId, id);
            foreach (var a in assignedRoles)
            {
                await _store.Delete<AssignedRole>(nameof(AssignedRole), a.Id);
            }

            await AuditInformation("User {contextUserId} deleted security principle [{id}] {existingUsername}", contextUserId, id, existing?.Username);

            return true;
        }

        public async Task<SecurityPrinciple> GetSecurityPrinciple(string contextUserId, string id)
        {
            try
            {
                return await _store.Get<SecurityPrinciple>(nameof(SecurityPrinciple), id);
            }
            catch (Exception exp)
            {
                await AuditError("User {contextUserId} attempted to retrieve security principle [{id}] but was not successful : {exp}", contextUserId, id, exp);

                return default;
            }
        }

        public async Task<SecurityPrinciple> GetSecurityPrincipleByUsername(string contextUserId, string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return default;
            }

            var list = await GetSecurityPrinciples(contextUserId);

            return list?.SingleOrDefault(sp => sp.Username?.ToLowerInvariant() == username.ToLowerInvariant());
        }

        /// <summary>
        /// Check if a security principle has access to the given resource action
        /// </summary>
        /// <param name="contextUserId">Security principle performing access check</param>
        /// <param name="principleId">Security principle to check access for</param>
        /// <param name="resourceType">resource type being accessed</param>
        /// <param name="actionId">resource action required</param>
        /// <param name="identifier">optional resource identifier, if access is limited by specific resource</param>
        /// <param name="scopedAssignedRoles">optional scoped assigned roles to limit access to (for scoped access token checks etc)</param>
        /// <returns></returns>
        public async Task<bool> IsSecurityPrincipleAuthorised(string contextUserId, AccessCheck check)
        {
            // to determine is a principle has access to perform a particular action
            // for each group the principle is part of

            // TODO: cache results for performance based on last update of access control config, which will be largely static

            // get all assigned roles (all users)
            var allAssignedRoles = await _store.GetItems<AssignedRole>(nameof(AssignedRole));

            // get all defined roles
            var allRoles = await _store.GetItems<Role>(nameof(Role));

            // get all defined policies
            var allPolicies = await _store.GetItems<ResourcePolicy>(nameof(ResourcePolicy));

            // get the assigned roles for this specific security principle
            var spAssignedRoles = allAssignedRoles.Where(a => a.SecurityPrincipleId == check.SecurityPrincipleId);

            // if scoped assigned role ID specified (access token check etc), reduce scope of assigned roles to check
            if (check.ScopedAssignedRoles?.Any() == true)
            {
                spAssignedRoles = spAssignedRoles.Where(a => check.ScopedAssignedRoles.Contains(a.Id));
            }

            // get all role definitions included in the principles assigned roles 
            var spAssignedRoleDefinitions = allRoles.Where(r => spAssignedRoles.Any(t => t.RoleId == r.Id));

            var spSpecificAssignedRoles = spAssignedRoles.Where(a => spAssignedRoleDefinitions.Any(r => r.Id == a.RoleId));

            // get all resource policies included in the principles assigned roles
            var spAssignedPolicies = allPolicies.Where(r => spAssignedRoleDefinitions.Any(p => p.Policies.Contains(r.Id)));

            // check an assigned policy allows the required resource action
            if (spAssignedPolicies.Any(a => a.ResourceActions.Contains(check.ResourceActionId)))
            {

                // if any of the service principles assigned roles are restricted by resource type,
                // check for identifier matches (e.g. role assignment restricted on domains )

                if (spSpecificAssignedRoles.Any(a => a.IncludedResources?.Any(r => r.ResourceType == check.ResourceType) == true))
                {
                    var allIncludedResources = spSpecificAssignedRoles.SelectMany(a => a.IncludedResources).Distinct();

                    if (check.ResourceType == ResourceTypes.Domain && !check.Identifier.Trim().StartsWith("*") && check.Identifier.Contains("."))
                    {
                        // get wildcard for respective domain identifier
                        var identifierComponents = check.Identifier.Split('.');

                        var wildcard = "*." + string.Join(".", identifierComponents.Skip(1));

                        // search for matching identifier

                        foreach (var includedResource in allIncludedResources)
                        {
                            if (includedResource.ResourceType == check.ResourceType && includedResource.Identifier == wildcard)
                            {
                                return true;
                            }
                            else if (includedResource.ResourceType == check.ResourceType && includedResource.Identifier == check.Identifier)
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

        public async Task<ActionResult> IsAccessTokenAuthorised(string contextUserId, AccessToken accessToken, AccessCheck check)
        {
            // resolve security principle from access token

            var assignedTokens = await _store.GetItems<AssignedAccessToken>(nameof(AssignedAccessToken));

            // check if a non-expired/non-revoked access token exists matching the given client ID
            var knownAssignedToken = assignedTokens.SingleOrDefault(t => t.AccessTokens.Any(a => a.ClientId == accessToken.ClientId && a.Secret == accessToken.Secret && a.DateRevoked == null && (a.DateExpiry == null || a.DateExpiry >= DateTimeOffset.UtcNow)));

            if (knownAssignedToken == null)
            {
                return new ActionResult("Access token unknown, expired or revoked.", false);
            }

            // check related principle has access

            var scopedCheck = new AccessCheck
            {
                SecurityPrincipleId = knownAssignedToken.SecurityPrincipleId,
                ResourceActionId = check.ResourceActionId,
                Identifier = check.Identifier,
                ResourceType = check.ResourceType,
                ScopedAssignedRoles = knownAssignedToken.ScopedAssignedRoles
            };

            var isAuthorised = await IsSecurityPrincipleAuthorised(contextUserId, scopedCheck);

            if (isAuthorised)
            {
                // TODO: check token scope restrictions

                return new ActionResult("OK", true);
            }
            else
            {
                return new ActionResult("Access token not authorized or invalid for action, resource or identifier", false);
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
                await AuditWarning("User {contextUserId} attempted to use AddResourcePolicy [{resourceProfileId}] without being in required role.", contextUserId, resourceProfile?.Id);
                return false;
            }

            await _store.Add(nameof(ResourcePolicy), resourceProfile);

            await AuditInformation("User {contextUserId} added resource policy [{resourceProfile.Id}]", contextUserId, resourceProfile?.Id);
            return true;
        }

        public async Task<bool> UpdateSecurityPrinciplePassword(string contextUserId, SecurityPrinciplePasswordUpdate passwordUpdate)
        {
            if (passwordUpdate.SecurityPrincipleId != contextUserId && !await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to use updated password for [{id}] without being in required role.", contextUserId, passwordUpdate.SecurityPrincipleId);
                return false;
            }

            var updated = false;

            var principle = await GetSecurityPrinciple(contextUserId, passwordUpdate.SecurityPrincipleId);

            if (IsPasswordValid(passwordUpdate.Password, principle.Password))
            {
                try
                {
                    var updateSp = await _store.Get<SecurityPrinciple>(nameof(SecurityPrinciple), principle.Id);
                    updateSp.Password = HashPassword(passwordUpdate.NewPassword);
                    await _store.Update<SecurityPrinciple>(nameof(SecurityPrinciple), updateSp);
                    updated = true;
                }
                catch
                {
                    await AuditWarning("User {contextUserId} attempted to use UpdateSecurityPrinciple password [{principleId}], but was not successful", contextUserId, principle?.Id);
                    return false;
                }
            }
            else
            {
                await AuditInformation("Previous password did not match while updating security principle password", contextUserId, principle.Username, principle.Id);
            }

            if (updated)
            {
                await AuditInformation("User {contextUserId} updated password for [{username} - {id}]", contextUserId, principle.Username, principle.Id);
            }
            else
            {

                await AuditWarning("User {contextUserId} failed to update password for [{username} - {id}]", contextUserId, principle.Username, principle.Id);
            }

            return updated;
        }

        public bool IsPasswordValid(string password, string currentHash)
        {
            if (string.IsNullOrWhiteSpace(currentHash) && string.IsNullOrWhiteSpace(password))
            {
                return true;
            }

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

        public async Task<bool> AddRole(string contextUserId, Role r, bool bypassIntegrityCheck = false)
        {
            if (!bypassIntegrityCheck && !await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to add an role action without being in required role.", contextUserId);
                return false;
            }

            await _store.Add(nameof(Role), r);
            return true;
        }

        public async Task<bool> AddAssignedRole(string contextUserId, AssignedRole r, bool bypassIntegrityCheck = false)
        {
            if (!bypassIntegrityCheck && !await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to add an assigned role without being in required role.", contextUserId);
                return false;
            }

            await _store.Add(nameof(AssignedRole), r);
            return true;
        }

        public async Task<bool> AddResourceAction(string contextUserId, ResourceAction action, bool bypassIntegrityCheck = false)
        {
            if (!bypassIntegrityCheck && !await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to add a resource action without being in required role.", contextUserId);
                return false;
            }

            await _store.Add(nameof(ResourceAction), action);
            return true;
        }

        public async Task<List<AssignedRole>> GetAssignedRoles(string contextUserId, string id)
        {
            if (id != contextUserId && !await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to read assigned role for [{id}] without being in required role.", contextUserId, id);
                return null;
            }

            var assignedRoles = await _store.GetItems<AssignedRole>(nameof(AssignedRole));

            return assignedRoles.Where(r => r.SecurityPrincipleId == id).ToList();
        }

        public async Task<RoleStatus> GetSecurityPrincipleRoleStatus(string contextUserId, string id)
        {
            if (id != contextUserId && !await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to read role status role for [{id}] without being in required role.", contextUserId, id);
                return null;
            }

            var allAssignedRoles = await _store.GetItems<AssignedRole>(nameof(AssignedRole));
            var allRoles = await _store.GetItems<Role>(nameof(Role));
            var allPolicies = await _store.GetItems<ResourcePolicy>(nameof(ResourcePolicy));
            var allActions = await _store.GetItems<ResourceAction>(nameof(ResourceAction));

            var spAssignedRoles = allAssignedRoles.Where(a => a.SecurityPrincipleId == id);
            var spRoles = allRoles.Where(r => spAssignedRoles.Any(t => t.RoleId == r.Id));
            var spPolicies = allPolicies.Where(r => spRoles.Any(p => p.Policies.Contains(r.Id)));
            var spActions = allActions.Where(r => spPolicies.Any(p => p.ResourceActions.Contains(r.Id)));

            var roleStatus = new RoleStatus
            {
                AssignedRoles = spAssignedRoles,
                Roles = spRoles,
                Policies = spPolicies,
                Action = spActions
            };

            return roleStatus;
        }

        public async Task<bool> UpdateAssignedRoles(string contextUserId, SecurityPrincipleAssignedRoleUpdate update)
        {
            if (!await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to update assigned role for [{id}] without being in required role.", contextUserId, update.SecurityPrincipleId);
                return false;
            }

            // remove items from assigned roles
            var existing = await GetAssignedRoles(contextUserId, update.SecurityPrincipleId);
            foreach (var deleted in update.RemovedAssignedRoles)
            {
                var e = existing.FirstOrDefault(r => r.RoleId == deleted.RoleId);
                if (e != null)
                {
                    await _store.Delete<AssignedRole>(nameof(AssignedRole), e.Id);
                }
            }

            // add items to assigned roles
            existing = await GetAssignedRoles(contextUserId, update.SecurityPrincipleId);
            foreach (var added in update.AddedAssignedRoles)
            {
                if (!existing.Exists(r => r.RoleId == added.RoleId))
                {
                    await _store.Add<AssignedRole>(nameof(AssignedRole), added);
                }
            }

            return true;
        }

        public async Task<SecurityPrincipleCheckResponse> CheckSecurityPrinciplePassword(string contextUserId, SecurityPrinciplePasswordCheck passwordCheck)
        {
            var principle = string.IsNullOrWhiteSpace(passwordCheck.SecurityPrincipleId) ?
                                await GetSecurityPrincipleByUsername(contextUserId, passwordCheck.Username) :
                                await GetSecurityPrinciple(contextUserId, passwordCheck.SecurityPrincipleId);

            if (principle != null && IsPasswordValid(passwordCheck.Password, principle.Password))
            {
                return new SecurityPrincipleCheckResponse { IsSuccess = true, SecurityPrinciple = principle };
            }
            else
            {
                if (principle == null)
                {
                    return new SecurityPrincipleCheckResponse { IsSuccess = false, Message = "Invalid security principle" };
                }
                else
                {
                    return new SecurityPrincipleCheckResponse { IsSuccess = false, Message = "Invalid password" };
                }
            }
        }

        public async Task<List<AssignedAccessToken>> GetAssignedAccessTokens(string contextUserId)
        {
            if (!await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to list assigned access tokens without being in required role.", contextUserId);
                return null;
            }

            return await _store.GetItems<AssignedAccessToken>(nameof(AssignedAccessToken));
        }

        public async Task<bool> AddAssignedAccessToken(string contextUserId, AssignedAccessToken a)
        {
            if (!await IsPrincipleInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to add an assigned access token without being in required role.", contextUserId);
                return false;
            }

            await _store.Add(nameof(AssignedAccessToken), a);

            return true;
        }

        public string GetSHA256Hash(string val)
        {
            using (var sha256Hash = SHA256.Create())
            {
                var data = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(val));
                var sBuilder = new StringBuilder();

                // Loop through each byte of the hashed data
                // and format each one as a hexadecimal string.
                for (var i = 0; i < data.Length; i++)
                {
                    sBuilder.Append(data[i].ToString("x2"));
                }

                // Return the hexadecimal string.
                return sBuilder.ToString();
            }
        }
    }
}
