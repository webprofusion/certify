using Microsoft.Extensions.Configuration;

namespace Certify.Server.Worker
{
    public class Startup : Certify.Server.Core.Startup
    {
        public Startup(IConfiguration configuration) : base(configuration)
        {
            // base startup performs most of the configuration in this instance
        }
    }
}
