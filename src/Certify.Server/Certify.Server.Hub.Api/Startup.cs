using System.Reflection;
using Certify.Client;
using Certify.Models;
using Certify.Models.Reporting;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Certify.SharedUtils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.OpenApi.Models;

namespace Certify.Server.Hub.Api
{

    /// <summary>
    /// Startup configuration for Public API
    /// </summary>
    public class Startup

    {
        private List<ActionStep> _systemStatusItems = new List<ActionStep>();

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
            AddSystemStatusItem(
                SystemStatusCategories.HUB_API,
                SystemStatusKeys.HUB_API_MODE,
                title: "Hub API with Connected Primary Instance",
                description: "Hub API will connect to a local or remote instance via the instance API and SignalR."
            );

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
            services.AddProblemDetails();


            services
                .AddSignalR(opt => opt.MaximumReceiveMessageSize = null)
                .AddMessagePackProtocol();

            services.AddResponseCompression(opts =>
            {
                opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                    new[] { "application/octet-stream", "application/json" });
            });

            services.AddOpenApi(); // required in net9 to resolve warning "Unable to find service type 'Microsoft.Extensions.ApiDescriptions.IDocumentProvider' in dependency injection container."

            services.AddEndpointsApiExplorer();

            // Register the Swagger generator, defining 1 or more Swagger documents
            // https://docs.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle?view=aspnetcore-3.1&tabs=visual-studio
            services.AddSwaggerGen(c =>
            {

                // docs UI will be available at /docs

                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Certify Management Hub API",
                    Version = "v1",
                    Description = "The Certify Management Hub API provides a certificate services API for use in UI, devops, CI/CD, middleware etc. See certifytheweb.com for more details."
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

            // connect to primary certify service 
            var configManager = new ServiceConfigManager();
            var serviceConfig = configManager.GetServiceConfig();

            if (serviceConfig.ConfigStatus == Shared.ConfigStatus.DefaultFailed)
            {
                AddSystemStatusItem(
                    SystemStatusCategories.HUB_API,
                    SystemStatusKeys.HUB_API_STARTUP_READSVCCONFIG,
                    title: "Service Config Access",
                    description: "Service Config Not Accessible",
                    hasError: true
                );
            }
            else
            {
                AddSystemStatusItem(
                    SystemStatusCategories.HUB_API,
                    SystemStatusKeys.HUB_API_STARTUP_READSVCCONFIG,
                    title: "Service Config Accessible",
                    description: "Service config loaded OK."
                );
            }

            // Optionally load service host/port from environment variables. ENV_CERTIFY_SERVICE_ is kubernetes and CERTIFY_SERVICE_HOST is docker-compose
            var serviceHostEnv = Environment.GetEnvironmentVariable("ENV_CERTIFY_SERVICE_HOST") ?? Environment.GetEnvironmentVariable("CERTIFY_SERVICE_HOST");
            var servicePortEnv = Environment.GetEnvironmentVariable("ENV_CERTIFY_SERVICE_PORT") ?? Environment.GetEnvironmentVariable("CERTIFY_SERVICE_PORT");

            if (!string.IsNullOrEmpty(serviceHostEnv))
            {
                serviceConfig.Host = serviceHostEnv;

                AddSystemStatusItem(
                    SystemStatusCategories.HUB_API,
                    SystemStatusKeys.HUB_API_STARTUP_SVCHOSTENV,
                    title: "Service Host Set By Env",
                    description: $"Primary Instance Service host has been set by environment variable to {serviceHostEnv}"
                );
            }

            if (!string.IsNullOrEmpty(servicePortEnv) && int.TryParse(servicePortEnv, out var tryServicePort))
            {
                serviceConfig.Port = tryServicePort;

                AddSystemStatusItem(
                    SystemStatusCategories.HUB_API,
                    SystemStatusKeys.HUB_API_STARTUP_SVCPORTENV,
                    title: "Service Host Set By Env",
                    description: $"Primary Instance Service host has been set by environment variable to {serviceHostEnv}"
                );
            }

            var backendServiceConnectionConfig = new Shared.ServerConnection(serviceConfig);

            backendServiceConnectionConfig.Authentication = "jwt";
            backendServiceConnectionConfig.ServerMode = "v2";
            //backendServiceConnectionConfig.Mode = "namedpipe";

            System.Diagnostics.Debug.WriteLine($"Public API: connecting to background service {serviceConfig.Host}:{serviceConfig.Port}");

            var internalServiceClient = new Client.CertifyServiceClient(configManager, backendServiceConnectionConfig);

            internalServiceClient.OnMessageFromService += InternalServiceClient_OnMessageFromService;
            internalServiceClient.OnRequestProgressStateUpdated += InternalServiceClient_OnRequestProgressStateUpdated;
            internalServiceClient.OnManagedCertificateUpdated += InternalServiceClient_OnManagedCertificateUpdated;

            services.AddSingleton(typeof(Certify.Client.ICertifyInternalApiClient), internalServiceClient);

            services.AddSingleton<IInstanceManagementStateProvider, InstanceManagementStateProvider>();

            // we create a new instance of the management API for each request
            services.AddTransient<ManagementAPI>();

            services.AddHostedService<ManagementWorker>();
            return results;
        }

        private void AddSystemStatusItem(string systemStatusCategory, string systemStatusKey, string title, string description, bool hasError = false, bool hasWarning = false) => _systemStatusItems.Add(new ActionStep(systemStatusKey, systemStatusCategory, title, description, hasError, hasWarning));

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

                AddSystemStatusItem(
                    SystemStatusCategories.HUB_API,
                    SystemStatusKeys.HUB_API_STARTUP_ENVIRONMENT,
                    title: "Development Mode",
                    description: $"Hub API is in Development mode."
                );
            }
            else
            {
                AddSystemStatusItem(
                    SystemStatusCategories.HUB_API,
                    SystemStatusKeys.HUB_API_STARTUP_ENVIRONMENT,
                    title: "Production Mode",
                    description: $"Hub API is in Production mode."
                );
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


            // Converts unhandled exceptions into Problem Details responses
            app.UseExceptionHandler();

            // Returns the Problem Details response for (empty) non-successful responses
            app.UseStatusCodePages();

#if DEBUG
            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
            // specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c =>
            {
                c.RoutePrefix = "docs";
                c.DocumentTitle = "Certify Management Hub API";
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Certify Management Hub API");
            });

