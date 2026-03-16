namespace Certify.Models.Hub
{
    public class HubInfo
    {
        public string InstanceId { get; set; } = default!;
        public VersionInfo Version { get; set; } = default!;

        public bool IsLicensed { get; set; }
    }

    public class HubJoiningInfo
    {
        public string HubAssignedInstanceId { get; set; } = default!;
        public VersionInfo Version { get; set; } = default!;
        public string HubEndpoint { get; set; } = default!;

        public string Message { get; set; } = default!;

        /// <summary>
        /// if set, provides the authenticated caller with a JWT joining token for use in subsequent hub communication
        /// </summary>
        public string JoiningToken { get; set; } = default!;

        /// <summary>
        /// Per-instance request authentication secret issued during join/rejoin for privileged instance-authenticated requests.
        /// </summary>
        public string RequestAuthSecret { get; set; } = string.Empty;

        public bool RejoinRequired { get; set; } = false;

        /// <summary>
        /// True when the joining request presented a known hub-assigned instance id and the hub reused existing instance identity.
        /// </summary>
        public bool IsKnownInstance { get; set; }
    }

    public class HubHealth
    {
        /// <summary>
        /// "OK" if all systems are operational, "Degraded" if a subsystem (e.g. data store) is unavailable
        /// </summary>
        public string Status { get; set; } = default!;
        /// <summary>
        /// Human-readable detail about the current status, populated when Status is not "OK"
        /// </summary>
        public string Detail { get; set; } = default!;
        public string Version { get; set; } = default!;
        public bool ServiceAvailable { get; set; } = default!;
        /// <summary>
        /// True when the backing data store is connected and operational
        /// </summary>
        public bool IsDataStoreAvailable { get; set; }
        public object env { get; set; } = default!;
    }
}
