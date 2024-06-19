using System;
using System.Collections.Generic;
using Certify.Models;

namespace Certify.API.Management
{
    public class ManagedInstanceInfo
    {
        public string InstanceId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new List<string>();
        public DateTimeOffset LastReported { get; set; }
    }

    public class ManagedInstanceItems
    {
        public string InstanceId { get; set; } = string.Empty;
        public List<ManagedCertificate> Items { get; set; } = new List<ManagedCertificate>();
    }
}
