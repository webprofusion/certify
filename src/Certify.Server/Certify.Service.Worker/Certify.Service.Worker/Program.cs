using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Server.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Certify.Service.Worker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args)
                .UseSystemd()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(serverOptions =>
                    {
                        // serverOptions.AllowSynchronousIO = true; // allow sync IO for legacy outputs
                        serverOptions.UseSystemd();
                    });  

                    webBuilder.ConfigureLogging(logging =>
                      {
                          logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Debug);
                          logging.AddFilter("Microsoft.AspNetCore.Http.Connections", LogLevel.Debug);
                      });

                    webBuilder.UseStartup<Startup>();
                });

            return builder;
        }

    }
}
