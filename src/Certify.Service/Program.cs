using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Topshelf;

namespace Certify.Service
{
    public class Program
    {
        public static int Main(string[] args)
        {
            var currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += CurrentDomain_UnhandledException; ;

            return (int)HostFactory.Run(x =>
            {
                x.SetDisplayName("Certify Certificate Manager Service");
                x.SetDescription("Certify Certificate Manager Service");
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
                var logPath = System.IO.Path.Combine(EnvironmentUtil.GetAppDataFolder("logs"), "service.exceptions.log");
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
                if (exceptionObject != null && exceptionObject is Exception)
                {
                    using (var tc = new TelemetryManager(Locales.ConfigResources.AIInstrumentationKey))
                    {
                        var properties = new Dictionary<string, string>
                        {
                            { "AppVersion", Management.Util.GetAppVersion().ToString() },
                            { "InstanceId", Management.CoreAppSettings.Current.InstanceId}
                        };

                        tc.TrackException((Exception)exceptionObject, properties);
                    }
                }

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
                            InstanceId = Management.CoreAppSettings.Current.InstanceId,
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
}
