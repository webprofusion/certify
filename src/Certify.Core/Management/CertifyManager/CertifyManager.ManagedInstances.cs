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

        public async Task<ActionResult> UpdateHubManagedInstance(string id, ManagedInstanceInfo item)
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

                existing.DateLastReported = item.DateLastReported;

                existing.License = item.License;

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
            var deleted = await _configStore.Delete<ManagedInstanceInfo>(nameof(ManagedInstanceInfo), id);
            if (deleted)
            {
                await RemoveManagedInstanceAccessArtifacts(id);
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

        private async Task RemoveManagedInstanceAccessArtifacts(string instanceId)
        {
            // Cleanup all resources for a managed instance we are removing

            // Remove instance tags
            var instanceTags = await _configStore.GetItems<ItemTag>(nameof(ItemTag));
            foreach (var tag in instanceTags.Where(t => t.TaggedItemType == TaggedItemTypes.ManagedInstance && t.TaggedItemId == instanceId).ToList())
            {
                await _configStore.Delete<ItemTag>(nameof(ItemTag), tag.Id);
            }

            var allPrincipals = await _configStore.GetItems<SecurityPrincipal>(nameof(SecurityPrincipal));
            var principalIds = allPrincipals
                .Where(p => p.PrincipalType == SecurityPrincipalType.ManagedInstance && string.Equals(p.ExternalIdentifier, instanceId, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Id, instanceId, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

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

            // Remove security principal(s)
            foreach (var principalId in principalIds)
            {
                await _configStore.Delete<SecurityPrincipal>(nameof(SecurityPrincipal), principalId);
            }
        }
    }
}
