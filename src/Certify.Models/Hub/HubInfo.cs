namespace Certify.Models.Hub
{
    public class HubInfo
    {
        public string InstanceId { get; set; } = default!;
        public VersionInfo Version { get; set; } = default!;
        public string HubEndpoint { get; set; } = default!;

        public string Message { get; set; } = default!;

        /// <summary>
        /// if set, provides the authenticated caller with a JWT joining token for use in subsequent hub communication
        /// </summary>
        public string JoiningToken { get; set; } = default!;
    }

    public class HubHealth
    {
        public string Status { get; set; } = default!;
        public string Detail { get; set; } = default!;
        public string Version { get; set; } = default!;
        public bool ServiceAvailable { get; set; } = default!;
        public object env { get; set; } = default!;
    }
}
