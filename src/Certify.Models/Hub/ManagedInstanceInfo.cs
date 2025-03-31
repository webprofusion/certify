using System;
using System.Collections.Generic;

namespace Certify.Models.Hub
{
    public class ManagedInstanceInfo : ConfigurationStoreItem
    {
        /// <summary>
        /// Instance Id is the unique identifier for this instance assigned by the Hub, not the clients own generated instance id
        /// </summary>
        public string InstanceId { get; set; } = string.Empty;

        public string OS { get; set; } = string.Empty;
        public string OSVersion { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;

        public List<ItemTag> Tags { get; set; } = [];
        public DateTimeOffset DateLastReported { get; set; }
        public DateTimeOffset DateRegistered { get; set; }

        public string ConnectionStatus { get; set; } = string.Empty;
        public bool IsAuthenticated { get; set; }
    }

    public class ConnectionStatus
    {
        public const string Connected = "connected";
        public const string Disconnected = "disconnected";
        public const string Away = "away";
    }
    public class ManagedInstanceItems
    {
        public string InstanceId { get; set; } = string.Empty;
        public List<ManagedCertificate> Items { get; set; } = new List<ManagedCertificate>();
    }
}
