using System.Text.Json;
using Certify.API.Management;
using Certify.Client;
using Certify.Models;
using Certify.Models.API;
using Certify.Models.Config;
using Certify.Models.Reporting;
using Certify.Server.Api.Public.SignalR.ManagementHub;
using Microsoft.AspNetCore.SignalR;

namespace Certify.Server.Api.Public.Services
{
    public partial class ManagementAPI
    {
        IInstanceManagementStateProvider _mgmtStateProvider;
        IHubContext<InstanceManagementHub, IInstanceManagementHub> _mgmtHubContext;
        ICertifyInternalApiClient _backendAPIClient;

        public ManagementAPI(IInstanceManagementStateProvider mgmtStateProvider, IHubContext<InstanceManagementHub, IInstanceManagementHub> mgmtHubContext, ICertifyInternalApiClient backendAPIClient)
        {
            _mgmtStateProvider = mgmtStateProvider;
            _mgmtHubContext = mgmtHubContext;
            _backendAPIClient = backendAPIClient;
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

            var args = new KeyValuePair
                <string, string>[] {
                    new("instanceId", instanceId) ,
                    new("managedCertId", managedCertId)
                };

            var cmd = new InstanceCommandRequest(ManagementHubCommands.GetManagedItem, args);
            var result = await GetCommandResult(instanceId, cmd);

            if (result?.Value != null)
            {
                return JsonSerializer.Deserialize<ManagedCertificate>(result.Value);

            }
            else
            {
                return null;
            }
        }

        public async Task<ManagedCertificate?> UpdateManagedCertificate(string instanceId, ManagedCertificate managedCert, AuthContext authContext)
        {
            // get managed cert via local api or via management hub

            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("managedCert", JsonSerializer.Serialize(managedCert))
                };

            var cmd = new InstanceCommandRequest(ManagementHubCommands.UpdateManagedItem, args);

            var result = await GetCommandResult(instanceId, cmd);

            if (result?.Value != null)
            {
                var update = JsonSerializer.Deserialize<ManagedCertificate>(result.Value);

                _mgmtStateProvider.UpdateCachedManagedInstanceItem(instanceId, update);
                return update;
            }
            else
            {
                return null;
            }
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

            var cmd = new InstanceCommandRequest(ManagementHubCommands.DeleteManagedItem, args);

            var result = await GetCommandResult(instanceId, cmd);

            try
            {
                _mgmtStateProvider.DeleteCachedManagedInstanceItem(instanceId, managedCertId);
                return true;
            }
            catch
            {
                return false;
            }
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

        public async Task<ICollection<Models.AccountDetails>?> GetAcmeAccounts(string instanceId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) 
                };

            var cmd = new InstanceCommandRequest(ManagementHubCommands.GetAcmeAccounts, args);

            var result = await GetCommandResult(instanceId, cmd);

            if (result?.Value != null)
            {
                return JsonSerializer.Deserialize<ICollection<Models.AccountDetails>>(result.Value);
            }
            else
            {
                return null;
            }
        }

        public async Task<ActionResult?> AddAcmeAccount(string instanceId, ContactRegistration registration, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("registration", JsonSerializer.Serialize(registration))
                };

            var cmd = new InstanceCommandRequest(ManagementHubCommands.AddAcmeAccount, args);

            var result = await GetCommandResult(instanceId, cmd);

            if (result?.Value != null)
            {
                return JsonSerializer.Deserialize<ActionResult>(result.Value);
            }
            else
            {
                return null;
            }
        }

        public async Task<LogItem[]> GetItemLog(string instanceId, string managedCertId, int maxLines, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("managedCertId",managedCertId),
                    new("limit",maxLines.ToString())
                };

            var cmd = new InstanceCommandRequest(ManagementHubCommands.GetManagedItemLog, args);

            var result = await GetCommandResult(instanceId, cmd);

            if (result?.Value != null)
            {
                return JsonSerializer.Deserialize<LogItem[]>(result.Value);
            }
            else
            {
                return [];
            }
        }

        internal async Task<List<StatusMessage>> TestManagedCertificateConfiguration(string instanceId, ManagedCertificate managedCert, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("managedCert",JsonSerializer.Serialize(managedCert))
                };

            var cmd = new InstanceCommandRequest(ManagementHubCommands.TestManagedItemConfiguration, args);

            var result = await GetCommandResult(instanceId, cmd);

            if (result?.Value != null)
            {
                return JsonSerializer.Deserialize<List<StatusMessage>>(result.Value);
            }
            else
            {
                return [];
            }
        }

        internal async Task<List<ActionStep>> GetPreviewActions(string instanceId, ManagedCertificate managedCert, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                    new("instanceId", instanceId) ,
                    new("managedCert",JsonSerializer.Serialize(managedCert))
                };

            var cmd = new InstanceCommandRequest(ManagementHubCommands.GetManagedItemRenewalPreview, args);

            var result = await GetCommandResult(instanceId, cmd);

            if (result?.Value != null)
            {
                return JsonSerializer.Deserialize<List<ActionStep>>(result.Value);
            }
            else
            {
                return [];
            }
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

