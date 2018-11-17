using System;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Locales;
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
                    LogException(ex);
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

        private static void LogException(object exceptionObject)
        {
            // log exception
            try
            {
                var logPath = Util.GetAppDataFolder("logs") + "\\service.exceptions.log";
                System.IO.File.AppendAllText(logPath, "Service Exception :: [" + DateTime.Now + "] :: " + ((Exception)exceptionObject).ToString());
            }
            catch { }

            //submit diagnostic info if connection available and status reporting enabled
            if (Management.CoreAppSettings.Current.EnableStatusReporting)
            {
                var client = new HttpClient();

                var appVersion = new Certify.Management.Util().GetAppVersion();

                var jsonRequest = Newtonsoft.Json.JsonConvert.SerializeObject(
                    new Models.Shared.FeedbackReport
                    {
                        EmailAddress = "(service exception)",
                        Comment = "An unhandled service exception has occurred.: " + ((Exception)exceptionObject).ToString(),
                        IsException = true,
                        AppVersion = appVersion.ToString(),
                        SupportingData = new
                        {
                            Framework = Certify.Management.Util.GetDotNetVersion(),
                            OS = Environment.OSVersion.ToString(),
                            AppVersion = new Certify.Management.Util().GetAppVersion(),
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
                LogException(e.ExceptionObject);
            }
        }
    }

    public class OwinService
    {
        private IDisposable _webApp;

        public void Start()
        {
            var serviceConfig = Certify.Management.Util.GetAppServiceConfig();

            var serviceUri = $"http://{serviceConfig.Host}:{serviceConfig.Port}";

            try
            {
                _webApp = WebApp.Start<APIHost>(serviceUri);
            }
            catch (Exception)
            {
                System.Diagnostics.Debug.WriteLine($"Service failed to listen on {serviceUri}. Attempting to reallocate port.");
                // failed to listen on service uri, attempt reconfiguration of port.
                int currentPort = serviceConfig.Port;

                int newPort = currentPort += 2;

                serviceUri = $"http://{serviceConfig.Host}:{newPort}";
                _webApp = WebApp.Start<APIHost>(serviceUri);

                // if that worked, save the new port setting
                Certify.Management.Util.SetAppServicePort(newPort);

                System.Diagnostics.Debug.WriteLine($"Service started on {serviceUri}.");
            }

        }

        public void Stop()
        {
            if (_webApp != null) _webApp.Dispose();
        }
    }
}
