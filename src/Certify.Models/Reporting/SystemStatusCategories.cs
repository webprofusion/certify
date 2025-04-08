namespace Certify.Models.Reporting
{
    public class SystemStatusCategories
    {
        public const string HUB_API = "hub.api";

        public const string SERVICE_CORE = "service.core";

    }

    public class SystemStatusKeys
    {
        public const string HUB_API_MODE = "hub.api.mode";
        public const string HUB_API_STARTUP_READSVCCONFIG = "hub.api.startup.readserviceconfig";
        public const string HUB_API_STARTUP_SVCHOSTENV = "hub.api.startup.servicehostenv";
        public const string HUB_API_STARTUP_SVCPORTENV = "hub.api.startup.serviceportenv";
        public const string HUB_API_STARTUP_ENVIRONMENT = "hub.api.startup.environment";
        public const string HUB_API_STARTUP_URL = "hub.api.startup.url";
        public const string HUB_API_STARTUP_SWAGGER = "hub.api.startup.swagger";
        public const string HUB_API_STARTUP_SVC_STATUS_STREAM = "hub.api.startup.svc.stream";

        public const string SERVICE_CORE_PLATFORM = "service.core.platform";
        public const string SERVICE_CORE_APPSETTINGS = "service.core.appsettings";
        public const string SERVICE_CORE_SVCCONFIG = "service.core.svcconfig";
        public const string SERVICE_CORE_LOADPLUGINS = "service.core.loadplugins";
        public const string SERVICE_CORE_DATASTORE_INIT = "service.core.datastore.init";
        public const string SERVICE_CORE_CA_CUSTOM_LOAD = "service.core.ca.custom.load";
        public const string SERVICE_CORE_HUB_JOINING_KEY = "service.core.hub.joining.key";
        public const string SERVICE_CORE_HUB_JOINING_AUTH = "service.core.hub.joining.auth";

    }
}
