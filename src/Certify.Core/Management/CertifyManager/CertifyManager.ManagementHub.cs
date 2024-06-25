using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Certify.API.Management;
using Certify.Client;
using Certify.Models;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        private ManagementServerClient _managementServerClient;
        private string _managementServerConnectionId = string.Empty;

        private async Task StartManagementHubConnection(string hubUri)
        {

            _serviceLog.Debug("Attempting connection to management hub {hubUri}", hubUri);

            var instanceInfo = new ManagedInstanceInfo
            {
                InstanceId = $"{CoreAppSettings.Current.InstanceId}_{Environment.MachineName}",

                Title = Environment.MachineName
            };

            if (_managementServerClient != null)
            {
                _managementServerClient.OnGetInstanceItems -= _managementServerClient_OnGetInstanceItems;
            }

            _managementServerClient = new ManagementServerClient(hubUri, instanceInfo);

            try
            {
                await _managementServerClient.ConnectAsync();

                _managementServerClient.OnGetInstanceItems += _managementServerClient_OnGetInstanceItems;
                _managementServerClient.OnConnectionReconnecting += _managementServerClient_OnConnectionReconnecting;
            }
            catch (Exception ex)
            {
                _serviceLog.Error(ex, "Failed to create connection to management hub {hubUri}", hubUri);

                _managementServerClient = null;
            }
        }

        private void _managementServerClient_OnConnectionReconnecting()
        {
            _serviceLog.Warning("Reconnecting to Management.");
        }

        private ManagedInstanceItems _managementServerClient_OnGetInstanceItems()
        {
            var instanceItems = new ManagedInstanceItems();
            instanceItems.InstanceId = CoreAppSettings.Current.InstanceId;
            instanceItems.Items = GetManagedCertificates(new ManagedCertificateFilter { }).Result;
            foreach (var item in instanceItems.Items)
            {
                item.Name = "[Agent] " + item.Name;
            }

            return instanceItems;
        }

        private void GenerateDemoItems()
        {
            var numItems = new Random().Next(10, 50);
            for (var i = 0; i < numItems; i++)
            {
                var item = new ManagedCertificate
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = GenerateName()
                };

                UpdateManagedCertificate(item);
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
