using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public enum ManagedSiteType
    {
        LocalIIS = 1
    }

    public class ManagedSiteBinding
    {
        public string Hostname { get; set; }
        public int Port { get; set; }

        /// <summary>
        /// IP is either * (all unassigned) or a specific IP
        /// </summary>
        public string IP { get; set; }
        public bool UseSNI { get; set; }
        public string CertName { get; set; }
    }

    public class ManagedSite
    {
        public string SiteId { get; set; }
        public string SiteName { get; set; }
        public string Server { get; set; }

        public ManagedSiteType SiteType { get; set; }
        public List<ManagedSiteBinding> SiteBindings { get; set; }
    }

    public class SiteBindingItem
    {
        public string Description
        {
            get
            {
                return SiteName + " - " + Protocol + "://" + Host + ":" + Port;
            }
        }

        public string SiteId { get; set; }
        public string SiteName { get; set; }
        public string Host { get; set; }
        public string IP { get; set; }
        public string PhysicalPath { get; set; }
        public bool IsHTTPS { get; set; }
        public string Protocol { get; set; }
        public int Port { get; set; }
        public bool HasCertificate { get; set; }
    }
}
