using Certify.Management;
using PropertyChanged;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public enum LogItemType
    {
        GeneralInfo = 1,
        GeneralWarning = 10,
        GeneralError = 20,
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
        [Description("Local IIS, SSL Certificate via Let's Encrypt")]
        SSL_LetsEncrypt_LocalIIS = 1,

        [Description("Manual SSL Certificate via Let's Encrypt")]
        SSL_LetsEncrypt_Manual = 2
    }

    public enum RequiredActionType
    {
        NewCertificate,
        ReplaceCertificate,
        KeepCertificate,
        Ignore
    }

    public class ManagedSiteLog
    {
        public ManagedSiteLog()
        {
            this.Logs = new List<ManagedSiteLogItem>();
        }

        /// <summary>
        /// Log of recent actions/results for this item 
        /// </summary>
        public List<ManagedSiteLogItem> Logs { get; set; }

        public static string GetLogPath(string managedItemId)
        {
            return Util.GetAppDataFolder() + "\\logs\\log_" + managedItemId.Replace(':', '_') + ".txt";
        }

        public static void AppendLog(string managedItemId, ManagedSiteLogItem logItem)
        {
            //FIXME:
            var logPath = GetLogPath(managedItemId);

            var log = new LoggerConfiguration()
                .WriteTo.File(logPath, shared: true)
                .CreateLogger();

            var logLevel = Serilog.Events.LogEventLevel.Information;
            if (logItem.LogItemType == LogItemType.CertficateRequestFailed) logLevel = Serilog.Events.LogEventLevel.Error;
            if (logItem.LogItemType == LogItemType.GeneralError) logLevel = Serilog.Events.LogEventLevel.Error;
            if (logItem.LogItemType == LogItemType.GeneralWarning) logLevel = Serilog.Events.LogEventLevel.Warning;

            log.Write(logLevel, logItem.Message);
            //TODO: log to per site log
            //if (this.Logs == null) this.Logs = new List<ManagedSiteLogItem>();
            //this.Logs.Add(logItem);
        }
    }

    public class BindableBase : INotifyPropertyChanged
    {
        /// <summary>
        /// change notification provide by fody on compile, not that subclasses shouldn't inherit 
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged(string propertyName, object before, object after)
        {
            //Perform property validation
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        /// <summary>
        /// True if a property has been changed on the model since IsChanged was last set to false 
        /// </summary>
        public bool IsChanged { get; set; }
    }

    public class ManagedItem : BindableBase
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

        public DateTime? DateStart { get; set; }
        public DateTime? DateExpiry { get; set; }
        public DateTime? DateRenewed { get; set; }

        public string CertificateId { get; set; }
        public string CertificatePath { get; set; }
    }

    public class ManagedSite : ManagedItem
    {
        public ManagedSite()
        {
            this.Name = "New Managed Site";
            this.IncludeInAutoRenew = true;

            this.DomainOptions = new ObservableCollection<DomainOption>();
            this.RequestConfig = new CertRequestConfig();
            this.RequestConfig.EnableFailureNotifications = true;

            this.RequestConfig.PropertyChanged += RequestConfig_PropertyChanged;
        }

        private void RequestConfig_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            //when RequestConfig IsChanged also mark ManagedItem as changed
            if (RequestConfig.IsChanged) IsChanged = true;
        }

        /// <summary>
        /// If true, the auto renewal process will include this item in attempted renewal operations
        /// if applicable
        /// </summary>
        public bool IncludeInAutoRenew { get; set; }

        /// <summary>
        /// Host or server where this item is based, usually localhost if managing the local server 
        /// </summary>
        public string TargetHost { get; set; }

        /// <summary>
        /// List of configured domains this managed site will include (primary subject or SAN) 
        /// </summary>
        public ObservableCollection<DomainOption> DomainOptions { get; set; }

        /// <summary>
        /// Configuration options for this request 
        /// </summary>
        public CertRequestConfig RequestConfig { get; set; }

        public void AddDomainOption(DomainOption domainOption)
        {
            if (this.DomainOptions == null) this.DomainOptions = new ObservableCollection<DomainOption>();

            domainOption.PropertyChanged += DomainOption_PropertyChanged;

            this.DomainOptions.Add(domainOption);
        }

        internal void DomainOption_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            //if a domain option has changed then the parent managed item gets marked as changed as well
            this.IsChanged = true;
        }

        public void ClearDomainOptions()
        {
            this.DomainOptions = new ObservableCollection<DomainOption>();
        }

        public void AddDomainOptions(List<DomainOption> domainOptions)
        {
            // add list of domain options, this in turn wires up change notifications
            foreach (var d in domainOptions)
            {
                this.AddDomainOption(d);
            }

            RaisePropertyChanged(nameof(DomainOptions));
        }
    }

    //TODO: may deprecate, was mainly for preview of setup wizard
    public class ManagedSiteBinding
    {
        public string Hostname { get; set; }
        public int? Port { get; set; }

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
        /// For SAN certificates, indicate if this name is an alternative name to be associated with
        /// a primary domain certificate
        /// </summary>
        public bool IsSubjectAlternativeName { get; set; }
    }

    //TODO: deprecate and remove
    public class SiteBindingItem
    {
        public string SiteId { get; set; }
        public string SiteName { get; set; }
        public string Host { get; set; }
        public string IP { get; set; }
        public string PhysicalPath { get; set; }
        public bool IsHTTPS { get; set; }
        public string Protocol { get; set; }
        public int? Port { get; set; }
        public bool HasCertificate { get; set; }

        public bool IsEnabled { get; set; }
    }
}