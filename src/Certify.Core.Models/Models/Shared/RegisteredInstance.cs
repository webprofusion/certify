using System;

namespace Certify.Models.Shared
{
    public class RegisteredInstance
    {
        public string UserProfileId { get; set; }
        public string InstanceId { get; set; }
        public string MachineName { get; set; }
        public string OS { get; set; }
        public string AppVersion { get; set; }
        public int Websites { get; set; }
        public int ManagedSites { get; set; }
        public DateTime? DateRegistered { get; set; }
        public DateTime? DateLastConfigSync { get; set; }
    }
}