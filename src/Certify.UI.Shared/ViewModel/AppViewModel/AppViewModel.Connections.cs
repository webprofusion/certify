﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Certify.Client;
using Certify.Models;
using Certify.Providers;
using Certify.Shared;
using Certify.Shared.Core.Management;

namespace Certify.UI.ViewModel
{
    public partial class AppViewModel : BindableBase
    {
        /// <summary>
        /// Get the service config (port, host etc)
        /// </summary>
        /// <returns></returns>
        public ServiceConfig GetAppServiceConfig()
        {
            return _configManager.GetServiceConfig();
        }

        /// <summary>
        /// Get the default connection for backend service
        /// </summary>
        /// <param name="configProvider"></param>
        /// <returns></returns>
        public ServerConnection GetDefaultServerConnection(IServiceConfigProvider configProvider)
        {
            var defaultConfig = new ServerConnection(configProvider.GetServiceConfig());

            var connections = ServerConnectionManager.GetServerConnections(Log, defaultConfig);

            if (connections.Any() && connections.Count() == 1)
            {
                ServerConnectionManager.Save(Log, connections);
            }

            return connections.FirstOrDefault(c => c.IsDefault == true);
        }

        /// <summary>
        /// Get list of known server connection
        /// </summary>
        /// <returns></returns>
        public List<ServerConnection> GetServerConnections()
        {

            var defaultConfig = new ServerConnection(GetAppServiceConfig());

            var connections = ServerConnectionManager.GetServerConnections(Log, defaultConfig);

            return connections;
        }

        /// <summary>
        /// UI Message for the current service connection state
        /// </summary>
        public string ConnectionState { get; set; } = "Not Connected";

        /// <summary>
        /// UI title for the current service connection
        /// </summary>
        public string ConnectionTitle
        {
            get
            {
                if (_certifyClient == null)
                {
                    return "(Not Connected)";
                }
                else
                {
                    return $"{_certifyClient?.GetConnectionInfo()}";
                }
            }
        }

        /// <summary>
        /// Perform a connection to the given service
        /// </summary>
        /// <param name="conn">service to connect to</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task ConnectToServer(ServerConnection conn, CancellationToken cancellationToken)
        {
            try
            {
                Mouse.OverrideCursor = System.Windows.Input.Cursors.AppStarting;
                IsLoading = true;

                var connectedOk = await InitServiceConnections(conn, cancellationToken);

                if (connectedOk)
                {
                    await ViewModel.AppViewModel.Current.LoadSettingsAsync();
                }
                else
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        MessageBox.Show("The server connection could not be completed. Check the service is running and that the connection details are correct.");
                    }
                }

                RaisePropertyChangedEvent(nameof(ConnectionTitle));

