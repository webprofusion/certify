using Owin;
using Swashbuckle.Application;
using System.Web.Http;
using LightInject;
using Microsoft.AspNet.SignalR;

namespace Certify.Service
{
    public partial class APIHost
    {
        private System.Timers.Timer _timer;
        private ServiceContainer _container;

        public void Configuration(IAppBuilder appBuilder)
        {
            var config = new HttpConfiguration();

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
              .EnableSwagger(c => c.SingleApiVersion("v1", "Service API for local install of Certify the web"))
              .EnableSwaggerUi();
#endif
            appBuilder.MapSignalR("/api/status", new HubConfiguration());
            appBuilder.UseWebApi(config);

            var currentCertifyManager = _container.GetInstance<Management.ICertifyManager>();
            currentCertifyManager.OnRequestProgressStateUpdated += (Models.RequestProgressState obj) =>
            {
                // notify client(s) of status updates
                StatusHub.HubContext.Clients.All.SendRequestProgressState(obj);
            };

            // use a timer to poll for periodic jobs (cleanup, renewal etc)
            _timer = new System.Timers.Timer(60 * 60 * 1000);// * 60 * 1000); // every 60 minutes
            _timer.Elapsed += _timer_Elapsed;
            _timer.Start();
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