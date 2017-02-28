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

    public enum PlannedActionType
    {
        NewCertificate,
        ReplaceCertificate,
        KeepCertificate,
        Ignore
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
        public PlannedActionType PlannedAction { get; set; }

        /// <summary>
        /// The primary domain is the main domain listed on the certificate
        /// </summary>
        public bool IsPrimaryCertificateDomain { get; set; }

        /// <summary>
        /// For SAN certificates, indicate if this name is an alternative name to be associated with a primary domain certificate
        /// </summary>
        public bool IsSubjectAlternativeName { get; set; }
    }

    public enum LogItemType
    {
        CertificateRequestStarted = 50,
        CertificateRequestSuccessful = 100,
        CertficateRequestFailed = 101,
        CertficateRequestAttentionRequired = 110
    }

    public class ManagedSiteLogItem
    {
        public DateTime EventDate { get; set; }
        public string Message { get; set; }
        public LogItemType LogItemType { get; set; }
    }

    public class ManagedSite
    {
        public string SiteId { get; set; }
        public string SiteName { get; set; }
        public string Server { get; set; }
        public bool IncludeInAutoRenew { get; set; }

        public ManagedSiteType SiteType { get; set; }
        public List<ManagedSiteBinding> SiteBindings { get; set; }
        public List<ManagedSiteLogItem> Logs { get; set; }
        public List<DomainOption> DomainOptions { get; set; }
        public CertRequestConfig RequestConfig { get; set; }

        public void AppendLog(ManagedSiteLogItem logItem)
        {
            if (this.Logs == null) this.Logs = new List<ManagedSiteLogItem>();
            this.Logs.Add(logItem);
        }
    }

    public class SiteBindingItem
    {
        public string Description
        {
            get
            {
                if (Host != null)
                {
                    return SiteName + " - " + Protocol + "://" + Host + ":" + Port;
                }
                else
                {
                    return SiteName;
                }
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