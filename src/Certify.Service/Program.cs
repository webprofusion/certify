using System;
using System.Net.Http;
using System.ServiceProcess;
using System.Threading.Tasks;
using Certify.Management;
using Microsoft.Owin.Hosting;
using Topshelf;

namespace Certify.Service
{
    public class Program
    {
        public static int Main(string[] args)
        {
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += CurrentDomain_UnhandledException; ;

            return (int)HostFactory.Run(x =>
            {
                x.SetDisplayName("Certify SSL Manager Service");
                x.SetDescription("Certify SSL/TLS Manager Service");
                x.StartAutomaticallyDelayed();

                x.OnException(ex =>
                {
                    // Do something with the exception
                    LogEvent(ex, includeReporting: true);
                });
#if DEBUG
                x.SetInstanceName("Debug");
#else
                // x.SetInstanceName("CertifySSLManager.Service");
#endif

                // FIXME: we should offer option during setup to configure this as a service account
                // account requires admin rights in IIS (and wwwroot etc) and permission to
                // administer certificates in certificate store
                x.RunAsLocalSystem();

                x.EnableServiceRecovery(r =>
                {
                    // restart service if it crashes
                    r.RestartService(10);
                    r.OnCrashOnly();
                    r.SetResetPeriod(1);
                });

                x.Service<OwinService>(s =>
                {
                    s.ConstructUsing(() => new OwinService());
                    s.WhenStarted(service => service.Start());
                    s.WhenStopped(service => service.Stop());
                });
            });
        }

        public static void LogEvent(object exceptionObject, string msg = null, bool includeReporting = false)
        {
            // log event/exception
            try
            {
                var logPath = Util.GetAppDataFolder("logs") + "\\service.exceptions.log";
                if (msg != null)
                {
                    System.IO.File.AppendAllText(logPath, "\r\n[" + DateTime.Now + "] :: " + msg);
                }

                if (exceptionObject != null)
                {
                    System.IO.File.AppendAllText(logPath, "\r\nService Exception :: [" + DateTime.Now + "] :: " + ((Exception)exceptionObject).ToString());
                }
            }
            catch { }

            //submit diagnostic info if connection available and status reporting enabled
            if (Management.CoreAppSettings.Current.EnableStatusReporting && includeReporting)
            {
                var client = new HttpClient();

                var appVersion = Management.Util.GetAppVersion();

                var jsonRequest = Newtonsoft.Json.JsonConvert.SerializeObject(
                    new Models.Shared.FeedbackReport
                    {
                        EmailAddress = "(service exception)",
                        Comment = "An unhandled service exception has occurred.: " + ((Exception)exceptionObject)?.ToString(),
                        IsException = true,
                        AppVersion = appVersion.ToString(),
                        SupportingData = new
                        {
                            Framework = Certify.Management.Util.GetDotNetVersion(),
                            OS = Environment.OSVersion.ToString(),
                            AppVersion = Management.Util.GetAppVersion(),
                            IsException = true
                        }
                    });

                var data = new StringContent(jsonRequest, System.Text.Encoding.UTF8, "application/json");
                try
                {
                    Task.Run(async () =>
                    {
                        await client.PostAsync(Models.API.Config.APIBaseURI + "feedback/submit", data);
                    });
                }
                catch (Exception exp)
                {
                    System.Diagnostics.Debug.WriteLine(exp.ToString());
                }
            }
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // an unhandled exception has caused the service to crash

            if (e.ExceptionObject != null)
            {
                LogEvent(e.ExceptionObject, includeReporting: true);
            }
        }
    }

    public class OwinService
    {
        private IDisposable _webApp;

        public string DiagnoseServices()
        {
            var httpStatus = "Unknown";

            try
            {
                var sc = new ServiceController("HTTP");
                switch (sc.Status)
                {
                    case ServiceControllerStatus.Running:
                        httpStatus = "Running";
                        break;
                    case ServiceControllerStatus.Stopped:
                        httpStatus = "Stopped";
                        break;
                    case ServiceControllerStatus.Paused:
                        httpStatus = "Paused";
                        break;
                    case ServiceControllerStatus.StopPending:
                        httpStatus = "Stopping";
                        break;
                    case ServiceControllerStatus.StartPending:
                        httpStatus = "Starting";
                        break;
                    default:
                        httpStatus = "Status Changing";
                        break;
                }
            }
            catch { }

            return $"System HTTP Service: {httpStatus}";
        }

        public void Start()
        {

            var serviceConfig = SharedUtils.ServiceConfigManager.GetAppServiceConfig();

            serviceConfig.ServiceFaultMsg = "";

            var serviceUri = $"http://{serviceConfig.Host}:{serviceConfig.Port}";

            try
            {
                _webApp = WebApp.Start<APIHost>(serviceUri);
                Program.LogEvent(null, $"Service API bound OK to {serviceUri}", false);
            }
            catch (Exception exp)
            {
                var httpSysStatus = DiagnoseServices();

                var msg = $"Service failed to listen on {serviceUri}. :: {httpSysStatus} :: Attempting to reallocate port. {exp.ToString()}";

                Program.LogEvent(exp, msg, false);

                // failed to listen on service uri, attempt reconfiguration of port.
                int currentPort = serviceConfig.Port;

                int newPort = currentPort += 2;

                var reconfiguredServiceUri = $"http://{serviceConfig.Host}:{newPort}";

                try
                {
                    // if the http listener cannot bind here then the entire service will fail to start
                    _webApp = WebApp.Start<APIHost>(reconfiguredServiceUri);

                    Program.LogEvent(null, $"Service API bound OK to {reconfiguredServiceUri}", false);

                    // if that worked, save the new port setting
                    serviceConfig.Port = newPort;

                    System.Diagnostics.Debug.WriteLine($"Service started on {reconfiguredServiceUri}.");
                }
                catch (Exception)
                {
                    serviceConfig.ServiceFaultMsg = $"Service failed to listen on {serviceUri}. \r\n\r\n{httpSysStatus} \r\n\r\nEnsure the windows HTTP service is available using 'sc config http start= demand' then 'net start http' commands.";
                    throw;
                }
            }
            finally
            {
                SharedUtils.ServiceConfigManager.StoreUpdatedAppServiceConfig(serviceConfig);
            }

        }

        public void Stop()
        {
            if (_webApp != null) _webApp.Dispose();
        }
    }
}
