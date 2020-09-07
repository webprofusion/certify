using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Providers;
using Certify.Shared;

namespace Certify.Server.UI.Utils
{
    public class ServiceConfigManager : IServiceConfigProvider
    {
        public ServiceConfig GetServiceConfig()
        {
            return new ServiceConfig();
        }
    }
}
