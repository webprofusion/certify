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
            var list = await GetSecurityPrincipals(StandardSecurityPrincipals.System);
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

        public async Task<List<ResourcePolicy>> GetResourcePolicies(string contextUserId)
        {
            return await _store.GetItems<ResourcePolicy>(nameof(ResourcePolicy));
        }

        public async Task<List<ResourceAction>> GetResourceActions(string contextUserId)
        {
            return await _store.GetItems<ResourceAction>(nameof(ResourceAction));
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
            if (contextUserId == StandardSecurityPrincipals.System && !string.IsNullOrEmpty(principal.ExternalIdentifier))
            {
                // special case for auto added users via OIDC
                bypassIntegrityCheck = true;
            }

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
                // a caller supplied password has to meet the minimum length
                if (principal.Password.Length < MinimumPasswordLength)
                {
                    await AuditWarning("User {contextUserId} attempted to use AddSecurityPrincipal [{principalId}] with a password shorter than the minimum length.", contextUserId, principal?.Id);
                    return false;
                }

                principal.Password = HashPassword(principal.Password);
            }
            else
            {
                // no password was supplied, which is the normal case for managed instance and other automated
                // principals. They are given a generated password rather than being rejected, as they never
                // authenticate with one.
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
        /// Check if a security principal has access to the given resource action.
        /// System context may evaluate another principal's access but does not auto-allow.
        /// </summary>
        public async Task<bool> IsSecurityPrincipalAuthorised(string contextUserId, AccessCheck check)
        {
            if (!await CanEvaluateAccessCheck(contextUserId, check))
            {
                return false;
            }

            var scope = await EvaluateAccessScopeInternal(check);

            if (!scope.HasAccess)
            {
                return false;
            }

            // Action-level checks without concrete resource tags succeed when any authorizing role grants the action,
            // including tag-scoped roles. Concrete resource filtering uses EvaluateAccessScope / IsResourceInScope.
            if (check.ResourceTags != null)
            {
                if (!IsResourceInScope(scope, check.ResourceTags))
                {
                    return false;
                }
            }

            return MatchesIncludedResources(scope.AuthorizingRoles, check);
        }

        /// <summary>
        /// Evaluate authorizing roles and unrestricted/scoped state for a principal and action.
        /// </summary>
        public async Task<ResourceAccessScope> EvaluateAccessScope(string contextUserId, AccessCheck check)
        {
            if (!await CanEvaluateAccessCheck(contextUserId, check))
            {
                return new ResourceAccessScope
                {
                    AllowUnscopedResources = check?.AllowUnscopedResources == true
                };
            }

            return await EvaluateAccessScopeInternal(check);
        }

        /// <summary>
        /// True when a concrete resource (via tags) is within the resolved access scope.
        /// </summary>
        public bool IsResourceInScope(ResourceAccessScope scope, IEnumerable<TagSummary>? resourceTags)
            => ResourceAccess.IsResourceInScope(scope, resourceTags);

        private Task<bool> CanEvaluateAccessCheck(string contextUserId, AccessCheck? check)
        {
            // Authorization evaluation is always against check.SecurityPrincipalId.
            // contextUserId is reserved for mutating/read-admin gates elsewhere; historically
            // IsSecurityPrincipalAuthorised did not require the caller to be admin to evaluate a check.
            if (check == null || string.IsNullOrWhiteSpace(check.SecurityPrincipalId) || string.IsNullOrWhiteSpace(check.ResourceActionId))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        private async Task<bool> CanReadPrincipalAccess(string contextUserId, string principalId)
        {
            if (string.IsNullOrWhiteSpace(principalId))
            {
                return false;
            }

            if (contextUserId == StandardSecurityPrincipals.System || contextUserId == principalId)
            {
                return true;
            }

            return await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id);
        }

        private async Task<ResourceAccessScope> EvaluateAccessScopeInternal(AccessCheck check)
        {
            var result = new ResourceAccessScope
            {
                AllowUnscopedResources = check.AllowUnscopedResources
            };

            // TODO: cache results for performance based on last update of access control config, which will be largely static

            var allAssignedRoles = await _store.GetItems<AssignedRole>(nameof(AssignedRole));
            var allRoles = await _store.GetItems<Role>(nameof(Role));
            var allPolicies = await _store.GetItems<ResourcePolicy>(nameof(ResourcePolicy));

            IEnumerable<AssignedRole> spAssignedRoles = allAssignedRoles.Where(a => a.SecurityPrincipalId == check.SecurityPrincipalId);

            // if scoped AssignedRole.ID (not just the roleID) specified (access token check etc), reduce scope of assigned roles to check
            if (check.ScopedAssignedRoles?.Any() == true)
            {
                spAssignedRoles = spAssignedRoles.Where(a => check.ScopedAssignedRoles.Contains(a.Id));
            }

            var spAssignedRoleDefinitions = allRoles.Where(r => spAssignedRoles.Any(t => t.RoleId == r.Id)).ToList();

            var roleDefinitionsGrantingAction = spAssignedRoleDefinitions
                .Where(r => r.Policies != null && r.Policies.Any(policyId =>
                    allPolicies.Any(p => p.Id == policyId
                        && p.ResourceActions != null
                        && p.ResourceActions.Contains(check.ResourceActionId))))
                .ToList();

            var authorizingRoles = spAssignedRoles
                .Where(a => roleDefinitionsGrantingAction.Any(r => r.Id == a.RoleId))
                .ToList();

            result.AuthorizingRoles = authorizingRoles;
            result.HasAccess = authorizingRoles.Count > 0;
            result.IsUnrestricted = authorizingRoles.Any(a => a.ScopedTags == null || a.ScopedTags.Count == 0);

            return result;
        }

        /// <summary>
        /// True when the check's identifier is within the resources explicitly included on the authorizing
        /// roles. Roles carrying no resources of the checked type impose no restriction.
        /// Domain resources are Domain Match rules, evaluated by the shared DomainMatchRules implementation
        /// via <see cref="ResourceAccess"/>; other resource types match on exact identifier.
        /// </summary>
        private static bool MatchesIncludedResources(IEnumerable<AssignedRole> authorizingRoles, AccessCheck check)
        {
            if (check.ResourceType == ResourceTypes.Domain)
            {
                return ResourceAccess.IsIdentifierPermittedByDomainRestrictions(authorizingRoles, check.Identifier);
            }

            var rolesWithIncludedResources = authorizingRoles
                .Where(a => a.IncludedResources?.Any(r => r.ResourceType == check.ResourceType) == true)
                .ToList();

            if (rolesWithIncludedResources.Count == 0)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(check.Identifier))
            {
                return false;
            }

            return rolesWithIncludedResources
                .SelectMany(a => a.IncludedResources ?? [])
                .Where(r => r.ResourceType == check.ResourceType)
                .Any(r => r.Identifier == check.Identifier);
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
                ScopedAssignedRoles = knownAssignedToken.ScopedAssignedRoles,
                ResourceTags = check.ResourceTags // Pass resource tags for tag-based access control
            };

            var isAuthorised = await IsSecurityPrincipalAuthorised(scopedCheck.SecurityPrincipalId, scopedCheck);

            if (isAuthorised)
            {
                // TODO: check token scope restrictions
                return new ActionResult("OK", true)
                {
                    Result = new AccessTokenAuthorizationContext
                    {
                        SecurityPrincipalId = knownAssignedToken.SecurityPrincipalId,
                        ScopedAssignedRoles = knownAssignedToken.ScopedAssignedRoles ?? []
                    }
                };
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

        /// <summary>
        /// Shortest password accepted when setting or changing a password. Enforced server side, so that a client
        /// which does not apply its own rule (or a direct API caller) cannot set a blank or trivially short password.
        ///
        /// This applies only to a password supplied by a caller. Principals created without one, such as managed
        /// instance and other automated principals, are still given a generated password instead.
        /// </summary>
        public const int MinimumPasswordLength = 8;

        public async Task<bool> UpdateSecurityPrincipalPassword(string contextUserId, SecurityPrincipalPasswordUpdate passwordUpdate, bool requirePasswordConfirmation = true)
        {
            if (passwordUpdate.SecurityPrincipalId != contextUserId && !await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
            {
                await AuditWarning("User {contextUserId} attempted to use updated password for [{id}] without being in required role.", contextUserId, passwordUpdate.SecurityPrincipalId);
                return false;
            }

            if (string.IsNullOrWhiteSpace(passwordUpdate.NewPassword) || passwordUpdate.NewPassword.Length < MinimumPasswordLength)
            {
                await AuditWarning("User {contextUserId} attempted to set a password for [{id}] which is shorter than the minimum length.", contextUserId, passwordUpdate.SecurityPrincipalId);
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

        /// <summary>
        /// Expected number of components in a stored password hash, in the form v1.{salt}.{hash}
        /// </summary>
        private const int PasswordHashComponentCount = 3;

        private const string PasswordHashVersionPrefix = "v1";

        public bool IsPasswordValid(string password, string currentHash)
        {
            // Fail closed when there is no stored hash. This previously returned true for a blank hash and a blank
            // password, which meant a principal holding no password could authenticate by supplying none. Principals
            // are written to the store without a password by some paths (managed instance principals, for example),
            // so this state does occur.
            if (string.IsNullOrWhiteSpace(currentHash) || password == null)
            {
                return false;
            }

            var components = currentHash.Split('.');

            // A stored value which is not in the expected format cannot be matched against. Checking this before
            // indexing also avoids throwing on a malformed hash, which would surface as a server error rather than
            // as a failed login.
            if (components.Length != PasswordHashComponentCount
                || components[0] != PasswordHashVersionPrefix
                || string.IsNullOrWhiteSpace(components[1]))
            {
                return false;
            }

            string hashedPassword;

            try
            {
                // hash provided password with same salt to compare result
                hashedPassword = HashPassword(password, components[1]);
            }
            catch (FormatException)
            {
                // the stored salt is not valid base64
                return false;
            }

            return FixedTimeEquals(currentHash, hashedPassword);
        }

        /// <summary>
        /// Compare two password hashes without leaking, through timing, how much of a candidate hash matched.
        /// </summary>
        private static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);

            if (leftBytes.Length != rightBytes.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
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
            if (!await CanReadPrincipalAccess(contextUserId, id))
            {
                await AuditWarning("User {contextUserId} attempted to read assigned role for [{id}] without being in required role.", contextUserId, id);
                return null;
            }

            var assignedRoles = await _store.GetItems<AssignedRole>(nameof(AssignedRole));

            return assignedRoles.Where(r => r.SecurityPrincipalId == id).ToList();
        }

        public async Task<RoleStatus> GetSecurityPrincipalRoleStatus(string contextUserId, string id)
        {
            if (!await CanReadPrincipalAccess(contextUserId, id))
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
                // The principal was fetched with includePassword so the hash could be checked. Clear it before returning
                principal.Password = null;

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
            // this system user is a special case because our ACME endpoints do not use the standard security principal model and have no associated user in most cases

            if (contextUserId != StandardSecurityPrincipals.System && !await IsPrincipalInRole(contextUserId, contextUserId, StandardRoles.Administrator.Id))
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

        /// <summary>
        /// Check if resource tags match the required tag scope for access control.
        /// Delegates to the shared ResourceAccess helper.
        /// </summary>
        public static bool IsResourceTagScopeMatch(List<TagSummary>? resourceTags, List<TagScope>? scopedTags, bool requireAll)
            => ResourceAccess.IsResourceTagScopeMatch(resourceTags, scopedTags, requireAll);
    }
}
