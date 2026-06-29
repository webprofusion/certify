using System;
using System.Threading.Tasks;
using Certify.Models.Hub;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Certify.Client
{
    /// <summary>
    /// Implements hub communication with a central management server
    /// </summary>
    public class ManagementServerClient : IManagementServerClient
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

        private void Log(string msg)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTimeOffset.UtcNow.ToString("HH:mm:ss")} INF] {msg}");
        }

        public bool IsConnected()
        {
            if (_connection == null || _connection?.State == HubConnectionState.Disconnected)
            {
                return false;
            }

            return true;
        }

        public async Task ConnectAsync(string hubConnectionAuthToken)
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

                opts.UseStatefulReconnect = true;
                opts.AccessTokenProvider = () => Task.FromResult(hubConnectionAuthToken ?? "");

            })
            .WithAutomaticReconnect()
            .AddMessagePackProtocol()
            .Build();

            _connection.On<InstanceCommandRequest>(ManagementHubMessages.SendCommandRequest, PerformRequestedCommand);

            // Wire up connection lifecycle events
            _connection.Reconnecting += (error) =>
            {
                Log($"[ManagementServerClient] Reconnecting to hub. Error: {error?.Message}");
                OnConnectionReconnecting?.Invoke();
                return Task.CompletedTask;
            };

            _connection.Reconnected += (connectionId) =>
            {
                Log($"[ManagementServerClient] Reconnected to hub. ConnectionId: {connectionId}");
                OnConnectionReconnected?.Invoke();
                return Task.CompletedTask;
            };

            _connection.Closed += async (error) =>
            {
                Log($"[ManagementServerClient] Connection closed. Error: {error?.Message}");

                // rely on delegate to organize reconnect
                OnConnectionClosed?.Invoke();
            };

            await _connection.StartAsync();
        }

        public async Task Disconnect()
        {
            if (_connection != null)
            {
                await _connection.StopAsync();
            }
        }

        private async Task PerformRequestedCommand(InstanceCommandRequest cmd)
        {
            if (_connection.State != HubConnectionState.Connected)
            {
                Log($"[ManagementServerClient.PerformRequestedCommand] Not Connected [{_connection?.State}], cannot send command. {cmd.CommandId} {cmd.CommandType}");
                return;
            }

            try
            {
                Log($"[ManagementServerClient.PerformRequestedCommand] Got command from management server {cmd.CommandId} {cmd.CommandType}");

                var resultTask = OnGetCommandResult?.Invoke(cmd);

                if (resultTask == null)
                {
                    return;
                }

                var result = await resultTask;

                // Reconnect commands re-establish the connection and do not return a response
                if (cmd.CommandType == ManagementHubCommands.Reconnect)
                {
                    return;
                }

                if (result == null)
                {
                    Log($"[ManagementServerClient.PerformRequestedCommand] No result produced for command {cmd.CommandId} {cmd.CommandType}");
                    return;
                }

                result.IsCommandResponse = true;
                result.CommandType = cmd.CommandType;
                result.CommandId = cmd.CommandId;

                await _connection.SendAsync(ManagementHubMessages.ReceiveCommandResult, result);
            }
            catch (Exception ex)
            {
                Log($"[ManagementServerClient.PerformRequestedCommand] Error handling command {cmd.CommandId} {cmd.CommandType}: {ex.Message}");
            }
        }

        /// <summary>
        /// Send instance info back to the management hub
        /// </summary>
        /// <param name="commandId">Unique ID for this command, New Guid if command is not a response</param>
        /// <param name="isCommandResponse">If false, message is not being sent in response to an existing query </param>
        public void SendInstanceInfo(Guid commandId, bool isCommandResponse = true)
        {
            try
            {
                if (_connection?.State != HubConnectionState.Connected)
                {
                    Log($"[ManagementServerClient] Cannot send instance info - not connected (State: {_connection?.State})");
                    return;
                }

                // send this clients instance ID back to the hub to identify it in the connection: should send a shared secret before this to confirm this client knows and is not impersonating another instance
                var result = new InstanceCommandResult
                {
                    CommandId = commandId,
                    CommandType = ManagementHubCommands.GetInstanceInfo,
                    Value = System.Text.Json.JsonSerializer.Serialize(_instanceInfo),
                    IsCommandResponse = isCommandResponse
                };

                result.ObjectValue = _instanceInfo;
                _ = _connection.SendAsync(ManagementHubMessages.ReceiveCommandResult, result);
            }
            catch (Exception ex)
            {
                Log($"[ManagementServerClient] Error sending instance info: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Send mgmt hub a general notification message to be actioned
        /// </summary>
        public void SendNotificationToManagementHub(string msgCommandType, object updateMsg)
        {
            try
            {
                if (_connection?.State != HubConnectionState.Connected)
                {
                    Log($"[ManagementServerClient] Cannot send notification - not connected (State: {_connection?.State})");
                    return;
                }

                var result = new InstanceCommandResult
                {
                    CommandId = Guid.NewGuid(),
                    InstanceId = _instanceInfo.InstanceId,
                    CommandType = msgCommandType,
                    Value = System.Text.Json.JsonSerializer.Serialize(updateMsg),
                    ObjectValue = updateMsg,
                    IsCommandResponse = false
                };

                result.ObjectValue = updateMsg;
                _ = _connection.SendAsync(ManagementHubMessages.ReceiveCommandResult, result);
            }
            catch (Exception ex)
            {
                Log($"[ManagementServerClient] Error sending notification ({msgCommandType}): {ex.Message}");
                throw;
            }
        }

        public void UpdateCachedInstanceInfo(ManagedInstanceInfo instanceInfo)
        {
            _instanceInfo = instanceInfo;
        }
    }
}
