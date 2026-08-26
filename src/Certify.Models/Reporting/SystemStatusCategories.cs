using System;

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
        public const string HUB_API_STARTUP_CUSTOMCONFIG = "hub.api.startup.customconfig";
        public const string HUB_API_STARTUP_JWTSECRET = "hub.api.startup.jwtsecret";
        public const string HUB_API_STARTUP_APIDOCS = "hub.api.startup.apidocs";
        public const string HUB_API_STARTUP_SVC_STATUS_STREAM = "hub.api.startup.svc.stream";

        public const string SERVICE_CORE_PLATFORM = "service.core.platform";
        public const string SERVICE_CORE_APPSETTINGS = "service.core.appsettings";
        public const string SERVICE_CORE_SVCCONFIG = "service.core.svcconfig";
        public const string SERVICE_CORE_LOADPLUGINS = "service.core.loadplugins";
        public const string SERVICE_CORE_DATASTORE_INIT = "service.core.datastore.init";
        public const string SERVICE_CORE_DATASTORE_STATUS = "service.core.datastore.status";
        public const string SERVICE_CORE_CA_CUSTOM_LOAD = "service.core.ca.custom.load";
        public const string SERVICE_CORE_HUB_JOINING_KEY = "service.core.hub.joining.key";
        public const string SERVICE_CORE_HUB_JOINING_AUTH = "service.core.hub.joining.auth";
        public const string SERVICE_CORE_HUB_CONNECTION = "service.core.hub.connection";

    }

    /// <summary>
    /// Represents the current status of the data store connection
    /// </summary>
    public class DataStoreStatus
    {
        /// <summary>
        /// True if the data store is connected and operational
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// True if the service is running in degraded mode due to data store issues
        /// </summary>
        public bool IsDegradedMode { get; set; }

        /// <summary>
        /// Description of the current status or error
        /// </summary>
        public string StatusMessage { get; set; } = string.Empty;

        /// <summary>
        /// The ID of the data store connection being used
        /// </summary>
        public string? DataStoreId { get; set; }

        /// <summary>
        /// The type of data store (e.g., sqlite, postgres, sqlserver)
        /// </summary>
        public string? DataStoreType { get; set; }

        /// <summary>
        /// When the last successful connection was made
        /// </summary>
        public DateTimeOffset? LastSuccessfulConnection { get; set; }

        /// <summary>
        /// When the last error occurred
        /// </summary>
        public DateTimeOffset? LastErrorTime { get; set; }

        /// <summary>
        /// The error message from the last failure
        /// </summary>
        public string? LastErrorMessage { get; set; }

        /// <summary>
        /// Number of consecutive failures
        /// </summary>
        public int ConsecutiveFailures { get; set; }
    }
}
