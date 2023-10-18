using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Certify.Server.API
{
    /// <summary>
    /// API Server hosting
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Entry point for API host
        /// </summary>
        /// <param name="args"></param>
        public static void Main(string[] args)
        {

            CreateHostBuilder(args).Build().Run();
        }

        /// <summary>
        /// Build hosting for API
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
