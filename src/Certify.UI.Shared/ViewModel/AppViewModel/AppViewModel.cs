using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Certify.Client;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Models.Reporting;
using Certify.Providers;
using Certify.SharedUtils;
using Certify.UI.Shared;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Certify.UI.ViewModel
{
    public partial class AppViewModel : BindableBase
    {
        /// <summary>
        /// Provide single static instance of model for all consumers 
        /// </summary>        
        public static AppViewModel Current = GetModel();

        /// <summary>
        /// Provider for service connection configuration info
        /// </summary>
        private IServiceConfigProvider _configManager;

        public AppViewModel()
        {
            _configManager = new ServiceConfigManager();

            Init();
        }

        public AppViewModel(ICertifyClient certifyClient)
        {
            _certifyClient = certifyClient;

            _configManager = new ServiceConfigManager();

            Init();
        }

        /// <summary>
        /// Get app model (real mode or design mode depending on context)
        /// </summary>
        /// <returns></returns>
        public static AppViewModel GetModel()
        {
            if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
            {
                return new AppViewModelDesign();
            }
            else
            {
                return new AppViewModel();
            }
        }

        /// <summary>
        /// Setup app model
        /// </summary>
        private void Init()
        {

            var serilogLog = new Serilog.LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Verbose()
                .WriteTo.File(Path.Combine(EnvironmentUtil.EnsuredAppDataPath("logs"), "ui.log"), shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10))
                .CreateLogger();

            Log = new Loggy(new Serilog.Extensions.Logging.SerilogLoggerFactory(serilogLog).CreateLogger<AppViewModel>());

            ProgressResults = new ObservableCollection<RequestProgressState>();

            ImportedManagedCertificates = new ObservableCollection<ManagedCertificate>();
            ManagedCertificates = new ObservableCollection<ManagedCertificate>();

            StartProgressCleanupTask();

        }

        /// <summary>
        /// Continuous clean of success items, this is because if the UI is left open it will continue to receive messages
        /// </summary>
        public void StartProgressCleanupTask()
        {
            var progressCleanup = new Task(async () =>
            {
                try
                {
                    var waitMS = 5 * 60 * 1000; // cleanup all old progress messages every 5 mins
                    while (true)
                    {
                        await Task.Delay(waitMS);
                        var now = DateTimeOffset.UtcNow;
                        var items = ProgressResults.Where(p => p.MessageCreated < DateTimeOffset.UtcNow.AddMilliseconds(-waitMS));

                        if (items.Any())
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                foreach (var item in items.ToList())
                                {
                                    ProgressResults.Remove(item);
                                }
                            });
                        }
                    }
                }
                catch (Exception exp)
                {
                    Log?.Error("ProgressCleanupTask: " + exp.ToString());
                }
            }, TaskCreationOptions.LongRunning);

            progressCleanup.Start();
        }
        /// <summary>
        /// Log for general app events
        /// </summary>
        public ILog Log { get; private set; }

        /// <summary>
        /// Internal product reference for registration etc
        /// </summary>
        public const int ProductTypeId = 1;

        /// <summary>
        /// internal client for the current background service connection
        /// </summary>
        internal ICertifyClient _certifyClient;

        protected bool TryGetAvailableCertifyClient(out ICertifyClient client)
        {
            client = _certifyClient;

            return client != null && IsServiceAvailable;
        }

        public string CurrentError { get; set; }
        public bool IsError { get; set; }

        /// <summary>
        /// If true, service connection is in progress
        /// </summary>
        public bool IsLoading { get; set; } = true;

        /// <summary>
        /// If true, service is connected
        /// </summary>
        public bool IsServiceAvailable { get; set; }

        /// <summary>
        /// If true, app update is currently downloading
        /// </summary>
        public bool IsUpdateInProgress { get; set; }

        /// <summary>
        /// General exception handling
        /// </summary>
        /// <param name="exp"></param>
        public void RaiseError(Exception exp)
        {
            IsError = true;
            CurrentError = exp.Message;

            SystemDiagnosticError = "An error occurred. Persistent errors should be reported to Certify The Web support: " + exp.Message;
        }

        /// <summary>
        /// UI message describing a current system diagnostics warning (low disk space etc)
        /// </summary>
        public string SystemDiagnosticWarning { get; set; }

        /// <summary>
        /// UI message describing a current system diagnostics error (no disk space etc)
        /// </summary>
        public string SystemDiagnosticError { get; set; }

        private DataStoreStatus _dataStoreStatus;
        /// <summary>
        /// Current data store connection status
        /// </summary>
        public DataStoreStatus DataStoreStatus
        {
            get => _dataStoreStatus;
            set
            {
                _dataStoreStatus = value;
                RaisePropertyChangedEvent(nameof(DataStoreStatus));
                RaisePropertyChangedEvent(nameof(IsInDegradedMode));
                RaisePropertyChangedEvent(nameof(DegradedModeMessage));
            }
        }

        /// <summary>
        /// Returns true if the service is running in degraded mode
        /// </summary>
        public bool IsInDegradedMode => DataStoreStatus?.IsDegradedMode == true;

        /// <summary>
        /// Message to display when in degraded mode
        /// </summary>
        public string DegradedModeMessage => DataStoreStatus?.LastErrorMessage ?? "Data store connection failed.";

        private bool _isReconnecting;
        /// <summary>
        /// If true, a reconnection attempt is in progress
        /// </summary>
        public bool IsReconnecting
        {
            get => _isReconnecting;
            set
            {
                _isReconnecting = value;
                RaisePropertyChangedEvent(nameof(IsReconnecting));
            }
        }

        /// <summary>
        /// Fetch and update the data store status from the service
        /// </summary>
        public async Task RefreshDataStoreStatus()
        {
            if (!TryGetAvailableCertifyClient(out var client))
            {
                return;
            }

            try
            {
                DataStoreStatus = await client.GetDataStoreStatus();
            }
            catch (Exception ex)
            {
                Log?.Error($"Failed to get data store status: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempt to reconnect to the data store
        /// </summary>
        public async Task<ActionResult> AttemptDataStoreReconnection()
        {
            if (!TryGetAvailableCertifyClient(out var client))
            {
                return new ActionResult("Service is not connected.", false);
            }

            if (IsReconnecting)
            {
                return new ActionResult("Reconnection already in progress.", false);
            }

            IsReconnecting = true;

            try
            {
                var result = await client.AttemptDataStoreReconnection();

                // Refresh the status regardless of result
                await RefreshDataStoreStatus();

                return result;
            }
            catch (Exception ex)
            {
                Log?.Error($"Failed to reconnect to data store: {ex.Message}");
                return new ActionResult($"Failed to reconnect: {ex.Message}", false);
            }
            finally
            {
                IsReconnecting = false;
            }
        }

        public async Task<List<ActionResult>> PerformServiceDiagnostics()
        {
            if (!TryGetAvailableCertifyClient(out var client))
            {
                return [];
            }

            return await client.PerformServiceDiagnostics();
        }

        /// <summary>
        /// Show UI message if sent from service (unused)
        /// </summary>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        private void CertifyClient_SendMessage(string arg1, string arg2) => MessageBox.Show($"Received: {arg1} {arg2}");

        public Application GetApplication() => System.Windows.Application.Current;

        /// <summary>
        /// Display a temporary notification UI for an action result, validation or other info
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="type"></param>
        /// <param name="autoClose"></param>
        public void ShowNotification(string msg, NotificationType type = NotificationType.Info, bool autoClose = true)
        {
            var app = (ICertifyApp)GetApplication();
            app.ShowNotification(msg, type, autoClose);
        }
    }
}
