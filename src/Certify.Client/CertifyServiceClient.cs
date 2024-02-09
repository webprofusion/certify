using System;
using System.IO;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Shared;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Certify.Client
{

    // This version of the client communicates with the Certify.Service instance on the local machine
    public class CertifyServiceClient : CertifyApiClient, ICertifyClient
    {

        public event Action<RequestProgressState> OnRequestProgressStateUpdated;

        public event Action<ManagedCertificate> OnManagedCertificateUpdated;

        public event Action<string, string> OnMessageFromService;

        public event Action OnConnectionReconnecting;

        public event Action OnConnectionReconnected;

        public event Action OnConnectionClosed;

        private Microsoft.AspNetCore.SignalR.Client.HubConnection connection;

        private Microsoft.AspNet.SignalR.Client.HubConnection _legacyConnection;

        private string _statusHubUri = "/api/status";

        public CertifyServiceClient(Providers.IServiceConfigProvider configProvider, Shared.ServerConnection config = null) : base(configProvider, config)
        {
            _statusHubUri = $"{(_connectionConfig.UseHTTPS ? "https" : "http")}://{_connectionConfig.Host}:{_connectionConfig.Port}" + _statusHubUri;
        }

        public async Task ConnectStatusStreamAsync()
        {
            if (_connectionConfig.ServerMode == "v1")
            {
                // older signalr client/server
                _legacyConnection = new Microsoft.AspNet.SignalR.Client.HubConnection(_statusHubUri)
                {
                    Credentials = System.Net.CredentialCache.DefaultCredentials
                };

                var hubProxy = _legacyConnection.CreateHubProxy("StatusHub");

                hubProxy.On<ManagedCertificate>(Providers.StatusHubMessages.SendManagedCertificateUpdateMsg, (u) => OnManagedCertificateUpdated?.Invoke(u));
                hubProxy.On<RequestProgressState>(Providers.StatusHubMessages.SendProgressStateMsg, (s) => OnRequestProgressStateUpdated?.Invoke(s));
                hubProxy.On<string, string>(Providers.StatusHubMessages.SendMsg, (a, b) => OnMessageFromService?.Invoke(a, b));

                _legacyConnection.Reconnecting += OnConnectionReconnecting;
                _legacyConnection.Reconnected += OnConnectionReconnected;
                _legacyConnection.Closed += OnConnectionClosed;

#if DEBUG
                var logPath = Path.Combine(EnvironmentUtil.GetAppDataFolder("logs"), "hubconnection.log");
                var writer = new StreamWriter(logPath);
                writer.AutoFlush = true;
                _legacyConnection.TraceLevel = TraceLevels.All;
                _legacyConnection.TraceWriter = writer;
#endif

                await _legacyConnection.Start();

            }
            else
            {
                // newer signalr client/server

                // TODO: auth: https://docs.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-3.1

                connection = new HubConnectionBuilder()

                .WithUrl(_statusHubUri, opts =>
                {
                    opts.HttpMessageHandlerFactory = (message) =>
                    {
                        if (message is System.Net.Http.HttpClientHandler clientHandler)
                        {
                            if (_connectionConfig.AllowUntrusted)
                            {
                                // allow invalid tls cert
                                clientHandler.ServerCertificateCustomValidationCallback +=
                                    (sender, certificate, chain, sslPolicyErrors) => { return true; };
                            }
                        }

                        return message;
                    };
                })
                .WithAutomaticReconnect()
                .AddMessagePackProtocol()
                .Build();

                connection.On<RequestProgressState>(Providers.StatusHubMessages.SendProgressStateMsg, (s) =>
                {
                    OnRequestProgressStateUpdated?.Invoke(s);
                });

                connection.On<ManagedCertificate>(Providers.StatusHubMessages.SendManagedCertificateUpdateMsg, (u) =>
                {
                    OnManagedCertificateUpdated?.Invoke(u);
                });

                connection.On<string, string>(Providers.StatusHubMessages.SendMsg, (a, b) =>
                {
                    OnMessageFromService?.Invoke(a, b);
                });

                await connection.StartAsync();

                connection.Closed += async (error) =>
                {
                    await Task.Delay(new Random().Next(0, 5) * 1000);
                    await connection.StartAsync();
                };
            }
        }

        public async Task<bool> EnsureServiceHubConnected()
        {
            var isConnected = false;
            if (connection != null)
            {
                isConnected = connection.State == HubConnectionState.Connected || connection.State == HubConnectionState.Reconnecting;
            }
            else if (_legacyConnection != null)
            {
                isConnected = _legacyConnection.State == ConnectionState.Connected || _legacyConnection.State == ConnectionState.Reconnecting;
            }

            if (!isConnected)
            {
                try
                {
                    await ConnectStatusStreamAsync();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                return true;
            }
        }

        public ServerConnection GetConnectionInfo()
        {
            return _connectionConfig;
        }

        public string GetStatusHubUri()
        {
            return _statusHubUri;
        }
    }
}