                IsLoading = false;
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow;
            }
            catch (TaskCanceledException)
            {
                IsLoading = false;
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow;
            }
        }

        /// <summary>
        /// Attempt connection to the given service, or default if none supplied.
        /// </summary>
        /// <param name="conn">If null, default will be used</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> InitServiceConnections(ServerConnection conn, CancellationToken cancellationToken)
        {

            //check service connection
            IsServiceAvailable = false;
            var useInitialConnectionRetry = false;

            ConnectionState = "Connecting...";

            var maxAttempts = 3;

            if (!useInitialConnectionRetry)
            {
                maxAttempts = 1;
            }

            var attemptsRemaining = maxAttempts;

            var clientConnection = _certifyClient;

            while (!IsServiceAvailable && attemptsRemaining > 0 && cancellationToken.IsCancellationRequested != true)
            {
                var connectionConfig = conn ?? GetDefaultServerConnection(_configManager);

                Debug.WriteLine("Attempting connection to management service..");

                if (attemptsRemaining != maxAttempts)
                {
                    Debug.WriteLine("Service not yet available. Waiting a few seconds..");
                    // the service could still be starting up or port may be reallocated
                    var waitMS = (maxAttempts - attemptsRemaining) * 1000;
                    await Task.Delay(waitMS, cancellationToken);
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    // restart client in case port has reallocated
                    clientConnection = new CertifyServiceClient(_configManager, connectionConfig);

                    IsServiceAvailable = await CheckServiceAvailable(clientConnection);

                    if (!IsServiceAvailable)
                    {
                        attemptsRemaining--;

                        // give up
                        if (attemptsRemaining == 0)
                        {
                            ConnectionState = IsServiceAvailable ? "Connected" : "Not Connected";
                            RaisePropertyChangedEvent(nameof(ConnectionState));
                            RaisePropertyChangedEvent(nameof(ConnectionTitle));

                            return false;
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Connected to management service.");
                        _certifyClient = clientConnection;
                    }
                }
            }

            if (cancellationToken.IsCancellationRequested == true || IsServiceAvailable == false)
            {
                ConnectionState = IsServiceAvailable ? "Connected" : "Not Connected";

                RaisePropertyChangedEvent(nameof(ConnectionState));
                RaisePropertyChangedEvent(nameof(ConnectionTitle));

                return false;
            }

            // wire up stream events
            clientConnection.OnMessageFromService += CertifyClient_SendMessage;
            clientConnection.OnRequestProgressStateUpdated += UpdateRequestTrackingProgress;
            clientConnection.OnManagedCertificateUpdated += CertifyClient_OnManagedCertificateUpdated;

            // replace active connection
            _certifyClient = clientConnection;

            // connect to status api stream & handle events
            try
            {
                await clientConnection.ConnectStatusStreamAsync();

            }
            catch (Exception exp)
            {
                // failed to connect to status signalr hub
                Log?.Error($"Failed to connect to status hub: {exp}");

                ConnectionState = IsServiceAvailable ? "Connected" : "Not Connected";

                RaisePropertyChangedEvent(nameof(ConnectionState));
                RaisePropertyChangedEvent(nameof(ConnectionTitle));

                return false;
            }

            ConnectionState = IsServiceAvailable ? "Connected" : "Not Connected";

            RaisePropertyChangedEvent(nameof(ConnectionState));
            RaisePropertyChangedEvent(nameof(ConnectionTitle));

            return true;
        }

        /// <summary>
        /// Checks the service availability by fetching the version. If the service is available but the version is wrong an exception will be raised.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> CheckServiceAvailable(ICertifyClient client)
        {
            string version = null;
            try
            {
                version = await client.GetAppVersion();

                IsServiceAvailable = true;
            }
            catch (Exception exp)
            {
                System.Diagnostics.Debug.WriteLine(exp);

                //service not available
                IsServiceAvailable = false;
            }

            if (version != null)
            {

                // ensure service is correct version
                var v = Version.Parse(version.Replace("\"", ""));

                var assemblyVersion = typeof(ServiceConfig).Assembly.GetName().Version;

                if (v.Major != assemblyVersion.Major)
                {
                    throw new Exception($"Mismatched service version ({v}). Please ensure the old version of the app has been fully uninstalled, then re-install the latest version.");
                }
                else
                {
                    return IsServiceAvailable;
                }
            }
            else
            {
                return IsServiceAvailable;
            }
        }

        /// <summary>
        /// Present service connection chooser UI
        /// </summary>
        /// <param name="parentWindow"></param>
        public void ChooseConnection(System.Windows.DependencyObject parentWindow)
        {
            var d = new Windows.ServerConnections { Owner = System.Windows.Window.GetWindow(parentWindow) };

            d.ShowDialog();
        }

        internal async Task<bool> SaveServerConnection(ServerConnection item)
        {

            var serverConnections = GetServerConnections();

            if (serverConnections.Exists(e => e.Id == item.Id))
            {
                serverConnections.Remove(serverConnections.Find(c => c.Id == item.Id));
            }

            // if item is the default, all other items are no longer the default
            if (item.IsDefault)
            {
                serverConnections
                    .Where(s => s.Id != item.Id)
                    .ToList()
                    .ForEach(s => s.IsDefault = false);
            }

            serverConnections.Add(item);

            // if no default exists, make the first item default
            if (!serverConnections.Exists(e => e.IsDefault))
            {
                serverConnections.First().IsDefault = true;
            }

            ServerConnectionManager.Save(Log, serverConnections);

            return await Task.FromResult(true);
        }
    }
}
