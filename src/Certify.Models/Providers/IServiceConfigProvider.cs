using System;
using System.Collections.Generic;
using System.Text;
using Certify.Shared;

namespace Certify.Providers
{
    public interface IServiceConfigProvider
    {
        ServiceConfig GetServiceConfig();
    }
}
