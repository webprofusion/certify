using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using Certify.Client;
using Certify.Server.Api.Public.Middleware;
using Certify.Server.Api.Public.Services;
using Certify.Server.Api.Public.SignalR;
using Certify.SharedUtils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.OpenApi.Models;

namespace Certify.Server.API
{

    /// <summary>
    /// Startup configuration for Public API
    /// </summary>
    public class Startup

    {
        /// <summary>
        /// Startup
        /// </summary>
        /// <param name="configuration"></param>
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        /// <summary>
        /// Injected configuration
        /// </summary>
        public IConfiguration Configuration { get; }

        /// <summary>
        /// Configure services
        /// </summary>
        /// <param name="services"></param>
        public void ConfigureServices(IServiceCollection services)
        {
            _ = ConfigureServicesWithResults(services);
        }

        /// <summary>
        /// Configure services for use by the API
        /// </summary>
        /// <param name="services"></param>
        public List<Models.Config.ActionResult> ConfigureServicesWithResults(IServiceCollection services)
        {
            var results = new List<Models.Config.ActionResult>();

            services
                .AddMemoryCache()
                .AddTokenAuthentication(Configuration)
                .AddAuthorization()
                .AddControllers()
                .AddJsonOptions(o =>
                {
                    o.JsonSerializerOptions.WriteIndented = true;
                });

            services.AddRouting(r => r.LowercaseUrls = true);

            services
                .AddSignalR(opt => opt.MaximumReceiveMessageSize = null)

                .AddMessagePackProtocol();

            services.AddResponseCompression(opts =>
            {
                opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                    new[] { "application/octet-stream", "application/json" });
            });

#if DEBUG

            services.AddEndpointsApiExplorer();

            // Register the Swagger generator, defining 1 or more Swagger documents
            // https://docs.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle?view=aspnetcore-3.1&tabs=visual-studio
            services.AddSwaggerGen(c =>
            {

                // docs UI will be available at /docs

                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Certify Server API",
                    Version = "v1",
                    Description = "The Certify Server API provides a certificate services API for use in devops, CI/CD, middleware etc. Certificates are managed by Certify The Web (https://certifytheweb.com) on the primary server using ACME, with API access controlled using API tokens."
                });

                c.UseAllOfToExtendReferenceSchemas();

                // use the actual method names as the generated operation id
                c.CustomOperationIds(e =>
                    $"{e.ActionDescriptor.RouteValues["action"]}"
                );

                // declare authorization method
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                    Name = "Authorization",
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.Http
                });

                // set security requirement
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        }, new List<string>()
                    }
                });

                // Set the comments path for the Swagger JSON and UI.
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);

                c.MapType<FileContentResult>(() =>
                {
                    return new Microsoft.OpenApi.Models.OpenApiSchema
                    {
                        Type = "string",
                        Format = "binary",
                    };
                });

            });
