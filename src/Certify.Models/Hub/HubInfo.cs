namespace Certify.Models.Hub
{
    public class HubInfo
    {
        public string InstanceId { get; set; }

        public VersionInfo Version { get; set; }
    }

    public class HubHealth
    {
        public string Status { get; set; }
        public string Detail { get; set; }
        public string Version { get; set; }
        public bool ServiceAvailable { get; set; }
        public object env { get; set; }
    }
}
