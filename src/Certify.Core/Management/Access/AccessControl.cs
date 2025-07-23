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
            await Task.CompletedTask;
        }

        public async Task AuditError(string template, params object[] propertyvalues)
        {
            _log?.Error(template, propertyvalues);
            await Task.CompletedTask;
        }

        public async Task AuditInformation(string template, params object[] propertyvalues)
        {
            _log?.Information(template, propertyvalues);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Check if the system has been initialized with a security principal
        /// </summary>
        /// <returns></returns>
        public async Task<bool> IsInitialized()
        {
            var list = await GetSecurityPrincipals("system");
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

        public async Task<List<SecurityPrincipal>> GetSecurityPrincipals(string contextUserId, bool includePassword = false)
        {
            var results = await _store.GetItems<SecurityPrincipal>(nameof(SecurityPrincipal));

            if (!includePassword)
            {
                // remove password hash from results
                results.ForEach(sp => sp.Password = null);
            }

            return results;
        }

        public async Task<bool> AddSecurityPrincipal(string contextUserId, SecurityPrincipal principal, bool bypassIntegrityCheck = false)
        {
            if (!bypassIntegrityCheck && !await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to use AddSecurityPrincipal [{principalId}] without being in required role.", contextUserId, principal?.Id);
                return false;
            }

            var existing = await GetSecurityPrincipal(contextUserId, principal.Id);
            if (existing != null)
            {
                await AuditWarning("User {contextUserId} attempted to use AddSecurityPrincipal [{principalId}] which already exists.", contextUserId, principal?.Id);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(principal.Password))
            {
                principal.Password = HashPassword(principal.Password);
            }
            else
            {
                principal.Password = HashPassword(Guid.NewGuid().ToString());
            }

            principal.AvatarUrl = GetAvatarUrlForPrincipal(principal);

            await _store.Add<SecurityPrincipal>(nameof(SecurityPrincipal), principal);

            await AuditInformation("User {contextUserId} added security principal [{principalId}] {username}", contextUserId, principal?.Id, principal?.Username);
            return true;
        }

        public string GetAvatarUrlForPrincipal(SecurityPrincipal principal)
        {
            return string.IsNullOrWhiteSpace(principal.Email) ? "https://gravatar.com/avatar/00000000000000000000000000000000" : $"https://gravatar.com/avatar/{GetSHA256Hash(principal.Email.Trim().ToLower())}";
        }

        public async Task<bool> UpdateSecurityPrincipal(string contextUserId, SecurityPrincipal principal)
        {

            if (!await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to use UpdateSecurityPrincipal [{principalId}] without being in required role.", contextUserId, principal?.Id);
                return false;
            }

            try
            {
                var updateSp = await _store.Get<SecurityPrincipal>(nameof(SecurityPrincipal), principal.Id);
                updateSp.Email = principal.Email;
                updateSp.Description = principal.Description;
                updateSp.Title = principal.Title;

                updateSp.AvatarUrl = GetAvatarUrlForPrincipal(principal);

                await _store.Update<SecurityPrincipal>(nameof(SecurityPrincipal), updateSp);
            }
            catch
            {
                await AuditWarning("User {contextUserId} attempted to use UpdateSecurityPrincipal [{principalId}], but was not successful", contextUserId, principal?.Id);
                return false;
            }

            await AuditInformation("User {contextUserId} updated security principal [{principalId}] {principalUsername}", contextUserId, principal?.Id, principal?.Username);
            return true;
        }

        /// <summary>
        /// delete a single security principal
        /// </summary>
        /// <param name="contextUserId"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<bool> DeleteSecurityPrincipal(string contextUserId, string id, bool allowSelfDelete = false)
        {
            if (!await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to use DeleteSecurityPrincipal [{id}] without being in required role.", contextUserId, id);
                return false;
            }

            if (!allowSelfDelete && id == contextUserId)
            {
                await AuditWarning("User {contextUserId} tried to delete themselves.", contextUserId);
                return false;
            }

            var existing = await GetSecurityPrincipal(contextUserId, id);

            if (existing.IsBuiltIn)
            {
                if (!allowSelfDelete && id == contextUserId)
                {
                    await AuditWarning("User {contextUserId} tried to delete built-in user [{id}].", contextUserId, id);
                    return false;
                }
            }

            var deleted = await _store.Delete<SecurityPrincipal>(nameof(SecurityPrincipal), id);

            if (deleted != true)
            {
                await AuditWarning("User {contextUserId} attempted to delete security principal [{id}] {existingUsername}, but was not successful", contextUserId, id, existing?.Username);
                return false;
            }

            var assignedRoles = await GetAssignedRoles(contextUserId, id);
            foreach (var a in assignedRoles)
            {
                await _store.Delete<AssignedRole>(nameof(AssignedRole), a.Id);
            }

            await AuditInformation("User {contextUserId} deleted security principal [{id}] {existingUsername}", contextUserId, id, existing?.Username);

            return true;
        }

        public async Task<SecurityPrincipal> GetSecurityPrincipal(string contextUserId, string id, bool includePassword = false)
        {
            try
            {
                var result = await _store.Get<SecurityPrincipal>(nameof(SecurityPrincipal), id);

                if (!includePassword && result != null)
                {
                    result.Password = null;
                }

                return result;
            }
            catch (Exception exp)
            {
                await AuditError("User {contextUserId} attempted to retrieve security principal [{id}] but was not successful : {exp}", contextUserId, id, exp);

                return default;
            }
        }

        public async Task<SecurityPrincipal> GetSecurityPrincipalByUsername(string contextUserId, string username, bool includePassword = false)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return default;
            }

            var list = await GetSecurityPrincipals(contextUserId, includePassword);

            var result = list?.SingleOrDefault(sp => sp.Username?.ToLowerInvariant() == username.ToLowerInvariant());

            if (!includePassword && result != null)
            {
                result.Password = null;
            }

            return result;
        }

        /// <summary>
        /// Check if a security principal has access to the given resource action
        /// </summary>
        /// <param name="contextUserId">Security principal performing access check</param>
        /// <param name="principalId">Security principal to check access for</param>
        /// <param name="resourceType">resource type being accessed</param>
        /// <param name="actionId">resource action required</param>
        /// <param name="identifier">optional resource identifier, if access is limited by specific resource</param>
        /// <param name="scopedAssignedRoles">optional scoped assigned roles to limit access to (for scoped access token checks etc)</param>
        /// <returns></returns>
        public async Task<bool> IsSecurityPrincipalAuthorised(string contextUserId, AccessCheck check)
        {
            // to determine is a principal has access to perform a particular action
            // for each group the principal is part of

            // TODO: cache results for performance based on last update of access control config, which will be largely static

            // get all assigned roles (all users)
            // assigned roles are assigned to a security principal (user) and include a specified role plus any restrictions on resource types or identifiers
            var allAssignedRoles = await _store.GetItems<AssignedRole>(nameof(AssignedRole));

            // get all defined roles
            var allRoles = await _store.GetItems<Role>(nameof(Role));

            // get all defined policies
            var allPolicies = await _store.GetItems<ResourcePolicy>(nameof(ResourcePolicy));

            // get the assigned roles for this specific security principal
            var spAssignedRoles = allAssignedRoles.Where(a => a.SecurityPrincipalId == check.SecurityPrincipalId);

            // if scoped AssignedRole.ID (not just the roleID) specified (access token check etc), reduce scope of assigned roles to check
            if (check.ScopedAssignedRoles?.Any() == true)
            {
                spAssignedRoles = spAssignedRoles.Where(a => check.ScopedAssignedRoles.Contains(a.Id));
            }

            // get all role definitions included in the principals assigned roles 
            var spAssignedRoleDefinitions = allRoles.Where(r => spAssignedRoles.Any(t => t.RoleId == r.Id));

            var spSpecificAssignedRoles = spAssignedRoles.Where(a => spAssignedRoleDefinitions.Any(r => r.Id == a.RoleId));

            // get all resource policies included in the principals assigned roles
            var spAssignedPolicies = allPolicies.Where(r => spAssignedRoleDefinitions.Any(p => p.Policies.Contains(r.Id)));

            // check an assigned policy allows the required resource action
            if (spAssignedPolicies.Any(a => a.ResourceActions.Contains(check.ResourceActionId)))
            {

                // if any of the service principals assigned roles are restricted by resource type,
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
            // resolve security principal from access token

            var assignedTokens = await _store.GetItems<AssignedAccessToken>(nameof(AssignedAccessToken));

            // check if a non-expired/non-revoked access token exists matching the given client ID
            var knownAssignedToken = assignedTokens.SingleOrDefault(t => t.AccessTokens.Any(a => a.ClientId == accessToken.ClientId && a.Secret == accessToken.Secret && a.DateRevoked == null && (a.DateExpiry == null || a.DateExpiry >= DateTimeOffset.UtcNow)));

            if (knownAssignedToken == null)
            {
                return new ActionResult("Access token unknown, expired or revoked.", false);
            }

            // check related principal has access

            var scopedCheck = new AccessCheck
            {
                SecurityPrincipalId = knownAssignedToken.SecurityPrincipalId,
                ResourceActionId = check.ResourceActionId,
                Identifier = check.Identifier,
                ResourceType = check.ResourceType,
                ScopedAssignedRoles = knownAssignedToken.ScopedAssignedRoles
            };

            var isAuthorised = await IsSecurityPrincipalAuthorised(contextUserId, scopedCheck);

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
        /// Check security principal is in a given role at the system level
        /// </summary>
        /// <param name="contextUserId"></param>
        /// <param name="id"></param>
        /// <param name="roleId"></param>
        /// <returns></returns>
        public async Task<bool> IsPrincipalInRole(string contextUserId, string id, string roleId)
        {
            var assignedRoles = await _store.GetItems<AssignedRole>(nameof(AssignedRole));

            if (assignedRoles.Any(a => a.RoleId == roleId && a.SecurityPrincipalId == id))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public async Task<bool> AddResourcePolicy(string contextUserId, ResourcePolicy resourcePolicy, bool bypassIntegrityCheck = false)
        {
            if (!bypassIntegrityCheck && !await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to use AddResourcePolicy [{resourcePolicyId}] without being in required role.", contextUserId, resourcePolicy?.Id);
                return false;
            }

            await _store.Add(nameof(ResourcePolicy), resourcePolicy);

            await AuditInformation("User {contextUserId} added resource policy [{resourcePolicy.Id}]", contextUserId, resourcePolicy?.Id);
            return true;
        }

        public async Task<bool> UpdateSecurityPrincipalPassword(string contextUserId, SecurityPrincipalPasswordUpdate passwordUpdate, bool requirePasswordConfirmation = true)
        {
            if (passwordUpdate.SecurityPrincipalId != contextUserId && !await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to use updated password for [{id}] without being in required role.", contextUserId, passwordUpdate.SecurityPrincipalId);
                return false;
            }

            var updated = false;

            var principal = await GetSecurityPrincipal(contextUserId, passwordUpdate.SecurityPrincipalId, includePassword: true);

            if (!requirePasswordConfirmation || (requirePasswordConfirmation && IsPasswordValid(passwordUpdate.Password, principal.Password)))
            {
                try
                {
                    var updateSp = await _store.Get<SecurityPrincipal>(nameof(SecurityPrincipal), principal.Id);
                    updateSp.Password = HashPassword(passwordUpdate.NewPassword);
                    await _store.Update<SecurityPrincipal>(nameof(SecurityPrincipal), updateSp);
                    updated = true;
                }
                catch (Exception exp)
                {
                    await AuditError("User {contextUserId} attempted to use UpdateSecurityPrincipal password [{principalId}], but was not successful : {exp}", contextUserId, principal?.Id, exp);
                    updated = false;
                }
            }
            else
            {
                await AuditInformation("Previous password did not match while updating security principal password", contextUserId, principal.Username, principal.Id);
            }

            if (updated)
            {
                await AuditInformation("User {contextUserId} updated password for [{username} - {id}]", contextUserId, principal.Username, principal.Id);
            }
            else
            {

                await AuditWarning("User {contextUserId} failed to update password for [{username} - {id}]", contextUserId, principal.Username, principal.Id);
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
            var hashedPassword = HashPassword(password, components[1]);
            return currentHash == hashedPassword;
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
            if (!bypassIntegrityCheck && !await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to add an role action without being in required role.", contextUserId);
                return false;
            }

            await _store.Add(nameof(Role), r);
            return true;
        }

        public async Task<bool> AddAssignedRole(string contextUserId, AssignedRole r, bool bypassIntegrityCheck = false)
        {
            if (!bypassIntegrityCheck && !await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to add an assigned role without being in required role.", contextUserId);
                return false;
            }

            await _store.Add(nameof(AssignedRole), r);
            return true;
        }

        public async Task<bool> AddResourceAction(string contextUserId, ResourceAction action, bool bypassIntegrityCheck = false)
        {
            if (!bypassIntegrityCheck && !await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to add a resource action without being in required role.", contextUserId);
                return false;
            }

            await _store.Add(nameof(ResourceAction), action);
            return true;
        }

        public async Task<List<AssignedRole>> GetAssignedRoles(string contextUserId, string id)
        {
            if (id != contextUserId && !await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to read assigned role for [{id}] without being in required role.", contextUserId, id);
                return null;
            }

            var assignedRoles = await _store.GetItems<AssignedRole>(nameof(AssignedRole));

            return assignedRoles.Where(r => r.SecurityPrincipalId == id).ToList();
        }

        public async Task<RoleStatus> GetSecurityPrincipalRoleStatus(string contextUserId, string id)
        {
            if (id != contextUserId && !await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to read role status role for [{id}] without being in required role.", contextUserId, id);
                return null;
            }

            var allAssignedRoles = await _store.GetItems<AssignedRole>(nameof(AssignedRole));
            var allRoles = await _store.GetItems<Role>(nameof(Role));
            var allPolicies = await _store.GetItems<ResourcePolicy>(nameof(ResourcePolicy));
            var allActions = await _store.GetItems<ResourceAction>(nameof(ResourceAction));

            var spAssignedRoles = allAssignedRoles.Where(a => a.SecurityPrincipalId == id);
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

        public async Task<bool> UpdateAssignedRoles(string contextUserId, SecurityPrincipalAssignedRoleUpdate update)
        {
            if (!await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to update assigned role for [{id}] without being in required role.", contextUserId, update.SecurityPrincipalId);
                return false;
            }

            // remove items from assigned roles
            var existing = await GetAssignedRoles(contextUserId, update.SecurityPrincipalId);
            foreach (var deleted in update.RemovedAssignedRoles)
            {
                var e = existing.FirstOrDefault(r => r.RoleId == deleted.RoleId);
                if (e != null)
                {
                    await _store.Delete<AssignedRole>(nameof(AssignedRole), e.Id);
                }
            }

            // add items to assigned roles
            existing = await GetAssignedRoles(contextUserId, update.SecurityPrincipalId);
            foreach (var added in update.AddedAssignedRoles)
            {
                if (!existing.Exists(r => r.RoleId == added.RoleId))
                {
                    await _store.Add<AssignedRole>(nameof(AssignedRole), added);
                }
            }

            return true;
        }

        public async Task<SecurityPrincipalCheckResponse> CheckSecurityPrincipalPassword(string contextUserId, SecurityPrincipalPasswordCheck passwordCheck)
        {
            var principal = string.IsNullOrWhiteSpace(passwordCheck.SecurityPrincipalId) ?
                                await GetSecurityPrincipalByUsername(contextUserId, passwordCheck.Username, includePassword: true) :
                                await GetSecurityPrincipal(contextUserId, passwordCheck.SecurityPrincipalId, includePassword: true);

            if (principal != null && principal.PrincipalType == SecurityPrincipalType.User && IsPasswordValid(passwordCheck.Password, principal.Password))
            {
                return new SecurityPrincipalCheckResponse { IsSuccess = true, SecurityPrincipal = principal };
            }
            else
            {
                if (principal == null)
                {
                    return new SecurityPrincipalCheckResponse { IsSuccess = false, Message = "Invalid security principal" };
                }
                else if (principal.PrincipalType != SecurityPrincipalType.User)
                {
                    return new SecurityPrincipalCheckResponse { IsSuccess = false, Message = "Invalid security principal for password based login" };
                }
                else
                {
                    return new SecurityPrincipalCheckResponse { IsSuccess = false, Message = "Invalid password" };
                }
            }
        }

        public async Task<List<AssignedAccessToken>> GetAssignedAccessTokens(string contextUserId)
        {
            // if not system user, must be in administrator role to list assigned access tokens
            // this "system" users is a special case because our ACME endpoints do not use the standard security principal model and have no associated user in most cases

            if (contextUserId != "system" && !await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to list assigned access tokens without being in required role.", contextUserId);
                return null;
            }

            return await _store.GetItems<AssignedAccessToken>(nameof(AssignedAccessToken));
        }

        public async Task<bool> AddAssignedAccessToken(string contextUserId, AssignedAccessToken a)
        {
            if (!await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to add an assigned access token without being in required role.", contextUserId);
                return false;
            }

            await _store.Add(nameof(AssignedAccessToken), a);

            return true;
        }

        public async Task<bool> DeleteAssignedAccessToken(string contextUserId, string id)
        {
            if (!await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to delete an assigned access token without being in required role.", contextUserId);
                return false;
            }

            return await _store.Delete<AssignedAccessToken>(nameof(AssignedAccessToken), id);
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
