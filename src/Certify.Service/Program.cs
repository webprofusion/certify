using Certify.Locales;
using Microsoft.Owin.Hosting;
using System;
using System.Net.Http;
using System.Threading.Tasks;
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

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // an unhandled exception has caused the service to crash

            if (e.ExceptionObject != null)
            {
                //submit diagnostic info if connection available

                var API_BASE_URI = Locales.ConfigResources.APIBaseURI;

                var client = new HttpClient();

                var jsonRequest = Newtonsoft.Json.JsonConvert.SerializeObject(
                    new Models.Shared.FeedbackReport
                    {
                        EmailAddress = "(service exception)",
                        Comment = "An unhandled exception has occurred.",
                        IsException = true,
                        AppVersion = ConfigResources.AppName + " " + new Certify.Management.Util().GetAppVersion(),
                        SupportingData = new
                        {
                            Framework = Certify.Management.Util.GetDotNetVersion(),
                            OS = Environment.OSVersion.ToString(),
                            AppVersion = ConfigResources.AppName + " " + new Certify.Management.Util().GetAppVersion(),
                            IsException = true
                        }
                    });

                var data = new StringContent(jsonRequest, System.Text.Encoding.UTF8, "application/json");
                try
                {
                    Task.Run(async () =>
                    {
                        await client.PostAsync(API_BASE_URI + "feedback/submit", data);
                    });
                }
                catch (Exception exp)
                {
                    System.Diagnostics.Debug.WriteLine(exp.ToString());
                }
            }
        }
    }

    public class OwinService
    {
        private IDisposable _webApp;

        public void Start()
        {
#if DEBUG
            _webApp = WebApp.Start<APIHost>(Certify.Locales.ConfigResources.LocalServiceBaseURIDebug);
#else
            _webApp = WebApp.Start<APIHost>(Certify.Locales.ConfigResources.LocalServiceBaseURI);
#endif
        }

        public void Stop()
        {
            _webApp.Dispose();
        }
    }
}