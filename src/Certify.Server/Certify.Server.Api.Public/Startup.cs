using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Certify.Server.Api.Public.Middleware;
using Certify.Shared.Core.Management;
using Certify.SharedUtils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        /// Configure services for use by the API
        /// </summary>
        /// <param name="services"></param>
        public void ConfigureServices(IServiceCollection services)
        {
            
            services
                .AddTokenAuthentication(Configuration)
                .AddAuthorization()
                .AddControllers()
                .AddJsonOptions(o =>
                {
                    o.JsonSerializerOptions.WriteIndented = true;
                });

            services.AddRouting(r => r.LowercaseUrls = true);

            services
                .AddSignalR()
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

            });
#endif
            // connect to certify service 
            var configManager = new ServiceConfigManager();
            var serviceConfig = configManager.GetServiceConfig();

            var serviceHostEnv = Environment.GetEnvironmentVariable("CERTIFY_SERVER_HOST");
            var servicePortEnv = Environment.GetEnvironmentVariable("CERTIFY_SERVER_PORT");

            if (!string.IsNullOrEmpty(serviceHostEnv))
            {
                serviceConfig.Host = serviceHostEnv;
            }

            if (!string.IsNullOrEmpty(servicePortEnv) && int.TryParse(servicePortEnv, out var tryServicePort))
            {
                serviceConfig.Port = tryServicePort;
            }

            var defaultConnectionConfig = new Shared.ServerConnection(serviceConfig);
            System.Diagnostics.Debug.WriteLine($"Public API: connecting to background service {serviceConfig:Host}:{serviceConfig.Port}");

            var connections = ServerConnectionManager.GetServerConnections(null, defaultConnectionConfig);
            var serverConnection = connections.FirstOrDefault(c => c.IsDefault == true);
#if DEBUG
            serverConnection = defaultConnectionConfig;
#endif
            var internalServiceClient = new Client.CertifyServiceClient(configManager, serverConnection);

            internalServiceClient.ConnectStatusStreamAsync();
            internalServiceClient.OnMessageFromService += InternalServiceClient_OnMessageFromService;
            internalServiceClient.OnRequestProgressStateUpdated += InternalServiceClient_OnRequestProgressStateUpdated;
            internalServiceClient.OnManagedCertificateUpdated += InternalServiceClient_OnManagedCertificateUpdated;

            services.AddSingleton(typeof(Certify.Client.ICertifyInternalApiClient), internalServiceClient);
        }

        /// <summary>
        /// Configure the http request pipeline
        /// </summary>
        /// <param name="app"></param>
        /// <param name="env"></param>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {

            var statusHubContext = app.ApplicationServices.GetService(typeof(IHubContext<StatusHub>)) as IHubContext<StatusHub>;
            if (statusHubContext == null)
            {
                throw new Exception("Status Hub not registered");
            }

            // setup signalr message forwarding, message received from internal service will be resent to our connected clients via our own SignalR hub
           _statusReporting = new StatusHubReporting(statusHubContext);

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();
            app.UseCors((p) =>
            {
                p.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
                //.AllowCredentials();

            });

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHub<StatusHub>("/api/internal/status");
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

        private StatusHubReporting _statusReporting;

        private void InternalServiceClient_OnManagedCertificateUpdated(Models.ManagedCertificate obj)
        {
            System.Diagnostics.Debug.WriteLine("Public API: got ManagedCertUpdate msg to forward:" + obj.ToString());

            _statusReporting.ReportManagedCertificateUpdated(obj);
        }
        private void InternalServiceClient_OnRequestProgressStateUpdated(Models.RequestProgressState obj)
        {
            System.Diagnostics.Debug.WriteLine("Public API: got Progress Message to forward:" + obj.ToString());
            _statusReporting.ReportRequestProgress(obj);
        }
        private void InternalServiceClient_OnMessageFromService(string arg1, string arg2)
        {
            System.Diagnostics.Debug.WriteLine($"Public API: got message to forward: {arg1} {arg2}"); ;
        }
    }
}
