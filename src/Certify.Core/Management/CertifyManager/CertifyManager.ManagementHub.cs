using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.API.Management;
using Certify.Client;
using Certify.Models;
using Certify.Models.Shared.Validation;
using System.Text.Json;
using Certify.Shared.Core.Utils;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        private ManagementServerClient _managementServerClient;
        private string _managementServerConnectionId = string.Empty;
        private void SendHeartbeatToManagementHub()
        {
            _managementServerClient.SendInstanceInfo(Guid.NewGuid(), false);
        }
        private async Task StartManagementHubConnection(string hubUri)
        {

            _serviceLog.Debug("Attempting connection to management hub {hubUri}", hubUri);

            var instanceInfo = new ManagedInstanceInfo
            {
                InstanceId = $"{this.InstanceId}",
                Title = Environment.MachineName
            };

            if (_managementServerClient != null)
            {
                _managementServerClient.OnGetCommandResult -= _managementServerClient_OnGetCommandResult;
                _managementServerClient.OnConnectionReconnecting -= _managementServerClient_OnConnectionReconnecting;
            }

            _managementServerClient = new ManagementServerClient(hubUri, instanceInfo);

            try
            {
                await _managementServerClient.ConnectAsync();

                _managementServerClient.OnGetCommandResult += _managementServerClient_OnGetCommandResult;
                _managementServerClient.OnConnectionReconnecting += _managementServerClient_OnConnectionReconnecting;
            }
            catch (Exception ex)
            {
                _serviceLog.Error(ex, "Failed to create connection to management hub {hubUri}", hubUri);

                _managementServerClient = null;
            }
        }

        private async Task<InstanceCommandResult> _managementServerClient_OnGetCommandResult(InstanceCommandRequest arg)
        {
            object val = null;

            if (arg.CommandType == ManagementHubCommands.GetInstanceManagedItem)
            {
                // Get a single managed item by id
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var managedCertIdArg = args.FirstOrDefault(a => a.Key == "managedCertId");
                val = await GetManagedCertificate(managedCertIdArg.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.GetInstanceManagedItems)
            {
                // Get all managed items
                var items = await GetManagedCertificates(new ManagedCertificateFilter { });
                val = new ManagedInstanceItems { InstanceId = InstanceId, Items = items };
            }
            else if (arg.CommandType == ManagementHubCommands.UpdateInstanceManagedItem)
            {
                // update a single managed item 
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var managedCertArg = args.FirstOrDefault(a => a.Key == "managedCert");
                var managedCertObj = JsonSerializer.Deserialize<ManagedCertificate>(managedCertArg.Value);
                val = await UpdateManagedCertificate(managedCertObj);
            }
            else if (arg.CommandType == ManagementHubCommands.DeleteInstanceManagedItem)
            {
                // delete a single managed item 
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var managedCertIdArg = args.FirstOrDefault(a => a.Key == "managedCertId");
                await DeleteManagedCertificate(managedCertIdArg.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.TestInstanceManagedItem)
            {
                // test challenge response config for a single managed item 
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var managedCertArg = args.FirstOrDefault(a => a.Key == "managedCert");
                var managedCertObj = JsonSerializer.Deserialize<ManagedCertificate>(managedCertArg.Value);
                await TestChallenge(null, managedCertObj, isPreviewMode: true);
            }

            var result = new InstanceCommandResult { CommandId = arg.CommandId, Value = JsonSerializer.Serialize(val) };

            result.ObjectValue = val;

            return result;
        }

        private void _managementServerClient_OnConnectionReconnecting()
        {
            _serviceLog.Warning("Reconnecting to Management.");
        }

        private void GenerateDemoItems()
        {
            var items = DemoDataGenerator.GenerateDemoItems();
            foreach (var item in items)
            {
                _ = UpdateManagedCertificate(item);
            }
        }
    }
}
