using LightInject;
using Microsoft.AspNet.SignalR;
using Owin;
using Swashbuckle.Application;
using System.Net;
using System.Web.Http;

namespace Certify.Service
{
    public partial class APIHost
    {
        private System.Timers.Timer _timer;
        private System.Timers.Timer _dailyTimer;
        private ServiceContainer _container;

        public void Configuration(IAppBuilder appBuilder)
        {
            var config = new HttpConfiguration();

            // enable windows auth credentials
            var listener = (Microsoft.Owin.Host.HttpListener.OwinHttpListener)appBuilder.Properties["Microsoft.Owin.Host.HttpListener.OwinHttpListener"];
            listener.Listener.AuthenticationSchemes = AuthenticationSchemes.IntegratedWindowsAuthentication;

            // inject single CertifyManager for service to use
            _container = new ServiceContainer();
            _container.RegisterApiControllers();
            _container.EnableWebApi(config);

            _container.Register<Management.ICertifyManager, Management.CertifyManager>(new PerContainerLifetime());

            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
                );
#if DEBUG
            config
              .EnableSwagger(c => c.SingleApiVersion("v1", "Service API for local install of Certify SSL Manager"))
              .EnableSwaggerUi();
#endif

            // appBuilder.UseCors(Microsoft.Owin.Cors.CorsOptions.AllowAll);

            appBuilder.MapSignalR("/api/status", new HubConfiguration());
            appBuilder.UseWebApi(config);

            var currentCertifyManager = _container.GetInstance<Management.ICertifyManager>();
            currentCertifyManager.OnRequestProgressStateUpdated += (Models.RequestProgressState obj) =>
            {
                // notify client(s) of status updates
                StatusHub.SendRequestProgressState(obj);
            };

            currentCertifyManager.OnManagedCertificateUpdated += (Models.ManagedCertificate obj) =>
            {
                // notify client(s) of update to a managed site
                StatusHub.SendManagedCertificateUpdate(obj);
            };

            // use a timer to poll for periodic jobs (cleanup, renewal etc)
            _timer = new System.Timers.Timer(60 * 60 * 1000);// every 60 minutes
            _timer.Elapsed += _timer_Elapsed;
            _timer.Start();

            // use a timer to poll for periodic jobs (cleanup, renewal etc)
            _dailyTimer = new System.Timers.Timer(24 * 60 * 60 * 1000);// every 24 hrs
            _dailyTimer.Elapsed += _dailyTimer_Elapsed;
            _dailyTimer.Start();
        }

        private async void _dailyTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            var currentCertifyManager = _container.GetInstance<Management.ICertifyManager>();
            if (currentCertifyManager != null)
            {
                await currentCertifyManager.PerformDailyTasks();
            }
        }

        private async void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            var currentCertifyManager = _container.GetInstance<Management.ICertifyManager>();
            if (currentCertifyManager != null)
            {
                await currentCertifyManager.PerformPeriodicTasks();
            }
        }
    }
}