#endif
            // connect to certify service 
            var configManager = new ServiceConfigManager();
            var serviceConfig = configManager.GetServiceConfig();

            // Optionally load service host/port from environment variables. ENV_CERTIFY_SERVICE_ is kubernetes and CERTIFY_SERVICE_HOST is docker-compose
            var serviceHostEnv = Environment.GetEnvironmentVariable("ENV_CERTIFY_SERVICE_HOST") ?? Environment.GetEnvironmentVariable("CERTIFY_SERVICE_HOST");
            var servicePortEnv = Environment.GetEnvironmentVariable("ENV_CERTIFY_SERVICE_PORT") ?? Environment.GetEnvironmentVariable("CERTIFY_SERVICE_PORT");

            if (!string.IsNullOrEmpty(serviceHostEnv))
            {
                serviceConfig.Host = serviceHostEnv;
            }

            if (!string.IsNullOrEmpty(servicePortEnv) && int.TryParse(servicePortEnv, out var tryServicePort))
            {
                serviceConfig.Port = tryServicePort;
            }

            var backendServiceConnectionConfig = new Shared.ServerConnection(serviceConfig);

            backendServiceConnectionConfig.Authentication = "jwt";
            backendServiceConnectionConfig.ServerMode = "v2";

            System.Diagnostics.Debug.WriteLine($"Public API: connecting to background service {serviceConfig.Host}:{serviceConfig.Port}");

            var internalServiceClient = new Client.CertifyServiceClient(configManager, backendServiceConnectionConfig);

            internalServiceClient.OnMessageFromService += InternalServiceClient_OnMessageFromService;
            internalServiceClient.OnRequestProgressStateUpdated += InternalServiceClient_OnRequestProgressStateUpdated;
            internalServiceClient.OnManagedCertificateUpdated += InternalServiceClient_OnManagedCertificateUpdated;

            services.AddSingleton(typeof(Certify.Client.ICertifyInternalApiClient), internalServiceClient);

            services.AddSingleton<IInstanceManagementStateProvider, InstanceManagementStateProvider>();

            services.AddHostedService<ManagementWorker>();
            return results;
        }

        /// <summary>
        /// Configure the http request pipeline
        /// </summary>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {

            var statusHubContext = app.ApplicationServices.GetRequiredService<IHubContext<UserInterfaceStatusHub>>();

            if (statusHubContext == null)
            {
                throw new Exception("Status Hub not registered");
            }

            // setup signalr message forwarding, message received from internal service will be resent to our connected clients via our own SignalR hub
            _statusReporting = new UserInterfaceStatusHubReporting(statusHubContext);

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();
            app.UseCors((p) =>
            {
                p.AllowAnyOrigin()
                // .AllowCredentials()
                .AllowAnyMethod()
                .AllowAnyHeader();
            });

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHub<UserInterfaceStatusHub>("/api/internal/status");
                endpoints.MapHub<InstanceManagementHub>("/api/internal/managementhub");
            });

#if DEBUG
            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
            // specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c =>
            {
                c.RoutePrefix = "docs";
                c.DocumentTitle = "Certify Server API";
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Certify Server API");
            });
#endif
        }

        /// <summary>
        /// Connect to status stream of backend service
        /// </summary>
        /// <param name="app"></param>
        /// <returns></returns>
        public async Task SetupStatusHubConnections(WebApplication app)
        {

            var internalServiceClient = app.Services.GetRequiredService<ICertifyInternalApiClient>() as CertifyServiceClient;

            if (internalServiceClient == null)
            {
                app.Logger.LogError($"Unable to resolve internal service client. Cannot connect status stream.");
                return;
            }
            else
            {

                var attempts = 3;
                var connected = false;
                while (attempts > 0 && !connected)
                {
                    try
                    {
                        if (internalServiceClient != null)
                        {
                            await internalServiceClient.ConnectStatusStreamAsync();
                            connected = true;
                        }
                    }
                    catch
                    {
                        attempts--;

                        if (attempts == 0)
                        {
                            app.Logger.LogError($"Unable to connect to service SignalR stream at {internalServiceClient?.GetStatusHubUri()}.");
                        }
                        else
                        {
                            Task.Delay(2000).Wait(); // wait for service to start
                        }
                    }
                }
            }
        }

        private UserInterfaceStatusHubReporting _statusReporting = default!;

        private void InternalServiceClient_OnManagedCertificateUpdated(Models.ManagedCertificate obj)
        {
            System.Diagnostics.Debug.WriteLine("Public API: got ManagedCertUpdate msg to forward:" + obj.ToString());

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _statusReporting.ReportManagedCertificateUpdated(obj);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }
        private void InternalServiceClient_OnRequestProgressStateUpdated(Models.RequestProgressState obj)
        {
            System.Diagnostics.Debug.WriteLine("Public API: got Progress Message to forward:" + obj.ToString());
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _statusReporting.ReportRequestProgress(obj);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }
        private void InternalServiceClient_OnMessageFromService(string arg1, string arg2)
        {
            System.Diagnostics.Debug.WriteLine($"Public API: got message to forward: {arg1} {arg2}"); ;
        }
    }
}
