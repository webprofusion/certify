using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Utils;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

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



        private HubConnection connection;

        private string _statusHubUri = "/api/status";

        public CertifyServiceClient(Shared.ServerConnection config = null) : base(config)
        {
            _statusHubUri = $"{(_connectionConfig.UseHTTPS ? "https" : "http")}://{_connectionConfig.Host}:{_connectionConfig.Port}" + _statusHubUri;
        }

        public async Task ConnectStatusStreamAsync()
        {
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

            // TODO: auth: https://docs.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-3.1

            /* connection = new HubConnection(_statusHubUri)
             {
                 Credentials = System.Net.CredentialCache.DefaultCredentials
             };*/

            connection.On<RequestProgressState>("SendProgressState", (s) =>
            {
                OnRequestProgressStateUpdated?.Invoke(s);
            });

            connection.On<ManagedCertificate>("SendManagedCertificateUpdate", (u) =>
            {
                OnManagedCertificateUpdated?.Invoke(u);
            });

            connection.On<string, string>("SendMessage", (a, b) =>
             {
                 OnMessageFromService?.Invoke(a, b);
             });

            await connection.StartAsync();
        }
    }
}
