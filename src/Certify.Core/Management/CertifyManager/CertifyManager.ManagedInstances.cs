using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Hub;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        public async Task<ActionResult<ManagedInstanceInfo>> AddHubManagedInstance(ManagedInstanceInfo item)
        {
            string? preferredId = null;

            if (!string.IsNullOrWhiteSpace(item.InstanceId) && Guid.TryParse(item.InstanceId, out var parsedInstanceId))
            {
                preferredId = parsedInstanceId.ToString();
            }
            else if (!string.IsNullOrWhiteSpace(item.Id) && Guid.TryParse(item.Id, out var parsedId))
            {
                preferredId = parsedId.ToString();
            }

            if (!string.IsNullOrWhiteSpace(preferredId))
            {
                var existing = await _configStore.Get<ManagedInstanceInfo>(nameof(ManagedInstanceInfo), preferredId);
                if (existing != null)
                {
                    return new ActionResult<ManagedInstanceInfo>("Managed instance already exists.", false);
                }
            }

            item.Id = preferredId ?? Guid.NewGuid().ToString();
            item.InstanceId = item.Id;

            if (item.DateRegistered == default)
            {
                item.DateRegistered = DateTimeOffset.UtcNow;
            }

            if (item.DateLastReported == default)
            {
                item.DateLastReported = DateTimeOffset.UtcNow;
            }

            await _configStore.Add(nameof(ManagedInstanceInfo), item);

            var principalId = await EnsureManagedInstanceSecurityPrincipal(item);
            if (!string.IsNullOrWhiteSpace(principalId))
            {
                item.SecurityPrincipalId = principalId;
                await _configStore.Update(nameof(ManagedInstanceInfo), item);
            }

            return new ActionResult<ManagedInstanceInfo>("Added", true, item);
        }

        public async Task<ActionResult> UpdateHubManagedInstance(string id, ManagedInstanceInfo item, bool isHeartBeatInfo)
        {
            if (id != item.Id)
            {
                return new ActionResult("Item Id mismatch. Cannot update.", false);
            }

            var existing = await _configStore.Get<ManagedInstanceInfo>(nameof(ManagedInstanceInfo), id);

            if (existing != null)
            {
                existing.OS = item.OS;
                existing.OSVersion = item.OSVersion;

                existing.ClientName = item.ClientName;
                existing.ClientVersion = item.ClientVersion;

                existing.Title = item.Title;
                if (!isHeartBeatInfo)
                {
                    // Preserve existing custom title for regular instance heartbeats where CustomTitle is omitted (null).
                    // Apply updates when explicitly provided by hub admin operations (including clear via empty string).
                    if (item.CustomTitle != null)
                    {
                        existing.CustomTitle = string.IsNullOrWhiteSpace(item.CustomTitle) ? null : item.CustomTitle.Trim();
                    }

                    if (item.Description != null)
                    {
                        existing.Description = string.IsNullOrWhiteSpace(item.Description) ? null : item.Description.Trim();
                    }
                }

                existing.DateLastReported = item.DateLastReported;

                existing.License = item.License;

                if (!string.IsNullOrWhiteSpace(item.RequestAuthSecretHash))
                {
                    existing.RequestAuthSecretHash = item.RequestAuthSecretHash;
                }

                if (existing.DateRegistered == DateTimeOffset.MinValue)
                {
                    existing.DateRegistered = DateTimeOffset.UtcNow;
                }

                await _configStore.Update(nameof(ManagedInstanceInfo), existing);

                var principalId = await EnsureManagedInstanceSecurityPrincipal(existing);
                if (!string.IsNullOrWhiteSpace(principalId) && existing.SecurityPrincipalId != principalId)
                {
                    existing.SecurityPrincipalId = principalId;
                    await _configStore.Update(nameof(ManagedInstanceInfo), existing);
                }

                return new ActionResult("Updated", true);
            }
            else
            {
                return new ActionResult("Item Not found. Cannot update.", false);
            }
        }

        public async Task<ManagedInstanceInfo> GetHubManagedInstance(string id)
        {
            var item = await _configStore.Get<ManagedInstanceInfo>(nameof(ManagedInstanceInfo), id);
            return item;
        }

        public async Task<ICollection<ManagedInstanceInfo>> GetHubManagedInstances()
        {
            return await _configStore.GetItems<ManagedInstanceInfo>(nameof(ManagedInstanceInfo));
        }

        public async Task<ActionResult> RemoveHubManagedInstance(string id)
        {
            var existing = await _configStore.Get<ManagedInstanceInfo>(nameof(ManagedInstanceInfo), id);

            if (existing == null)
            {
                var allInstances = await _configStore.GetItems<ManagedInstanceInfo>(nameof(ManagedInstanceInfo));
                existing = allInstances.FirstOrDefault(i => string.Equals(i.InstanceId, id, StringComparison.OrdinalIgnoreCase));
            }

            if (existing == null)
            {
                return new ActionResult("Not found", false);
            }

            var deleted = await _configStore.Delete<ManagedInstanceInfo>(nameof(ManagedInstanceInfo), existing.Id);
            if (deleted)
            {
                await RemoveManagedInstanceAccessArtifacts(existing, id);
                return new ActionResult("Deleted", true);
            }
            else
            {
                return new ActionResult("Not found", false);
            }
        }

        private async Task<string?> EnsureManagedInstanceSecurityPrincipal(ManagedInstanceInfo instance)
        {
            if (string.IsNullOrWhiteSpace(instance?.InstanceId))
            {
                return null;
            }

            SecurityPrincipal? existing = null;

            if (!string.IsNullOrWhiteSpace(instance.SecurityPrincipalId))
            {
                existing = await _configStore.Get<SecurityPrincipal>(nameof(SecurityPrincipal), instance.SecurityPrincipalId);
            }

            if (existing == null)
            {
                var allPrincipals = await _configStore.GetItems<SecurityPrincipal>(nameof(SecurityPrincipal));
                existing = allPrincipals.FirstOrDefault(p =>
                    string.Equals(p.ExternalIdentifier, instance.InstanceId, StringComparison.OrdinalIgnoreCase)
                    && p.PrincipalType == SecurityPrincipalType.ManagedInstance);
            }

            if (existing == null)
            {
                var principal = new SecurityPrincipal
                {
                    Id = Guid.NewGuid().ToString(),
                    ExternalIdentifier = instance.InstanceId,
                    Provider = StandardIdentityProviders.INTERNAL,
                    PrincipalType = SecurityPrincipalType.ManagedInstance,
                    Title = instance.Title,
                    Description = instance.Description
                };

                await _configStore.Add(nameof(SecurityPrincipal), principal);
                return principal.Id;
            }
            else
            {
                existing.ExternalIdentifier = instance.InstanceId;
                existing.Provider = StandardIdentityProviders.INTERNAL;
                existing.PrincipalType = SecurityPrincipalType.ManagedInstance;
                existing.Title = instance.Title;
                existing.Description = instance.Description;

                await _configStore.Update(nameof(SecurityPrincipal), existing);
                return existing.Id;
            }
        }

        private async Task RemoveManagedInstanceAccessArtifacts(ManagedInstanceInfo? instance, string deletedIdentifier)
        {
            var managedInstanceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(deletedIdentifier))
            {
                managedInstanceIds.Add(deletedIdentifier);
            }

            if (!string.IsNullOrWhiteSpace(instance?.Id))
            {
                managedInstanceIds.Add(instance.Id);
            }

            if (!string.IsNullOrWhiteSpace(instance?.InstanceId))
            {
                managedInstanceIds.Add(instance.InstanceId);
            }

            foreach (var managedInstanceId in managedInstanceIds)
            {
                await RemoveHubItemTagsForItem(TaggedItemTypes.ManagedInstance, managedInstanceId);
            }

            var allPrincipals = await _configStore.GetItems<SecurityPrincipal>(nameof(SecurityPrincipal));
            var principalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(instance?.SecurityPrincipalId))
            {
                principalIds.Add(instance.SecurityPrincipalId);
            }

            foreach (var principalId in allPrincipals
                .Where(p =>
                    managedInstanceIds.Contains(p.Id)
                    || (!string.IsNullOrWhiteSpace(p.ExternalIdentifier) && managedInstanceIds.Contains(p.ExternalIdentifier)))
                .Select(p => p.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                principalIds.Add(principalId);
            }

            if (!principalIds.Any())
            {
                return;
            }

            // Remove assigned roles
            var assignedRoles = await _configStore.GetItems<AssignedRole>(nameof(AssignedRole));
            foreach (var assignedRole in assignedRoles.Where(r => principalIds.Contains(r.SecurityPrincipalId)).ToList())
            {
                await _configStore.Delete<AssignedRole>(nameof(AssignedRole), assignedRole.Id);
            }

            // Remove access tokens
            var assignedTokens = await _configStore.GetItems<AssignedAccessToken>(nameof(AssignedAccessToken));
            foreach (var token in assignedTokens.Where(t => principalIds.Contains(t.SecurityPrincipalId)).ToList())
            {
                await _configStore.Delete<AssignedAccessToken>(nameof(AssignedAccessToken), token.Id);
            }

            foreach (var principalId in principalIds)
            {
                await RemoveHubItemTagsForItem(TaggedItemTypes.SecurityPrincipal, principalId);
                await _configStore.Delete<SecurityPrincipal>(nameof(SecurityPrincipal), principalId);
            }
        }
    }
}
