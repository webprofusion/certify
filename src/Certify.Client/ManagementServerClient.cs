using System;
using System.Threading;
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
        private readonly SemaphoreSlim _connectionSync = new SemaphoreSlim(1, 1);

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

        public async Task ConnectAsync(Func<CancellationToken, Task<string>> hubConnectionTokenFactory)
        {
            if (hubConnectionTokenFactory == null)
            {
                throw new ArgumentNullException(nameof(hubConnectionTokenFactory));
            }

            await _connectionSync.WaitAsync();

            try
            {
                if (_connection?.State == HubConnectionState.Connected
                    || _connection?.State == HubConnectionState.Connecting
                    || _connection?.State == HubConnectionState.Reconnecting)
                {
                    return;
                }

                // discard any previous connection (e.g. one closed by the hub) before replacing it
                await DisposeCurrentConnection();

                var allowUntrusted = true;

                var connection = new HubConnectionBuilder()

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

                    // called for each connect and reconnect attempt, so an expired token is replaced with a fresh one
                    // rather than the same stale token being presented until the hub rejects the connection
                    opts.AccessTokenProvider = async () => await hubConnectionTokenFactory(CancellationToken.None) ?? "";

                })
                .WithAutomaticReconnect()
                .AddMessagePackProtocol()
                .Build();

                connection.On<InstanceCommandRequest>(ManagementHubMessages.SendCommandRequest, PerformRequestedCommand);

                // Wire up connection lifecycle events
                connection.Reconnecting += (error) =>
                {
                    Log($"[ManagementServerClient] Reconnecting to hub. Error: {error?.Message}");
                    OnConnectionReconnecting?.Invoke();
                    return Task.CompletedTask;
                };

                connection.Reconnected += (connectionId) =>
                {
                    Log($"[ManagementServerClient] Reconnected to hub. ConnectionId: {connectionId}");
                    OnConnectionReconnected?.Invoke();
                    return Task.CompletedTask;
                };

                connection.Closed += async (error) =>
                {
                    Log($"[ManagementServerClient] Connection closed. Error: {error?.Message}");

                    // rely on delegate to organize reconnect
                    OnConnectionClosed?.Invoke();
                };

                try
                {
                    await connection.StartAsync();
                }
                catch
                {
                    // the handshake can fail (e.g. an unauthorized token is rejected with a 401), don't leak the unusable connection
                    await connection.DisposeAsync();
                    throw;
                }

                _connection = connection;
            }
            finally
            {
                _connectionSync.Release();
            }
        }

        public async Task Disconnect()
        {
            await _connectionSync.WaitAsync();

            try
            {
                await DisposeCurrentConnection();
            }
            finally
            {
                _connectionSync.Release();
            }
        }

        /// <summary>
        /// Stop and dispose the current connection, if any. Caller must hold <see cref="_connectionSync"/>.
        /// </summary>
        private async Task DisposeCurrentConnection()
        {
            var connection = _connection;

            if (connection == null)
            {
                return;
            }

            // clear the field first so in-flight command handlers see there is no usable connection rather than a disposed one
            _connection = null;

            try
            {
                await connection.StopAsync();
            }
            catch (Exception ex)
            {
                Log($"[ManagementServerClient] Error stopping hub connection: {ex.Message}");
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        private async Task PerformRequestedCommand(InstanceCommandRequest cmd)
        {
            if (_connection?.State != HubConnectionState.Connected)
            {
                Log($"[ManagementServerClient.PerformRequestedCommand] Not Connected [{_connection?.State}], cannot send command. {cmd.CommandId} {cmd.CommandType}");
                return;
            }

            try
            {
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

                // the handler itself may have torn down the connection (e.g. a re-authentication command), so re-check before replying
                var connection = _connection;
                if (connection?.State != HubConnectionState.Connected)
                {
                    Log($"[ManagementServerClient.PerformRequestedCommand] Connection no longer active [{connection?.State}], dropping result for {cmd.CommandId} {cmd.CommandType}");
                    return;
                }

                await connection.SendAsync(ManagementHubMessages.ReceiveCommandResult, result);
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
                var connection = _connection;

                if (connection?.State != HubConnectionState.Connected)
                {
                    Log($"[ManagementServerClient] Cannot send instance info - not connected (State: {connection?.State})");
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
                _ = connection.SendAsync(ManagementHubMessages.ReceiveCommandResult, result);
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
                var connection = _connection;

                if (connection?.State != HubConnectionState.Connected)
                {
                    Log($"[ManagementServerClient] Cannot send notification - not connected (State: {connection?.State})");
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
                _ = connection.SendAsync(ManagementHubMessages.ReceiveCommandResult, result);
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
