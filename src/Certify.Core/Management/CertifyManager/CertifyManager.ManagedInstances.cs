using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Models.Shared;

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

                if (!string.IsNullOrWhiteSpace(item.InternalInstanceId))
                {
                    existing.InternalInstanceId = item.InternalInstanceId;
                }

                existing.IsDashboardEnabled = item.IsDashboardEnabled;

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

        public async Task<ActionResult> RegisterManagedInstanceWithDashboard(string instanceId)
        {
            var credentialResult = await GetDashboardRegistrationCredentials();

            if (!credentialResult.IsSuccess || credentialResult.Result == null)
            {
                return new ActionResult(credentialResult.Message, false);
            }

            var username = credentialResult.Result["username"];
            var password = credentialResult.Result["password"];

            var instanceInfo = await GetHubManagedInstance(instanceId);
            if (!string.IsNullOrWhiteSpace(instanceInfo.InternalInstanceId))
            {
                var registration = await CreateDashboardRegistration(instanceInfo);

                if (registration == null || instanceInfo == null)
                {
                    return new ActionResult("Managed instance not found.", false);
                }

                var registrationSucceeded = await _dashboardClient.RegisterInstance(registration, username, password, createAccount: false);

                if (!registrationSucceeded)
                {
                    return new ActionResult("Dashboard registration could not be completed. Check that the stored dashboard account credentials are correct and that the hub can reach the Certify The Web API.", false);
                }

                var updateResult = await UpdateManagedInstanceDashboardState(instanceInfo, true);

                if (!updateResult.IsSuccess)
                {
                    return new ActionResult("Dashboard registration completed, but the managed instance status could not be updated in the hub.", false);
                }

                return new ActionResult("Managed instance added to the dashboard.", true);
            }
            else
            {
                return new ActionResult("Managed instance could not be added (invalid internal instance Id).", false);
            }
        }

        public async Task<ActionResult> RemoveManagedInstanceFromDashboard(string instanceId)
        {
            var instanceInfo = await GetHubManagedInstance(instanceId);
            if (!string.IsNullOrWhiteSpace(instanceInfo.InternalInstanceId))
            {
                var credentialResult = await GetDashboardRegistrationCredentials();

                if (!credentialResult.IsSuccess || credentialResult.Result == null)
                {
                    return new ActionResult(credentialResult.Message, false);
                }

                var username = credentialResult.Result["username"];
                var password = credentialResult.Result["password"];

                var registration = new RegisteredInstance
                {
                    InstanceId = instanceInfo.InternalInstanceId
                };

                var removalSucceeded = await _dashboardClient.RemoveInstance(registration, username, password);

                if (!removalSucceeded)
                {
                    if (instanceInfo != null)
                    {
                        var localUpdateResult = await UpdateManagedInstanceDashboardState(instanceInfo, false);

                        if (localUpdateResult.IsSuccess)
                        {
                            return new ActionResult
                            {
                                IsSuccess = true,
                                IsWarning = true,
                                Message = "Managed instance was not present on the dashboard, but dashboard status has been cleared in the hub."
                            };
                        }
                    }

                    return new ActionResult("Dashboard removal could not be completed. Check that the stored dashboard account credentials are correct and that the hub can reach the Certify The Web API.", false);
                }

                if (instanceInfo == null)
                {
                    return new ActionResult
                    {
                        IsSuccess = true,
                        IsWarning = true,
                        Message = "Managed instance removed from the dashboard, but the hub could not refresh the stored managed instance state."
                    };
                }

                var updateResult = await UpdateManagedInstanceDashboardState(instanceInfo, false);

                if (!updateResult.IsSuccess)
                {
                    return new ActionResult
                    {
                        IsSuccess = true,
                        IsWarning = true,
                        Message = "Managed instance removed from the dashboard, but the hub could not update the stored managed instance state."
                    };
                }

                return new ActionResult("Managed instance removed from the dashboard.", true);
            }
            else
            {
                return new ActionResult("Managed instance could not be removed (invalid internal instance Id).", false);
            }
        }

        private async Task<RegisteredInstance?> CreateDashboardRegistration(ManagedInstanceInfo instanceInfo)
        {
            var registrationSource = instanceInfo;

            return new RegisteredInstance
            {
                InstanceId = instanceInfo.InternalInstanceId,
                MachineName = registrationSource.DisplayTitle,
                OS = string.IsNullOrWhiteSpace(registrationSource.OSVersion) ? registrationSource.OS : $"{registrationSource.OS} {registrationSource.OSVersion}",
                AppVersion = registrationSource.ClientVersion,
                AppName = registrationSource.ClientName
            };
        }

        private async Task<ActionResult> UpdateManagedInstanceDashboardState(ManagedInstanceInfo instanceInfo, bool isDashboardEnabled)
        {
            instanceInfo.IsDashboardEnabled = isDashboardEnabled;
            return await UpdateHubManagedInstance(instanceInfo.Id, instanceInfo, false);
        }

        private async Task<ActionResult<Dictionary<string, string>>> GetDashboardRegistrationCredentials()
        {
            try
            {
                var dashboardCredentialJson = await _credentialsManager.GetUnlockedCredential(HubSharedConstants.DashboardRegistrationCredentialStorageKey);

                if (string.IsNullOrWhiteSpace(dashboardCredentialJson))
                {
                    return new ActionResult<Dictionary<string, string>>("Dashboard registration credentials required", false);
                }

                var dashboardCredentials = JsonSerializer.Deserialize<Dictionary<string, string>>(dashboardCredentialJson, Shared.JsonOptions.DefaultJsonSerializerOptions);

                if (dashboardCredentials == null
                    || !dashboardCredentials.TryGetValue("username", out var username)
                    || !dashboardCredentials.TryGetValue("password", out var password)
                    || string.IsNullOrWhiteSpace(username)
                    || string.IsNullOrWhiteSpace(password))
                {
                    return new ActionResult<Dictionary<string, string>>("The dashboard registration credential is incomplete. Update the hub dashboard account credentials and try again.", false);
                }

                return new ActionResult<Dictionary<string, string>>("OK", true, dashboardCredentials);
            }
            catch
            {
                return new ActionResult<Dictionary<string, string>>("The dashboard registration credential could not be read from the internal credentials store.", false);
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
