using Certify.Server.Hub.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Certify.Server.Hub.Api.Extensions
{
    /// <summary>
    /// Extension methods for configuring ACME services
    /// </summary>
    public static class AcmeServiceExtensions
    {
        /// <summary>
        /// Adds ACME background task processing services to the service collection
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddAcmeBackgroundServices(this IServiceCollection services)
        {
            // Register the background task service as a singleton
            services.AddSingleton<AcmeBackgroundTaskService>();
            
            // Register it as a hosted service so it starts with the application
            services.AddHostedService<AcmeBackgroundTaskService>(provider => 
                provider.GetRequiredService<AcmeBackgroundTaskService>());

            return services;
        }

        /// <summary>
        /// Adds all ACME helper services to the service collection
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddAcmeHelperServices(this IServiceCollection services)
        {
            // Register helper services as scoped (per request)
            services.AddScoped<AcmeJwsValidator>();
            services.AddScoped<AcmeExternalAccountBindingValidator>();
            services.AddScoped<AcmeHelper>();

            return services;
        }

        /// <summary>
        /// Adds all ACME services to the service collection
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddAcmeServices(this IServiceCollection services)
        {
            services.AddAcmeBackgroundServices();
            services.AddAcmeHelperServices();

            return services;
        }
    }
}
