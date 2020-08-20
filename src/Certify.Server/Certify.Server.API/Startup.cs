using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Server.Api.Public.Middleware;
using Certify.Shared.Core.Management;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace Certify.Server.API
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
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

#if DEBUG
            // Register the Swagger generator, defining 1 or more Swagger documents
            // https://docs.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle?view=aspnetcore-3.1&tabs=visual-studio
            services.AddSwaggerGen(c =>
            {

                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "CertifyServer - Certificate Server API",
                    Version = "v1",
                    Description = "CertifyServer provides a certificate services API for use in devops, CI/CD, middleware etc. Certificates are managed by Certify The Web on the primary server using ACME, with API access controlled using API tokens."
                });

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

            });
#endif
            // connect to certify service 
            var configManager = new SharedUtils.ServiceConfigManager();
            var defaultConnectionConfig = new Shared.ServerConnection(configManager.GetServiceConfig());
            var connections = ServerConnectionManager.GetServerConnections(null, defaultConnectionConfig);
            var serverConnection = connections.FirstOrDefault(c => c.IsDefault = true);

            services.AddSingleton(typeof(Certify.Client.ICertifyInternalApiClient), new Client.CertifyApiClient(configManager, serverConnection));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

#if DEBUG
            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
            // specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c =>
            {
                c.RoutePrefix = "docs";
                c.DocumentTitle = "CertifyServer - Certificate Server API";
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "CertifyServer - Certificate Server API");
            });
#endif
        }
    }
}
