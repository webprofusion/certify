using Microsoft.Owin.Hosting;
using Owin;
using Swashbuckle.Application;
using System;
using System.Linq;
using System.Net.Http;
using System.Web.Http;
using Topshelf;
using LightInject.WebApi;
using LightInject;

namespace Certify.Service
{
    public class Program
    {
        public static int Main(string[] args)
        {
            return (int)HostFactory.Run(x =>
            {
                x.SetDisplayName("Certify SSL Manager Service");
                x.SetDescription("Certify The Web - SSL Manager Service for IIS");
                x.StartAutomaticallyDelayed();

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
    }

    public class OwinService
    {
        private IDisposable _webApp;

        public void Start()
        {
#if DEBUG
            _webApp = WebApp.Start<StartOwin>(Certify.Locales.ConfigResources.LocalServiceBaseURIDebug);
#else
            _webApp = WebApp.Start<StartOwin>(Certify.Locales.ConfigResources.LocalServiceBaseURI);
#endif
        }

        public void Stop()
        {
            _webApp.Dispose();
        }
    }

    public class StartOwin
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
            appBuilder.UseWebApi(config);
        }
    }
}