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
        private const string ServiceAuthScheme = "ServiceAuthScheme";
        private const string CertifyServiceAuthPolicy = "CertifyServiceAuth";
        private const string SwaggerDocTitle = "Certify Agent Service Internal API";
        private const string SwaggerDocVersion = "v1";
        private const string SwaggerDocDescription = "Provides a private API for use by the Certify The Web Desktop UI and related components. This internal API changes between versions, you should use the public Hub API when building integrations instead.";

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public async Task ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();

            ConfigureSignalR(services);
            ConfigureDataProtection(services);
            ConfigureResponseCompression(services);
            ConfigureCors(services);
            ConfigureAuthentication(services);
            ConfigureAuthorization(services);
#if DEBUG
            ConfigureSwagger(services);
#endif
            ConfigureHttpsRedirection(services);
            ConfigureClaimsTransformation(services);
            await ConfigureCertifyManager(services);
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var statusHubContext = app.ApplicationServices.GetRequiredService<IHubContext<Service.StatusHub>>()
                                   ?? throw new Exception("Status Hub not registered");

            var certifyManager = app.ApplicationServices.GetRequiredService<ICertifyManager>() as CertifyManager
                                 ?? throw new Exception("Certify Manager not registered");

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
#if DEBUG
                ConfigureSwaggerUI(app);
#endif
            }

            certifyManager.SetStatusReporting(new Service.StatusHubReporting(statusHubContext));

            if (bool.TryParse(Configuration["API:Service:UseHttps"], out var useHttps) && useHttps)
            {
                app.UseHttpsRedirection();
            }

            app.UseRouting();
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

        private void ConfigureSignalR(IServiceCollection services)
        {
            services.AddSignalR().AddMessagePackProtocol();
        }

        private void ConfigureDataProtection(IServiceCollection services)
        {
            var appDataPath = EnvironmentUtil.CreateAppDataPath("keys");
            services.AddDataProtection(a => a.ApplicationDiscriminator = "certify")
                    .PersistKeysToFileSystem(new DirectoryInfo(appDataPath));
        }

        private void ConfigureResponseCompression(IServiceCollection services)
        {
            services.AddResponseCompression(opts =>
            {
                opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "application/octet-stream", "application/json" });
            });
        }

        private void ConfigureCors(IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddDefaultPolicy(builder =>
                {
                    builder.AllowAnyOrigin().AllowAnyMethod();
                });
            });
        }

        private void ConfigureAuthentication(IServiceCollection services)
        {
            services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
                    .AddScheme<ServiceAuthSchemeOptions, ServiceAuthSchemeHandler>(ServiceAuthScheme, opts => { })
                    .AddNegotiate();
        }

        private void ConfigureAuthorization(IServiceCollection services)
        {
            var windowsAuthRequired = DetermineWindowsAuthRequired();

            services.AddAuthorization(options =>
            {
                options.AddPolicy(CertifyServiceAuthPolicy, policy =>
                {
                    if (windowsAuthRequired)
                    {
                        policy.AddAuthenticationSchemes(NegotiateDefaults.AuthenticationScheme);
                        policy.RequireAuthenticatedUser();
                    }
                    else
                    {
                        // apply custom auth scheme for service auth
                        policy.AddAuthenticationSchemes(ServiceAuthScheme);
                    }

                    policy.RequireClaim(ClaimTypes.Role, "service_admin");
                });
            });
        }

#if DEBUG
        private void ConfigureSwagger(IServiceCollection services)
        {

            services.AddEndpointsApiExplorer();

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc(SwaggerDocVersion, new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = SwaggerDocTitle,
                    Version = SwaggerDocVersion,
                    Description = SwaggerDocDescription
                });

                c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                    Name = "Authorization",
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                    Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http
                });

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

        }

        private void ConfigureSwaggerUI(IApplicationBuilder app)
        {
            app.UseSwagger();

            app.UseSwaggerUI(c =>
            {
                c.RoutePrefix = "docs";
                c.DocumentTitle = "Certify Core Server API";
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Certify Core Server API");
            });
        }
#endif

        private void ConfigureHttpsRedirection(IServiceCollection services)
        {
            if (bool.TryParse(Configuration["API:Service:UseHttps"], out var useHttps) && useHttps)
            {
                services.AddHttpsRedirection(options =>
                {
                    options.RedirectStatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status307TemporaryRedirect;
                    options.HttpsPort = 443;
                });
            }
        }

        private void ConfigureClaimsTransformation(IServiceCollection services)
        {
            var windowsAuthRequired = DetermineWindowsAuthRequired();
            services.AddSingleton<IClaimsTransformation, ClaimsTransformer>(c => new ClaimsTransformer(windowsAuthRequired));
        }

        private async Task ConfigureCertifyManager(IServiceCollection services)
        {
            var certifyManager = new Management.CertifyManager();
            await certifyManager.Init();
            services.AddSingleton<Management.ICertifyManager>(certifyManager);
        }

        private bool DetermineWindowsAuthRequired()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Environment.GetEnvironmentVariable("CERTIFY_SERVICE_AUTH_MODE") != "none" &&
                       Configuration["Service:AuthMode"] != "none";
            }

            return false;
        }
    }
}
