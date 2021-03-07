using System.Net;
using System.Web.Http;
using LightInject;
using Microsoft.AspNet.SignalR;
using Newtonsoft.Json.Serialization;
using Owin;
using Swashbuckle.Application;

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
#if !DEBUG_NO_AUTH
            var owinHttp = appBuilder.Properties["Microsoft.Owin.Host.HttpListener.OwinHttpListener"] as Microsoft.Owin.Host.HttpListener.OwinHttpListener;
            owinHttp.Listener.AuthenticationSchemes = AuthenticationSchemes.IntegratedWindowsAuthentication;
#endif

#if DEBUG
            config
              .EnableSwagger(c => c.SingleApiVersion("v1", "Service API for local install of Certify SSL Manager"))
              .EnableSwaggerUi();
#endif

            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
                );

            config.Formatters.JsonFormatter.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();

            appBuilder.UseCors(Microsoft.Owin.Cors.CorsOptions.AllowAll);

            appBuilder.MapSignalR("/api/status", new HubConfiguration());
            appBuilder.UseWebApi(config);

            _container = new ServiceContainer();

            _container.RegisterApiControllers();
            _container.EnableWebApi(config);

            // inject single CertifyManager for service to use
            _container.Register<Management.ICertifyManager, Management.CertifyManager>(new PerContainerLifetime());

            var currentCertifyManager = _container.GetInstance<Management.ICertifyManager>();

            // attached handlers for SignalR hub updates
            currentCertifyManager.SetStatusReporting(new StatusHubReporting());


            // hourly jobs timer (renewal etc)
            _timer = new System.Timers.Timer(60 * 60 * 1000); // every 60 minutes
            _timer.Elapsed += _timer_Elapsed;
            _timer.Start();

            // daily jobs timer ((cleanup etc)
            _dailyTimer = new System.Timers.Timer(24 * 60 * 60 * 1000); // every 24 hrs
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
