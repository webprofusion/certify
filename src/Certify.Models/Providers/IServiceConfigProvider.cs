using Certify.Shared;

namespace Certify.Providers
{
    public interface IServiceConfigProvider
    {
        ServiceConfig GetServiceConfig();
    }
}
