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
        public event Func<InstanceCommandRequest, Task<InstanceCommandResult>> OnGetCommandResult;

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

        public async Task Disconnect()
        {
            await _connection.StopAsync();

        }
        private void PerformRequestedCommand(InstanceCommandRequest cmd)
        {
            System.Diagnostics.Debug.WriteLine($"Got command from management server {cmd}");

            if (cmd.CommandType == ManagementHubCommands.GetInstanceInfo)
            {
                SendInstanceInfo(cmd.CommandId);
            }
            else
            {
                var task = OnGetCommandResult?.Invoke(cmd);
                if (task != null)
                {
                    if (cmd.CommandType != ManagementHubCommands.Reconnect)
                    {
                        _connection.SendAsync(ManagementHubMessages.ReceiveCommandResult, task.Result).Wait();
                    }
                    else
                    {
                        task.Wait();
                    }
                }
            }
        }

        /// <summary>
        /// Send instance info back to the management hub
        /// </summary>
        /// <param name="commandId">Unique ID for this command, New Guid if command is not a response</param>
        /// <param name="isCommandResponse">If false, message is not being sent in response to an existing query </param>
        public void SendInstanceInfo(Guid commandId, bool isCommandResponse = true)
        {
            // send this clients instance ID back to the hub to identify it in the connection: should send a shared secret before this to confirm this client knows and is not impersonating another instance
            var result = new InstanceCommandResult
            {
                CommandId = commandId,
                CommandType = ManagementHubCommands.GetInstanceInfo,
                Value = System.Text.Json.JsonSerializer.Serialize(_instanceInfo),
                IsCommandResponse = isCommandResponse
            };

            result.ObjectValue = _instanceInfo;
            _connection.SendAsync(ManagementHubMessages.ReceiveCommandResult, result);
        }
    }
}
