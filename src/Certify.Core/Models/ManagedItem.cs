using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
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

    public enum ManagedItemType
    {
        SSL_LetsEncrypt_LocalIIS = 1,
        SSL_LetsEncrypt_Manual = 2
    }

    public enum RequiredActionType
    {
        NewCertificate,
        ReplaceCertificate,
        KeepCertificate,
        Ignore
    }

    public class ManagedItem
    {
        /// <summary>
        /// Unique ID for this managed item
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Optional grouping ID, such as where mamaged sites share a common IIS site id
        /// </summary>
        public string GroupId { get; set; }

        /// <summary>
        /// Display name for this item, for easier reference
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Optional user notes regarding this item
        /// </summary>
        public string Comments { get; set; }

        /// <summary>
        /// Specific type of item we are managing, affects the renewal/rewuest operations required
        /// </summary>
        public ManagedItemType ItemType { get; set; }
    }

    public class ManagedSite : ManagedItem
    {
        /// <summary>
        /// If true, the auto renewal process will include this item in attempted renewal operations if applicable
        /// </summary>
        public bool IncludeInAutoRenew { get; set; }

        /// <summary>
        /// Host or server where this item is based, usually localhost if managing the local server
        /// </summary>
        public string TargetHost { get; set; }

        /// <summary>
        /// List of configured domains this managed site will include (primary subject or SAN)
        /// </summary>
        public List<DomainOption> DomainOptions { get; set; }

        /// <summary>
        /// Configuration options for this request
        /// </summary>
        public CertRequestConfig RequestConfig { get; set; }

        /// <summary>
        /// Log of recent actions/results for this item
        /// </summary>
        public List<ManagedSiteLogItem> Logs { get; set; }

        public void AppendLog(ManagedSiteLogItem logItem)
        {
            if (this.Logs == null) this.Logs = new List<ManagedSiteLogItem>();
            this.Logs.Add(logItem);
        }
    }

    //TODO: may deprecate, was mainly for preview of setup wizard
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
        public RequiredActionType PlannedAction { get; set; }

        /// <summary>
        /// The primary domain is the main domain listed on the certificate
        /// </summary>
        public bool IsPrimaryCertificateDomain { get; set; }

        /// <summary>
        /// For SAN certificates, indicate if this name is an alternative name to be associated with a primary domain certificate
        /// </summary>
        public bool IsSubjectAlternativeName { get; set; }
    }

    //TODO: deprecated, remove
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