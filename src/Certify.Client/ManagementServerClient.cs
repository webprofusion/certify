using System;
using System.Threading.Tasks;
using Certify.API.Management;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Certify.Client
{
    /// <summary>
    /// Implements hub communication with a central management server
    /// </summary>
    public class ManagementServerClient
    {

        public event Action OnConnectionReconnecting;

        public event Action OnConnectionReconnected;

        public event Action OnConnectionClosed;

        public event Func<ManagedInstanceItems> OnGetInstanceItems;

        private HubConnection _connection;

        private string _hubUri = "";

        private ManagedInstanceInfo _instanceInfo;

        public ManagementServerClient(string hubUri, ManagedInstanceInfo instanceInfo)
        {
            _hubUri = $"{hubUri}";
            _instanceInfo = instanceInfo;
        }

        public bool IsConnected()
        {
            if (_connection == null || _connection?.State == HubConnectionState.Disconnected)
            {
                return false;
            }

            return true;
        }

        public async Task ConnectAsync()
        {
            var allowUntrusted = true;

            _connection = new HubConnectionBuilder()

            .WithUrl(_hubUri, opts =>
            {
                opts.HttpMessageHandlerFactory = (message) =>
                {
                    if (message is System.Net.Http.HttpClientHandler clientHandler)
                    {
                        if (allowUntrusted)
                        {
                            // allow invalid/untrusted tls cert
                            clientHandler.ServerCertificateCustomValidationCallback +=
                                (sender, certificate, chain, sslPolicyErrors) => true;
                        }
                    }

                    return message;
                };
            })
            .WithAutomaticReconnect()
            .AddMessagePackProtocol()
            .Build();

            _connection.On(ManagementHubMessages.SendCommandRequest, (Action<InstanceCommandRequest>)((s) =>
            {
                PerformRequestedCommand(s);
            }));

            await _connection.StartAsync();

            _connection.Closed += async (error) =>
            {
                await Task.Delay(new Random().Next(0, 5) * 1000);
                await _connection.StartAsync();
            };

        }

        private void PerformRequestedCommand(InstanceCommandRequest s)
        {
            System.Diagnostics.Debug.WriteLine($"Got command from management server {s}");

            if (s.CommandType == ManagementHubCommands.GetInstanceInfo)
            {
                // send this clients instance ID back to the hub to identify it in the connection: should send a shared secret before this to confirm this client knows and is not impersonating another instance
                var result = new InstanceCommandResult { CommandId = s.CommandId, Value = System.Text.Json.JsonSerializer.Serialize(_instanceInfo) };
                result.ObjectValue = _instanceInfo;
                _connection.SendAsync(ManagementHubMessages.ReceiveCommandResult, result);
            }

            if (s.CommandType == ManagementHubCommands.GetInstanceItems)
            {
                var items = OnGetInstanceItems?.Invoke() ?? new ManagedInstanceItems { };

                var result = new InstanceCommandResult { CommandId = s.CommandId, Value = System.Text.Json.JsonSerializer.Serialize(items) };
                result.ObjectValue = items;
                _connection.SendAsync(ManagementHubMessages.ReceiveCommandResult, result);
            }
        }
    }
}
