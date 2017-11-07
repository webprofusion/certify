using Certify.Models;
using Microsoft.Owin.Hosting;
using Owin;
using Swashbuckle.Application;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using Topshelf;

namespace Certify.Service
{
    public class Program
    {
        public static int Main(string[] args)
        {
            return (int)HostFactory.Run(x =>
            {
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
            _webApp = WebApp.Start<StartOwin>("http://localhost:9696");
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
            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
                );

            config
              .EnableSwagger(c => c.SingleApiVersion("v1", "A title for your API"))
              .EnableSwaggerUi();

            appBuilder.UseWebApi(config);
        }
    }

    public class ManagedSitesController : ApiController
    {
        Management.CertifyManager _certifyManager = new Certify.Management.CertifyManager();

        // Get List of Top N Managed Sites, filtered by title
        public List<ManagedSite> Get()
        {
            return _certifyManager.GetManagedSites();
        }
        //add or update managed site

        public ManagedSite Update(ManagedSite site)
        {
            var certifyManager = new Certify.Management.CertifyManager();
            //certifyManager.UpdateManagedSite(site);

            return site;
        }


        //delete managed site

        //get web server site list

        //test managed site configuration

        //perform certificate request (stream responses or poll?). How to get progress updates.

        //get all requests in progress
    }

}