            AddSystemStatusItem(
                SystemStatusCategories.HUB_API,
                SystemStatusKeys.HUB_API_STARTUP_APIDOCS,
                title: "API Docs UI enabled",
                description: $"Hub API Swagger docs available at /docs"
            );
#else
            AddSystemStatusItem(
                SystemStatusCategories.HUB_API,
                SystemStatusKeys.HUB_API_STARTUP_APIDOCS,
                title: "API Docs UI not enabled",
                description: $"Hub API Swagger docs not enabled in release mode."
            );
#endif
        }

        /// <summary>
        /// Connect to status stream of primary service
        /// </summary>
        /// <param name="app"></param>
        /// <returns></returns>
        public async Task SetupStatusHubConnections(WebApplication app)
        {

            var internalServiceClient = app.Services.GetRequiredService<ICertifyInternalApiClient>() as CertifyServiceClient;

            if (internalServiceClient == null)
            {
                var errMsg = "Unable to resolve internal service client. Cannot connect status stream.";
                app.Logger.LogError(errMsg);

                AddSystemStatusItem(
                    SystemStatusCategories.HUB_API,
                    SystemStatusKeys.HUB_API_STARTUP_SVC_STATUS_STREAM,
                    title: "Primary Service Status Stream",
                    description: errMsg,
                    hasError: true
                );

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

                            AddSystemStatusItem(
                                SystemStatusCategories.HUB_API,
                                SystemStatusKeys.HUB_API_STARTUP_SVC_STATUS_STREAM,
                                title: "Primary Service Status Stream",
                                description: "Hub API has connected to the backend service instance status stream."
                            );
                        }
                    }
                    catch
                    {
                        attempts--;

                        if (attempts == 0)
                        {
                            var errMsg = $"Unable to connect to service SignalR stream at {internalServiceClient?.GetStatusHubUri()}.";

                            app.Logger.LogError(errMsg);

                            AddSystemStatusItem(
                                SystemStatusCategories.HUB_API,
                                SystemStatusKeys.HUB_API_STARTUP_SVC_STATUS_STREAM,
                                title: "Primary Service Status Stream",
                                description: errMsg,
                                hasError: true
                            );
                        }
                        else
                        {
                            app.Logger.LogWarning($"Waiting for service SignalR stream at {internalServiceClient?.GetStatusHubUri()}.");
                            Task.Delay(2000).Wait(); // wait for service to start
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Reports the startup status of a web application by updating system status items.
        /// </summary>
        /// <param name="app">The web application instance from which services are retrieved to manage system status.</param>
        public void ReportStartupStatus(WebApplication app)
        {
            var stateProvider = app.Services.GetRequiredService<IInstanceManagementStateProvider>();
            if (stateProvider != null)
            {
                foreach (var item in _systemStatusItems)
                {
                    stateProvider.AddOrUpdateSystemStatusItem(item);
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
