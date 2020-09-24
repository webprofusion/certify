using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using System.Runtime.InteropServices;
using Microsoft.OpenApi.Models;

namespace Certify.Server.Core
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
            services.AddControllers();

            services.AddSignalR().AddMessagePackProtocol();

            services.AddCors(options =>
            {
                options.AddDefaultPolicy(
                                  builder =>
                                  {
                                      
                                      builder.AllowAnyOrigin();
                                      builder.AllowAnyMethod();
                                  });
            });

#if DEBUG
            // Register the Swagger generator, defining 1 or more Swagger documents
            // https://docs.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle?view=aspnetcore-3.1&tabs=visual-studio
            services.AddSwaggerGen(c =>
            {

                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Certify Core Internal API",
                    Version = "v1",
                    Description = "Provides a private API for use by the Certify The Web UI and related components. This internal API changes between versions, you should use the public API when building integrations instead."
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

            var useWindowsNativeFeatures = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            // inject instance of certify manager

            var certifyManager = new Management.CertifyManager(useWindowsNativeFeatures);

            services.AddSingleton<Management.ICertifyManager>(certifyManager);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHubContext<Service.StatusHub> statusHubContext, Management.ICertifyManager certifyManager)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            
            // set status report context provider
            certifyManager.SetStatusReporting(new Service.StatusHubReporting(statusHubContext));

            //

           // app.UseHttpsRedirection();

            app.UseRouting();

            app.UseCors();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<Service.StatusHub>("/api/status");
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
                c.DocumentTitle = "Certify Core Server API";
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Certify Core Server API");
            });
#endif

        }
    }
}
