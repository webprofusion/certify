using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Certify.Client;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers;
using Certify.SharedUtils;
using Certify.UI.Shared;
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
            var stack = new StackTrace();
            if (stack.GetFrames().Last().GetMethod().Name == "Main")
            {
                return new AppViewModel();
            }
            else
            {
                return new AppViewModelDesign();
            }
        }

        /// <summary>
        /// Setup app model
        /// </summary>
        private void Init()
        {
            Log = new Loggy(
                new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Debug()
                .WriteTo.File(Path.Combine(EnvironmentUtil.GetAppDataFolder("logs"), "ui.log"), shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10))
                .CreateLogger()
                );

            ProgressResults = new ObservableCollection<RequestProgressState>();

            ImportedManagedCertificates = new ObservableCollection<ManagedCertificate>();
            ManagedCertificates = new ObservableCollection<ManagedCertificate>();
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

        /// <summary>
        /// Provider for current set of plugins
        /// </summary>
        public PluginManager PluginManager { get; set; }

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

        public async Task<List<ActionResult>> PerformServiceDiagnostics()
        {
            return await _certifyClient.PerformServiceDiagnostics();
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
