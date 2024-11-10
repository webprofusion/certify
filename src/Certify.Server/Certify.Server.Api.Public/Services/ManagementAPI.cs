using System.Text.Json;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Models.Reporting;
using Certify.Server.Api.Public.SignalR.ManagementHub;
using Microsoft.AspNetCore.SignalR;

namespace Certify.Server.Api.Public.Services
{
    /// <summary>
    /// Management Hub API
    /// </summary>
    public partial class ManagementAPI
    {
        IInstanceManagementStateProvider _mgmtStateProvider;
        IHubContext<InstanceManagementHub, IInstanceManagementHub> _mgmtHubContext;
        ICertifyInternalApiClient _backendAPIClient;

        /// <summary>
        /// Constructor for Management Hub API
        /// </summary>
        /// <param name="mgmtStateProvider"></param>
        /// <param name="mgmtHubContext"></param>
        /// <param name="backendAPIClient"></param>
        public ManagementAPI(IInstanceManagementStateProvider mgmtStateProvider, IHubContext<InstanceManagementHub, IInstanceManagementHub> mgmtHubContext, ICertifyInternalApiClient backendAPIClient)
        {
            _mgmtStateProvider = mgmtStateProvider;
            _mgmtHubContext = mgmtHubContext;
            _backendAPIClient = backendAPIClient;
        }

        private async Task<InstanceCommandResult?> GetCommandResult(string instanceId, InstanceCommandRequest cmd)
        {
            var connectionId = _mgmtStateProvider.GetConnectionIdForInstance(instanceId);

            if (connectionId == null)
            {
                throw new Exception("Instance connection info not known, cannot send commands to instance.");
            }

            _mgmtStateProvider.AddAwaitedCommandRequest(cmd);

            await _mgmtHubContext.Clients.Client(connectionId).SendCommandRequest(cmd);

            return await _mgmtStateProvider.ConsumeAwaitedCommandResult(cmd.CommandId);
        }

        private async Task SendCommandWithNoResult(string instanceId, InstanceCommandRequest cmd)
        {
            var connectionId = _mgmtStateProvider.GetConnectionIdForInstance(instanceId);

            if (connectionId == null)
            {
                throw new Exception("Instance connection info not known, cannot send commands to instance.");
            }

            _mgmtStateProvider.AddAwaitedCommandRequest(cmd);

            await _mgmtHubContext.Clients.Client(connectionId).SendCommandRequest(cmd);
        }

        private async Task<T?> PerformInstanceCommandTaskWithResult<T>(string instanceId, KeyValuePair<string, string>[] args, string commandType)
        {
            var cmd = new InstanceCommandRequest(commandType, args);

            var result = await GetCommandResult(instanceId, cmd);

            if (result?.Value != null)
            {
                return JsonSerializer.Deserialize<T>(result.Value);
            }
            else
            {
                return default;
            }
        }

        /// <summary>
        /// Fetch managed cert details from the target instance
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="managedCertId"></param>
        /// <param name="authContext"></param>
        /// <returns></returns>
        public async Task<ManagedCertificate?> GetManagedCertificate(string instanceId, string managedCertId, AuthContext authContext)
        {
            // get managed cert via local api or via management hub

            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("managedCertId", managedCertId)
                };

            return await PerformInstanceCommandTaskWithResult<ManagedCertificate?>(instanceId, args, ManagementHubCommands.GetManagedItem);
        }

        /// <summary>
        /// Add or Update Managed Certificate for target instance
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="managedCert"></param>
        /// <param name="authContext"></param>
        /// <returns></returns>
        public async Task<ManagedCertificate?> UpdateManagedCertificate(string instanceId, ManagedCertificate managedCert, AuthContext authContext)
        {
            // update managed cert via management hub

            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("managedCert", JsonSerializer.Serialize(managedCert))
                };

            var result = await PerformInstanceCommandTaskWithResult<ManagedCertificate?>(instanceId, args, ManagementHubCommands.UpdateManagedItem);

            if (result != null)
            {
                _mgmtStateProvider.UpdateCachedManagedInstanceItem(instanceId, result);
            }

