using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Certify.Server.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
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
                 .ConfigureAppConfiguration((context, builder) =>
                 {
                     // when running within an integration test optionally load test config
                     if (File.Exists(Path.Join(AppContext.BaseDirectory, "appsettings.worker.test.json")))
                     {
                         builder.AddJsonFile("appsettings.worker.test.json");
                         builder.AddUserSecrets(typeof(Program).GetTypeInfo().Assembly); // for worker pfx details)
                     }
                 })
                 .ConfigureLogging(logging =>
                 {
                     logging.ClearProviders();
                     logging.AddConsole();

                 })
                .UseSystemd()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(serverOptions =>
                    {
                        serverOptions.UseSystemd();
                        // configure https listener, cert path and pwd can come either from an environment variable, usersecrets or appsettings.
                        // configuration precedence is secrets first, https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-5.0#default
                        var configuration = (IConfiguration)serverOptions.ApplicationServices.GetService(typeof(IConfiguration));

                        var useHttps = bool.Parse(configuration["API:Service:UseHttps"]);

                        // default IP to localhost then specify from configuration
                        var ipSelection = configuration["API:Service:BindingIP"];
                        var ipBinding = IPAddress.Loopback;

                        if (ipSelection != null)
                        {
                            if (ipSelection.ToLower() == "loopback")
                            {
                                ipBinding = IPAddress.Loopback;
                            }
                            else if (ipSelection.ToLower() == "any")
                            {
                                ipBinding = IPAddress.Any;
                            }
                            else
                            {
                                ipBinding = IPAddress.Parse(ipSelection);
                            }
                        }

                        if (useHttps)
                        {

                            var certPassword = Environment.GetEnvironmentVariable("ASPNETCORE_Kestrel__Certificates__Development__Password");
                            var certPath = Environment.GetEnvironmentVariable("ASPNETCORE_Kestrel__Certificates__Development__Path");

                            // if not yet defined load config from usersecrets (development env only) or appsettings
                            if (certPassword == null)
                            {
                                certPassword = configuration["Kestrel:Certificates:Default:Password"];
                            }

                            if (certPath == null)
                            {
                                certPath = configuration["Kestrel:Certificates:Default:Path"];
                            }

                            try
                            {
                                var certificate = new X509Certificate2(certPath, certPassword);

                                // if password is wrong at this stage the attempts to use the cert will results in SSL Protocol Error


                                var httpsConnectionAdapterOptions = new HttpsConnectionAdapterOptions()
                                {
                                    ClientCertificateMode = ClientCertificateMode.NoCertificate,
                                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                                    ServerCertificate = certificate,
                                };

                                var httpsPort = Convert.ToInt32(configuration["API:Service:HttpsPort"]);



                                serverOptions.Listen(new System.Net.IPEndPoint(ipBinding, httpsPort), listenOptions =>
                                {
                                    listenOptions.UseHttps(httpsConnectionAdapterOptions);
                                });

                            }
                            catch (Exception exp)
                            {
                                // TODO: there is no logger yet, need to report this failure to main log once the log exists
                                System.Diagnostics.Debug.WriteLine("Failed to load PFX certificate for application. Check service certificate config." + exp.ToString());
                            }

                        }
                        else
                        {
                            var httpPort = Convert.ToInt32(configuration["API:Service:HttpPort"]);

                            serverOptions.Listen(new System.Net.IPEndPoint(ipBinding, httpPort), listenOptions =>
                            {
                            });
                        }


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
