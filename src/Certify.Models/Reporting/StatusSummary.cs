namespace Certify.Models.Reporting
{
    public class StatusSummary : BindableBase
    {
        public string InstanceId { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Healthy { get; set; }
        public int Error { get; set; }
        public int Warning { get; set; }
        public int AwaitingUser { get; set; }
        public int InvalidConfig { get; set; }

        public int NoCertificate { get; set; }

        public int ExternallyManaged { get; set; }
        public int TotalDomains { get; set; }
        
        /// <summary>
        /// Incrementing ID that changes whenever a managed certificate is added, updated, or deleted.
        /// Used by the hub to detect when a full refresh is needed.
        /// </summary>
        public long LastUpdateId { get; set; }
    }   
}
