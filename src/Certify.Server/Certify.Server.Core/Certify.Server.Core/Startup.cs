using System.Runtime.InteropServices;
using System.Security.Claims;
using Certify.Management;
using Certify.Models;
using Certify.Service.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;

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

            services
                .AddSignalR()
                .AddMessagePackProtocol();

            var appDataPath = EnvironmentUtil.CreateAppDataPath("keys");

            services
                .AddDataProtection(a =>
                {
                    a.ApplicationDiscriminator = "certify";
                })
                .PersistKeysToFileSystem(new DirectoryInfo(appDataPath));

            services.AddResponseCompression(opts =>
            {
                opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "application/octet-stream", "application/json" });
            });

            services.AddCors(options =>
            {
                options.AddDefaultPolicy(
                                  builder =>
                                  {

                                      builder.AllowAnyOrigin();
                                      builder.AllowAnyMethod();
                                  });
            });

            // determine whether we require auth via Kerberos/windows auth, can be overridden by env var
            var windowsAuthRequired = true;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // windows auth is required by default
                if (Environment.GetEnvironmentVariable("CERTIFY_SERVICE_AUTH_MODE") == "none")
                {
                    windowsAuthRequired = false;
                }
                else if (Configuration["Service:AuthMode"] == "none")
                {
                    windowsAuthRequired = false;
                }
            }
            else
            {
                // on non-windows platforms we don't support windows auth
                windowsAuthRequired = false;
            }

            services
                    .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
                    .AddScheme<ServiceAuthSchemeOptions, ServiceAuthSchemeHandler>("ServiceAuthScheme", opts => { }) // allow custom service auth when windows auth not used
                    .AddNegotiate(); //add windows auth/kerberos

            services.AddAuthorization(options =>
            {
                // add policy to require admin role claim

                if (windowsAuthRequired)
                {
                    // when using windows auth we require the user to be in the admin group which we check via our ClaimsTransformer
                    options.AddPolicy("CertifyServiceAuth", policy =>
                    {
                        policy.AddAuthenticationSchemes(NegotiateDefaults.AuthenticationScheme);
                        policy.RequireAuthenticatedUser();
                        policy.RequireClaim(ClaimTypes.Role, new[] { "service_admin" });
                    });
                }
                else
                {
                    // when not using windows auth we use our custom service auth scheme
                    options.AddPolicy("CertifyServiceAuth", policy =>
                    {
                        policy.AddAuthenticationSchemes("ServiceAuthScheme");
                        policy.RequireClaim(ClaimTypes.Role, new[] { "service_admin" });
                    });
                }
            });

#if DEBUG
            services.AddEndpointsApiExplorer();

            // Register the Swagger generator, defining 1 or more Swagger documents
            // https://docs.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle?view=aspnetcore-3.1&tabs=visual-studio
            services.AddSwaggerGen(c =>
            {

                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "Certify Core Internal API",
                    Version = "v1",
                    Description = "Provides a private API for use by the Certify The Web UI and related components. This internal API changes between versions, you should use the public API when building integrations instead."
                });

                // declare authorization method
                c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                    Name = "Authorization",
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                    Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http
                });

                // set security requirement
                c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
                {
                    {
                        new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                        {
                            Reference = new Microsoft.OpenApi.Models.OpenApiReference
                            {
                                Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        }, new List<string>()
                    }
                });

            });
#endif

            var useHttps = Configuration["API:Service:UseHttps"] != null ? bool.Parse(Configuration["API:Service:UseHttps"]) : false;

            if (useHttps)
            {
                services.AddHttpsRedirection(options =>
                {
                    options.RedirectStatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status307TemporaryRedirect;
                    options.HttpsPort = 443;
                });
            }

            // add claims transformation service, this is used to optionally check auth requirements
            services.AddSingleton<IClaimsTransformation, ClaimsTransformer>(c => new ClaimsTransformer(windowsAuthRequired));

            // inject instance of certify manager
            var certifyManager = new Management.CertifyManager();
            certifyManager.Init().Wait();

            services.AddSingleton<Management.ICertifyManager>(certifyManager);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var statusHubContext = app.ApplicationServices.GetRequiredService<IHubContext<Service.StatusHub>>();
            if (statusHubContext == null)
            {
                throw new Exception("Status Hub not registered");
            }

            var certifyManager = app.ApplicationServices.GetRequiredService(typeof(ICertifyManager)) as CertifyManager;

            if (certifyManager == null)
            {
                throw new Exception("Certify Manager not registered");
            }

#if DEBUG
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();

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
            }
#endif
            // set status report context provider
            certifyManager.SetStatusReporting(new Service.StatusHubReporting(statusHubContext));

            var useHttps = Configuration["API:Service:UseHttps"] != null ? bool.Parse(Configuration["API:Service:UseHttps"]) : false;

            if (useHttps)
            {
                app.UseHttpsRedirection();
            }

            app.UseRouting();

            // enable authentication middleware 
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseCors();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<Service.StatusHub>("/api/status");
                endpoints.MapControllers();

#if DEBUG
                endpoints.MapGet("/debug/routes", (IEnumerable<EndpointDataSource> endpointSources) =>
                {
                    var sb = new System.Text.StringBuilder();
                    var endpoints = endpointSources.SelectMany(es => es.Endpoints);
                    foreach (var endpoint in endpoints)
                    {
                        if (endpoint is RouteEndpoint routeEndpoint)
                        {
                            sb.AppendLine($"{routeEndpoint.DisplayName} {routeEndpoint.RoutePattern.RawText}");
                        }
                    }

                    return sb.ToString();
                });
#endif
            });
        }
    }
}
