using Owin;
using Swashbuckle.Application;
using System.Web.Http;
using LightInject;
using Microsoft.AspNet.SignalR;

namespace Certify.Service
{
    public partial class APIHost
    {
        public void Configuration(IAppBuilder appBuilder)
        {
            var config = new HttpConfiguration();

            // inject single CertifyManager for service to use
            var container = new ServiceContainer();
            container.RegisterApiControllers();
            container.EnableWebApi(config);

            container.Register<Management.ICertifyManager, Management.CertifyManager>(new PerContainerLifetime());

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

            var currentCertifyManager = container.GetInstance<Management.ICertifyManager>();
            currentCertifyManager.OnRequestProgressStateUpdated += (Models.RequestProgressState obj) =>
            {
                // notify client(s) of status updates
                StatusHub.HubContext.Clients.All.SendRequestProgressState(obj);
            };
        }
    }
}