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
using Microsoft.AspNet.SignalR.Client;
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

        private IHubProxy hubProxy;

        private HubConnection connection;

        private string _statusHubUri = "/api/status";

        public CertifyServiceClient() : base()
        {

            _statusHubUri = $"{(_serviceConfig.UseHTTPS ? "https" : "http")}://{_serviceConfig.Host}:{_serviceConfig.Port}" + _statusHubUri;

        }

        public async Task ConnectStatusStreamAsync()
        {
            connection = new HubConnection(_statusHubUri)
            {
                Credentials = System.Net.CredentialCache.DefaultCredentials
            };
            hubProxy = connection.CreateHubProxy("StatusHub");

            hubProxy.On<ManagedCertificate>("ManagedCertificateUpdated", (u) => OnManagedCertificateUpdated?.Invoke(u));
            hubProxy.On<RequestProgressState>("RequestProgressStateUpdated", (s) => OnRequestProgressStateUpdated?.Invoke(s));
            hubProxy.On<string, string>("SendMessage", (a, b) => OnMessageFromService?.Invoke(a, b));

            connection.Reconnecting += OnConnectionReconnecting;
            connection.Reconnected += OnConnectionReconnected;
            connection.Closed += OnConnectionClosed;

            await connection.Start();
        }
    }
}
