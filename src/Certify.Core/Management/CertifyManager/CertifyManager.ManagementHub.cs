using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.API.Management;
using Certify.Client;
using Certify.Models;
using Certify.Models.Shared.Validation;
using System.Text.Json;

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
            var numItems = new Random().Next(10, 50);
            for (var i = 0; i < numItems; i++)
            {

                var item = new ManagedCertificate
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = GenerateName(),
                    RequestConfig = new CertRequestConfig
                    {
                        Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig> { new CertRequestChallengeConfig { ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP } }
                    }
                };

                item.DomainOptions.Add(new DomainOption { Domain = $"{item.Name}.dev.projectbids.co.uk", IsManualEntry = true, IsPrimaryDomain = true, IsSelected = true, Type = CertIdentifierType.Dns });
                item.RequestConfig.PrimaryDomain = item.DomainOptions[0].Domain;
                item.RequestConfig.SubjectAlternativeNames = new string[] { item.DomainOptions[0].Domain };

                var validation = CertificateEditorService.Validate(item, null, null, applyAutoConfiguration: true);
                if (validation.IsValid)
                {
                    UpdateManagedCertificate(item);
                }
                else
                {
                    // generated invalid test item
                    System.Diagnostics.Debug.WriteLine(validation.Message);
                }
            }
        }

        private string GenerateName()
        {
            // generate test item names using verb,animal
            var subjects = new string[] {
                "Lion",
                "Tiger",
                "Leopard",
                "Cheetah",
                "Elephant",
                "Giraffe",
                "Rhinoceros",
                "Gorilla"
            };
            var adjectives = new string[] {
                "active",
                "adaptable",
                "alert",
                "clever" ,
                "comfortable" ,
                "conscientious",
                "considerate",
                "courageous" ,
                "decisive",
                "determined" ,
                "diligent" ,
                "energetic",
                "entertaining",
                "enthusiastic" ,
                "fabulous"
            };

            var rnd = new Random();

            return $"{adjectives[rnd.Next(0, adjectives.Length - 1)]}-{subjects[rnd.Next(0, subjects.Length - 1)]}".ToLower();
        }
    }
}
