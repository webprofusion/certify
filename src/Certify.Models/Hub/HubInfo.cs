namespace Certify.Models.Hub
{
    public class HubInfo
    {
        public string InstanceId { get; set; } = default!;

        public VersionInfo Version { get; set; } = default!;
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