            return result;
        }

        /// <summary>
        /// Delete a managed certificate
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="managedCertId"></param>
        /// <param name="authContext"></param>
        /// <returns></returns>
        public async Task<bool> RemoveManagedCertificate(string instanceId, string managedCertId, AuthContext authContext)
        {
            // delete managed cert via management hub

            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("managedCertId",managedCertId)
                };

            var deletedOK = await PerformInstanceCommandTaskWithResult<bool>(instanceId, args, ManagementHubCommands.RemoveDeleteManagedItem);

            if (deletedOK)
            {
                try
                {
                    _mgmtStateProvider.DeleteCachedManagedInstanceItem(instanceId, managedCertId);
                }
                catch
                {
                }
            }

            return deletedOK;
        }

        public async Task<StatusSummary> GetManagedCertificateSummary(AuthContext? currentAuthContext)
        {

            var allSummary = _mgmtStateProvider.GetManagedInstanceStatusSummaries();
            var sum = new StatusSummary();

            foreach (var item in allSummary)
            {
                if (item.Value != null)
                {
                    sum.Total += item.Value.Total;
                    sum.Error += item.Value.Error;
                    sum.Warning += item.Value.Warning;
                    sum.AwaitingUser += item.Value.AwaitingUser;
                    sum.Healthy += item.Value.Healthy;
                    sum.NoCertificate += item.Value.NoCertificate;

                }
            }

            return await Task.FromResult(sum);
        }

        public async Task<ICollection<Models.CertificateAuthority>?> GetCertificateAuthorities(string instanceId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId)
                };

            return await PerformInstanceCommandTaskWithResult<ICollection<Models.CertificateAuthority>>(instanceId, args, ManagementHubCommands.GetCertificateAuthorities);
        }
        public async Task<ActionResult?> UpdateCertificateAuthority(string instanceId, CertificateAuthority certificateAuthority, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("certificateAuthority", JsonSerializer.Serialize(certificateAuthority))
                };

            return await PerformInstanceCommandTaskWithResult<ActionResult?>(instanceId, args, ManagementHubCommands.UpdateCertificateAuthority);
        }

        public async Task<ActionResult?> RemoveCertificateAuthority(string instanceId, string id, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId),
                    new("id", id)
                };

            return await PerformInstanceCommandTaskWithResult<ActionResult?>(id, args, ManagementHubCommands.RemoveCertificateAuthority);
        }

        public async Task<ICollection<Models.AccountDetails>?> GetAcmeAccounts(string instanceId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId)
                };

            return await PerformInstanceCommandTaskWithResult<ICollection<Models.AccountDetails>>(instanceId, args, ManagementHubCommands.GetAcmeAccounts);
        }

        public async Task<ActionResult?> AddAcmeAccount(string instanceId, ContactRegistration registration, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("registration", JsonSerializer.Serialize(registration))
                };

            return await PerformInstanceCommandTaskWithResult<ActionResult?>(instanceId, args, ManagementHubCommands.AddAcmeAccount);
        }
        public async Task<ActionResult?> RemoveAcmeAccount(string instanceId, string storageKey, bool deactivate, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId),
                    new("storageKey", storageKey),
                    new("deactivate", deactivate.ToString())
                };

            return await PerformInstanceCommandTaskWithResult<ActionResult?>(instanceId, args, ManagementHubCommands.RemoveAcmeAccount);
        }
        public async Task<ICollection<ChallengeProviderDefinition>?> GetChallengeProviders(string instanceId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId)
                };
            return await PerformInstanceCommandTaskWithResult<ICollection<ChallengeProviderDefinition>>(instanceId, args, ManagementHubCommands.GetChallengeProviders);
        }
        public async Task<ICollection<DeploymentProviderDefinition>?> GetDeploymentProviders(string instanceId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId)
                };
            return await PerformInstanceCommandTaskWithResult<ICollection<DeploymentProviderDefinition>>(instanceId, args, ManagementHubCommands.GetDeploymentProviders);
        }

        public async Task<ICollection<ActionStep>?> ExecuteDeploymentTask(string instanceId, string managedCertificateId, string taskId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId),
                    new("managedCertificateId", managedCertificateId),
                    new("taskId", taskId)
            };

            return await PerformInstanceCommandTaskWithResult<ICollection<ActionStep>>(instanceId, args, ManagementHubCommands.ExecuteDeploymentTask);
        }

        public async Task<ICollection<Models.Providers.DnsZone>?> GetDnsZones(string instanceId, string providerTypeId, string credentialId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId),
                    new("providerTypeId", providerTypeId),
                    new("credentialId", credentialId)
                };

            return await PerformInstanceCommandTaskWithResult<ICollection<DnsZone>>(instanceId, args, ManagementHubCommands.GetDnsZones);
        }

        public async Task<ICollection<Models.Config.StoredCredential>?> GetStoredCredentials(string instanceId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId)
                };

            return await PerformInstanceCommandTaskWithResult<ICollection<StoredCredential>>(instanceId, args, ManagementHubCommands.GetStoredCredentials);
        }

        public async Task<ActionResult?> UpdateStoredCredential(string instanceId, StoredCredential item, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("item", JsonSerializer.Serialize(item))
                };

            return await PerformInstanceCommandTaskWithResult<ActionResult?>(instanceId, args, ManagementHubCommands.UpdateStoredCredential);
        }

        public async Task<ActionResult?> RemoveStoredCredential(string instanceId, string storageKey, AuthContext authContext)
        {
            // delete stored credential via management hub

            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("storageKey",storageKey)
                };

            return await PerformInstanceCommandTaskWithResult<ActionResult?>(instanceId, args, ManagementHubCommands.RemoveStoredCredential);
        }

        public async Task<LogItem[]> GetItemLog(string instanceId, string managedCertId, int maxLines, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("managedCertId",managedCertId),
                    new("limit",maxLines.ToString())
                };

            return await PerformInstanceCommandTaskWithResult<LogItem[]>(instanceId, args, ManagementHubCommands.GetManagedItemLog) ?? [];
        }

        internal async Task<List<StatusMessage>> TestManagedCertificateConfiguration(string instanceId, ManagedCertificate managedCert, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("managedCert",JsonSerializer.Serialize(managedCert))
                };

            return await PerformInstanceCommandTaskWithResult<List<StatusMessage>>(instanceId, args, ManagementHubCommands.TestManagedItemConfiguration) ?? [];
        }

        internal async Task<List<ActionStep>> GetPreviewActions(string instanceId, ManagedCertificate managedCert, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("managedCert",JsonSerializer.Serialize(managedCert))
                };

            return await PerformInstanceCommandTaskWithResult<List<ActionStep>>(instanceId, args, ManagementHubCommands.GetManagedItemRenewalPreview) ?? [];
        }

        internal async Task PerformManagedCertificateRequest(string instanceId, string managedCertId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("managedCertId",managedCertId)
                };

            var cmd = new InstanceCommandRequest(ManagementHubCommands.PerformManagedItemRequest, args);

            await SendCommandWithNoResult(instanceId, cmd);
        }
    }
}

