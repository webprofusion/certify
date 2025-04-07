using System.Text.Json;
using Certify.Client;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Config.Migration;
using Certify.Models.Hub;
using Certify.Models.Providers;
using Certify.Models.Reporting;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Certify.Shared;
using Microsoft.AspNetCore.SignalR;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Management Hub API for handling commands and requests. This sends commands to instances (local ICertifyManager instance or remote) and handles the results.
    /// </summary>
    public partial class ManagementAPI
    {
        IInstanceManagementStateProvider _mgmtStateProvider;
        IHubContext<InstanceManagementHub, IInstanceManagementHub> _mgmtHubContext;
        Certify.Management.ICertifyManager _certifyManager = default!;

        ILogger<ManagementAPI> _log;

        /// <summary>
        /// Initializes a new instance of the <see cref="ManagementAPI"/> class.
        /// </summary>
        /// <param name="mgmtStateProvider">The instance management state provider.</param>
        /// <param name="mgmtHubContext">The management hub context for SignalR or Direct communication.</param>
        public ManagementAPI(IInstanceManagementStateProvider mgmtStateProvider, IHubContext<InstanceManagementHub, IInstanceManagementHub> mgmtHubContext, ILogger<ManagementAPI> log)
        {
            _mgmtStateProvider = mgmtStateProvider;
            _mgmtHubContext = mgmtHubContext;
            _log = log;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ManagementAPI"/> class with a Certify manager.
        /// </summary>
        /// <param name="mgmtStateProvider">The instance management state provider.</param>
        /// <param name="mgmtHubContext">The management hub context for SignalR communication.</param>
        /// <param name="certifyManager">The in-process Certify manager instance.</param>
        public ManagementAPI(IInstanceManagementStateProvider mgmtStateProvider, IHubContext<InstanceManagementHub, IInstanceManagementHub> mgmtHubContext, Certify.Management.ICertifyManager certifyManager, ILogger<ManagementAPI> log)
        {
            _mgmtStateProvider = mgmtStateProvider;
            _mgmtHubContext = mgmtHubContext;
            _log = log;
            _certifyManager = certifyManager;

        }

        /// <summary>
        /// Flush connections and reconnect all instances.
        /// </summary>
        /// <returns></returns>
        public async Task ReconnectInstances()
        {
            _mgmtStateProvider.Clear();
            //TODO: send command to local instance if present, then send to signalr hub clients
            await _mgmtHubContext.Clients.All.SendCommandRequest(new InstanceCommandRequest(ManagementHubCommands.Reconnect));
        }

        /// <summary>
        /// Sends a command request to the target instance and retrieves the command result.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="cmd">The command request to send.</param>
        /// <returns>An <see cref="InstanceCommandResult"/> that contains the result of the command if available.</returns>
        private async Task<InstanceCommandResult?> GetCommandResult(string instanceId, InstanceCommandRequest cmd)
        {
            if (_certifyManager != null && instanceId == _mgmtStateProvider.GetManagementHubInstanceId())
            {
                // get command result directly from in-process instance
                return await _certifyManager.PerformHubCommandWithResult(cmd);
            }
            else
            {
                var connectionId = _mgmtStateProvider.GetConnectionIdForInstance(instanceId);

                if (connectionId == null)
                {
                    _log.LogError("Instance connection info not known for {instanceId}, cannot send command {cmdId} {cmdType} to instance.", instanceId, cmd.CommandId, cmd.CommandType);
                    return null;
                }

                _mgmtStateProvider.AddAwaitedCommandRequest(cmd);
                await _mgmtHubContext.Clients.Client(connectionId).SendCommandRequest(cmd);
                return await _mgmtStateProvider.ConsumeAwaitedCommandResult(cmd);
            }
        }

        /// <summary>
        /// Sends a command request to the target instance without waiting for a response.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="cmd">The command request to send.</param>
        private async Task SendCommandWithNoResult(string instanceId, InstanceCommandRequest cmd)
        {
            var connectionId = _mgmtStateProvider.GetConnectionIdForInstance(instanceId);

            if (connectionId == null)
            {
                throw new Exception("Instance connection info not known, cannot send commands to instance.");
            }

            if (_certifyManager != null && instanceId == _mgmtStateProvider.GetManagementHubInstanceId())
            {
                // send directly to in-process instance
                await _certifyManager.PerformHubCommandWithResult(cmd);
            }
            else
            {
                await _mgmtHubContext.Clients.Client(connectionId).SendCommandRequest(cmd);
            }
        }

        /// <summary>
        /// Performs an instance command task and returns the result deserialized to the specified type.
        /// </summary>
        /// <typeparam name="T">The type to which the command result should be deserialized.</typeparam>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="args">The key value pair arguments to send with the command.</param>
        /// <param name="commandType">The command type identifier.</param>
        /// <returns>The deserialized result as type <typeparamref name="T"/> if available; otherwise, default.</returns>
        private async Task<T?> PerformInstanceCommandTaskWithResult<T>(string instanceId, KeyValuePair<string, string>[] args, string commandType)
        {
            InstanceCommandResult result;
            var cmd = new InstanceCommandRequest(commandType, args);

            cmd.IsResultAwaited = true;

            result = await GetCommandResult(instanceId, cmd);

            if (result?.Value != null)
            {
                return JsonSerializer.Deserialize<T>(result.Value, Certify.Shared.JsonOptions.DefaultJsonSerializerOptions);
            }
            else
            {
                return default;
            }
        }

        /// <summary>
        /// Fetches managed certificate details from the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="managedCertId">The managed certificate identifier.</param>
        /// <param name="authContext">The authentication context.</param>
        /// <returns>A <see cref="ManagedCertificate"/> if found; otherwise, null.</returns>
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
        /// Issue request for updated status summary from this instance
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="currentAuthContext"></param>
        /// <returns></returns>
        public async Task RefreshManagedCertificateSummary(string instanceId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                 new("instanceId", instanceId)
             };

            var result = await PerformInstanceCommandTaskWithResult<StatusSummary?>(instanceId, args, ManagementHubCommands.GetStatusSummary);
            _mgmtStateProvider.UpdateInstanceStatusSummary(instanceId, result);
        }

        /// <summary>
        /// Exports a managed certificate from the target instance in the specified format.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="managedCertId">The managed certificate identifier.</param>
        /// <param name="format">The export format (e.g. PFX, PEM).</param>
        /// <param name="authContext">The authentication context.</param>
        /// <returns>An <see cref="ActionResult{T}"/> that wraps the exported certificate as a byte array.</returns>
        public async Task<ActionResult<byte[]?>> ExportCertificate(string instanceId, string managedCertId, string format, AuthContext authContext)
        {
            // get managed cert via local api or via management hub

            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId) ,
                        new("managedCertId", managedCertId),
                        new("format", format)
                    };

            return await PerformInstanceCommandTaskWithResult<ActionResult<byte[]?>>(instanceId, args, ManagementHubCommands.ExportCertificate);
        }

        /// <summary>
        /// Adds or updates a managed certificate on the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="managedCert">The managed certificate to add or update.</param>
        /// <param name="authContext">The authentication context.</param>
        /// <returns>The updated <see cref="ManagedCertificate"/> if successful; otherwise, null.</returns>
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
        /// Removes a managed certificate from the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="managedCertId">The managed certificate identifier to remove.</param>
        /// <param name="authContext">The authentication context.</param>
        /// <returns>An <see cref="ActionResult"/> indicating whether the removal was successful.</returns>
        public async Task<ActionResult> RemoveManagedCertificate(string instanceId, string managedCertId, AuthContext authContext)
        {
            // delete managed cert via management hub

            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId) ,
                        new("managedCertId", managedCertId)
                    };

            var result = await PerformInstanceCommandTaskWithResult<ActionResult>(instanceId, args, ManagementHubCommands.RemoveManagedItem);

            if (result.IsSuccess)
            {
                _mgmtStateProvider.DeleteCachedManagedInstanceItem(instanceId, managedCertId);
            }

            return result;
        }

        /// <summary>
        /// Gets a summary of the statuses of all managed certificates.
        /// </summary>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>A <see cref="StatusSummary"/> summarizing certificate statuses.</returns>
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

        /// <summary>
        /// Retrieves the certificate authorities from the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>A collection of <see cref="Models.CertificateAuthority"/> objects, or null if none found.</returns>
        public async Task<ICollection<Models.CertificateAuthority>?> GetCertificateAuthorities(string instanceId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId)
                    };

            return await PerformInstanceCommandTaskWithResult<ICollection<Models.CertificateAuthority>>(instanceId, args, ManagementHubCommands.GetCertificateAuthorities);
        }

        /// <summary>
        /// Updates a certificate authority on the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="certificateAuthority">The certificate authority to update.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>An <see cref="ActionResult"/> indicating whether the update succeeded.</returns>
        public async Task<ActionResult?> UpdateCertificateAuthority(string instanceId, CertificateAuthority certificateAuthority, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId) ,
                        new("certificateAuthority", JsonSerializer.Serialize(certificateAuthority))
                    };

            return await PerformInstanceCommandTaskWithResult<ActionResult?>(instanceId, args, ManagementHubCommands.UpdateCertificateAuthority);
        }

        /// <summary>
        /// Removes a certificate authority from the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="caId">The certificate authority identifier to remove.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>An <see cref="ActionResult"/> indicating whether the removal succeeded.</returns>
        public async Task<ActionResult?> RemoveCertificateAuthority(string instanceId, string caId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId),
                        new("id", caId)
                    };

            return await PerformInstanceCommandTaskWithResult<ActionResult?>(instanceId, args, ManagementHubCommands.RemoveCertificateAuthority);
        }

        /// <summary>
        /// Retrieves ACME accounts from the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>A collection of <see cref="Models.AccountDetails"/> objects, or null if none found.</returns>
        public async Task<ICollection<Models.AccountDetails>?> GetAcmeAccounts(string instanceId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId)
                    };

            return await PerformInstanceCommandTaskWithResult<ICollection<Models.AccountDetails>>(instanceId, args, ManagementHubCommands.GetAcmeAccounts);
        }

        /// <summary>
        /// Adds an ACME account to the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="registration">The contact registration details for the ACME account.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>An <see cref="ActionResult"/> indicating whether the operation succeeded.</returns>
        public async Task<ActionResult?> AddAcmeAccount(string instanceId, ContactRegistration registration, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId) ,
                        new("registration", JsonSerializer.Serialize(registration))
                    };

            return await PerformInstanceCommandTaskWithResult<ActionResult?>(instanceId, args, ManagementHubCommands.AddAcmeAccount);
        }

        /// <summary>
        /// Removes an ACME account from the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="storageKey">The storage key identifying the ACME account.</param>
        /// <param name="deactivate">A boolean indicating whether to deactivate the account.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>An <see cref="ActionResult"/> indicating whether the removal succeeded.</returns>
        public async Task<ActionResult?> RemoveAcmeAccount(string instanceId, string storageKey, bool deactivate, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId),
                        new("storageKey", storageKey),
                        new("deactivate", deactivate.ToString())
                    };

            return await PerformInstanceCommandTaskWithResult<ActionResult?>(instanceId, args, ManagementHubCommands.RemoveAcmeAccount);
        }

        /// <summary>
        /// Retrieves challenge provider definitions from the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>A collection of <see cref="ChallengeProviderDefinition"/> objects, or null if none found.</returns>
        public async Task<ICollection<ChallengeProviderDefinition>?> GetChallengeProviders(string instanceId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId)
                    };
            return await PerformInstanceCommandTaskWithResult<ICollection<ChallengeProviderDefinition>>(instanceId, args, ManagementHubCommands.GetChallengeProviders);
        }

        /// <summary>
        /// Retrieves deployment provider definitions from the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>A collection of <see cref="DeploymentProviderDefinition"/> objects, or null if none found.</returns>
        public async Task<ICollection<DeploymentProviderDefinition>?> GetDeploymentProviders(string instanceId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId)
                    };
            return await PerformInstanceCommandTaskWithResult<ICollection<DeploymentProviderDefinition>>(instanceId, args, ManagementHubCommands.GetDeploymentProviders);
        }

        /// <summary>
        /// Executes a deployment task for the specified managed certificate on the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="managedCertificateId">The managed certificate identifier.</param>
        /// <param name="taskId">The deployment task identifier.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>A collection of <see cref="ActionStep"/> objects representing the deployment steps, or null if none found.</returns>
        public async Task<ICollection<ActionStep>?> ExecuteDeploymentTask(string instanceId, string managedCertificateId, string taskId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId),
                        new("managedCertificateId", managedCertificateId),
                        new("taskId", taskId)
                };

            var result = await PerformInstanceCommandTaskWithResult<ICollection<ActionStep>>(instanceId, args, ManagementHubCommands.ExecuteDeploymentTask);

            // a deployment task may take more time to execute than the SignalR/messaging timeout
            if (result != null)
            {
                return result;
            }
            else
            {
                return new List<ActionStep> { new ActionStep("Task Still Running", "The deployment task is still running and took longer than the default wait time. Check logs for task status.", hasError: false) };
            }
        }

        /// <summary>
        /// Retrieves the DNS zones available for the specified provider and credential on the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="providerTypeId">The provider type identifier.</param>
        /// <param name="credentialId">The credential identifier.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>A collection of <see cref="Models.Providers.DnsZone"/> objects, or null if none found.</returns>
        public async Task<ICollection<Models.Providers.DnsZone>?> GetDnsZones(string instanceId, string providerTypeId, string credentialId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId),
                        new("providerTypeId", providerTypeId),
                        new("credentialId", credentialId)
                    };

            return await PerformInstanceCommandTaskWithResult<ICollection<DnsZone>>(instanceId, args, ManagementHubCommands.GetDnsZones);
        }

        /// <summary>
        /// Retrieves stored credentials from the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>A collection of <see cref="Models.Config.StoredCredential"/> objects, or null if none found.</returns>
        public async Task<ICollection<Models.Config.StoredCredential>?> GetStoredCredentials(string instanceId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId)
                    };

            return await PerformInstanceCommandTaskWithResult<ICollection<StoredCredential>>(instanceId, args, ManagementHubCommands.GetStoredCredentials);
        }

        /// <summary>
        /// Updates a stored credential on the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="item">The stored credential to update.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>An <see cref="ActionResult"/> indicating whether the update succeeded.</returns>
        public async Task<ActionResult?> UpdateStoredCredential(string instanceId, StoredCredential item, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId) ,
                        new("item", JsonSerializer.Serialize(item))
                    };

            return await PerformInstanceCommandTaskWithResult<ActionResult?>(instanceId, args, ManagementHubCommands.UpdateStoredCredential);
        }

        /// <summary>
        /// Removes a stored credential from the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="storageKey">The storage key of the credential to remove.</param>
        /// <param name="authContext">The authentication context.</param>
        /// <returns>An <see cref="ActionResult"/> indicating whether the removal succeeded.</returns>
        public async Task<ActionResult?> RemoveStoredCredential(string instanceId, string storageKey, AuthContext authContext)
        {
            // delete stored credential via management hub

            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId) ,
                        new("storageKey",storageKey)
                    };

            return await PerformInstanceCommandTaskWithResult<ActionResult?>(instanceId, args, ManagementHubCommands.RemoveStoredCredential);
        }

        /// <summary>
        /// Retrieves the log entries of a managed certificate from the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="managedCertId">The managed certificate identifier.</param>
        /// <param name="maxLines">The maximum number of log lines to retrieve.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>An array of <see cref="LogItem"/> objects representing the log entries.</returns>
        public async Task<LogItem[]> GetItemLog(string instanceId, string managedCertId, int maxLines, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId) ,
                        new("managedCertId",managedCertId),
                        new("limit",maxLines.ToString())
                    };

            return await PerformInstanceCommandTaskWithResult<LogItem[]>(instanceId, args, ManagementHubCommands.GetManagedItemLog) ?? [];
        }

        /// <summary>
        /// Tests the configuration of a managed certificate on the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="managedCert">The managed certificate to test.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>A list of status messages detailing the result of the configuration test.</returns>
        internal async Task<List<StatusMessage>> TestManagedCertificateConfiguration(string instanceId, ManagedCertificate managedCert, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId) ,
                        new("managedCert",JsonSerializer.Serialize(managedCert))
                    };

            return await PerformInstanceCommandTaskWithResult<List<StatusMessage>>(instanceId, args, ManagementHubCommands.TestManagedItemConfiguration) ?? [];
        }

        /// <summary>
        /// Retrieves a preview of the renewal actions for a managed certificate.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="managedCert">The managed certificate for which to get preview actions.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>A list of <see cref="ActionStep"/> objects representing the renewal preview actions.</returns>
        internal async Task<List<ActionStep>> GetPreviewActions(string instanceId, ManagedCertificate managedCert, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId) ,
                        new("managedCert",JsonSerializer.Serialize(managedCert))
                    };

            return await PerformInstanceCommandTaskWithResult<List<ActionStep>>(instanceId, args, ManagementHubCommands.GetManagedItemRenewalPreview) ?? [];
        }

        /// <summary>
        /// Performs a certificate request for the specified managed certificate on the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="managedCertId">The managed certificate identifier.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        internal async Task PerformManagedCertificateRequest(string instanceId, string managedCertId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId) ,
                        new("managedCertId",managedCertId)
                    };

            var cmd = new InstanceCommandRequest(ManagementHubCommands.PerformManagedItemRequest, args);

            await SendCommandWithNoResult(instanceId, cmd);
        }

        /// <summary>
        /// Performs an import operation on the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="importRequest">The import request details.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>A list of <see cref="ActionStep"/> objects representing the import process outcomes.</returns>
        internal async Task<List<ActionStep>> PerformInstanceImport(string instanceId, ImportRequest importRequest, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId) ,
                        new("importRequest", JsonSerializer.Serialize(importRequest))
                    };

            return await PerformInstanceCommandTaskWithResult<List<ActionStep>>(instanceId, args, ManagementHubCommands.PerformImport) ?? [];
        }

        /// <summary>
        /// Performs an export operation on the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="exportRequest">The export request details.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>An <see cref="ImportExportPackage"/> containing the exported configuration.</returns>
        internal async Task<Models.Config.Migration.ImportExportPackage?> PerformInstanceExport(string instanceId, ExportRequest exportRequest, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                        new("instanceId", instanceId) ,
                        new("exportRequest", JsonSerializer.Serialize(exportRequest))
                    };

            return await PerformInstanceCommandTaskWithResult<Models.Config.Migration.ImportExportPackage>(instanceId, args, ManagementHubCommands.PerformExport);
        }

        /// <summary>
        /// Retrieves System Status items from the target instance.
        /// </summary>
        /// <param name="instanceId">The target instance identifier.</param>
        /// <param name="currentAuthContext">The current authentication context.</param>
        /// <returns>A collection of <see cref="Models.AccountDetails"/> objects, or null if none found.</returns>
        public async Task<ICollection<Models.ActionStep>?> GetInstanceStatusItems(string instanceId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                     new("instanceId", instanceId)
                 };

            return await PerformInstanceCommandTaskWithResult<ICollection<Models.ActionStep>>(instanceId, args, ManagementHubCommands.GetSystemStatusItems);
        }

        public async Task<ServiceConfig?> GetServiceConfig(string instanceId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                new("instanceId", instanceId)
            };

            return await PerformInstanceCommandTaskWithResult<ServiceConfig>(instanceId, args, ManagementHubCommands.GetServiceConfig);
        }

        public async Task<Preferences?> GetServiceCoreSettings(string instanceId, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
               new("instanceId", instanceId)
            };

            return await PerformInstanceCommandTaskWithResult<Preferences>(instanceId, args, ManagementHubCommands.GetServiceCoreSettings);
        }

        public async Task<ActionResult?> UpdateServiceCoreSettings(string instanceId, Preferences prefs, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                     new("instanceId", instanceId) ,
                     new("prefs", JsonSerializer.Serialize(prefs))
                 };

            return await PerformInstanceCommandTaskWithResult<ActionResult?>(instanceId, args, ManagementHubCommands.UpdateServiceCoreSettings);
        }

        public async Task<ActionResult?> UpdateServiceConfig(string instanceId, ServiceConfig config, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
               new("instanceId", instanceId) ,
               new("config", JsonSerializer.Serialize(config))
           };

            return await PerformInstanceCommandTaskWithResult<ActionResult?>(instanceId, args, ManagementHubCommands.UpdateServiceConfig);
        }
    }
}

