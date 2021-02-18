using System;
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
                _legacyConnection = new Microsoft.AspNet.SignalR.Client.HubConnection(_statusHubUri);
                _legacyConnection.Credentials = System.Net.CredentialCache.DefaultCredentials;

                var hubProxy = _legacyConnection.CreateHubProxy("StatusHub");

                hubProxy.On<ManagedCertificate>(Providers.StatusHubMessages.SendManagedCertificateUpdateMsg, (u) => OnManagedCertificateUpdated?.Invoke(u));
                hubProxy.On<RequestProgressState>(Providers.StatusHubMessages.SendProgressStateMsg, (s) => OnRequestProgressStateUpdated?.Invoke(s));
                hubProxy.On<string, string>(Providers.StatusHubMessages.SendMsg, (a, b) => OnMessageFromService?.Invoke(a, b));

                _legacyConnection.Reconnecting += OnConnectionReconnecting;
                _legacyConnection.Reconnected += OnConnectionReconnected;
                _legacyConnection.Closed += OnConnectionClosed;

                await _legacyConnection.Start();

            }
            else
            {
                // newer signalr client/server

                // TODO: auth: https://docs.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-3.1

                connection = new HubConnectionBuilder()
                .WithUrl(_statusHubUri)
                .WithAutomaticReconnect()
                .AddMessagePackProtocol()
                .Build();

                connection.Closed += async (error) =>
                {
                    await Task.Delay(new Random().Next(0, 5) * 1000);
                    await connection.StartAsync();
                };

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
            }
        }

        public ServerConnection GetConnectionInfo()
        {
            return _connectionConfig;
        }
    }
}
