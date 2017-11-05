using Certify.Management;
using Certify.Utils;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
        [SRDescription("ManagedItemType_LocalIIS")]
        SSL_LetsEncrypt_LocalIIS = 1,

        [SRDescription("ManagedItemType_Manual")]
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

    /// <summary>
    /// Base class for data classes used with WPF, with a bubbled IsChanged property
    /// </summary>
    /// <remarks>
    /// Handles any level of nested INotifyPropertyChanged objects (ex: other BindableBase-
    /// derived classes) or INotifyCollectionChanged objects (ex: ObservableCollection)
    /// </remarks>
    public class BindableBase : INotifyPropertyChanged
    {
        /// <summary>
        /// change notification provide by fody on compile, not that subclasses shouldn't inherit 
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged(string prop, object before, object after)
        {
            if (prop != nameof(IsChanged))
            {
                // auto-update the IsChanged property for standard properties
                IsChanged = true;
            }

            // hook up to events
            DetachChangeEventHandlers(before);
            AttachChangeEventHandlers(after);

            // fire the event
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private void AttachChangeEventHandlers(object obj)
        {
            // attach to INotifyPropertyChanged properties
            if (obj is INotifyPropertyChanged prop)
            {
                prop.PropertyChanged += HandleChangeEvent;
            }
            // attach to INotifyCollectionChanged properties
            if (obj is INotifyCollectionChanged coll)
            {
                coll.CollectionChanged += HandleChangeEvent;
            }
        }

        private void DetachChangeEventHandlers(object obj)
        {
            // detach from INotifyPropertyChanged properties
            if (obj is INotifyPropertyChanged prop)
            {
                prop.PropertyChanged -= HandleChangeEvent;
            }
            // detach from INotifyCollectionChanged properties
            if (obj is INotifyCollectionChanged coll)
            {
                coll.CollectionChanged -= HandleChangeEvent;
            }
        }

        private void HandleChangeEvent(object src, EventArgs args)
        {
            IsChanged = true;
            if (args is NotifyCollectionChangedEventArgs ccArgs)
            {
                if (ccArgs.Action == NotifyCollectionChangedAction.Remove)
                {
                    foreach (object obj in ccArgs.OldItems)
                    {
                        DetachChangeEventHandlers(obj);
                    }
                }
                if (ccArgs.Action == NotifyCollectionChangedAction.Add)
                {
                    foreach (object obj in ccArgs.NewItems)
                    {
                        AttachChangeEventHandlers(obj);
                    }
                }
            }
        }

        public void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        /// <summary>
        /// True if a property has been changed on the model since IsChanged was last set to false 
        /// </summary>
        [JsonIgnore] // don't serialize this property to saved settings
        public bool IsChanged
        {
            get { return isChanged; }
            set
            {
                if (!value)
                {
                    UnsetChanged(this);
                }
                else
                {
                    isChanged = value;
                }
            }
        }
        private bool isChanged;

        // recursively unsets IsChanged on a BindableBase object, any property
        // on the object of type BindableBase, and any BindableBase objects
        // nested in ICollection properties
        private void UnsetChanged(object obj)
        {
            if (obj is BindableBase bb)
            {
                bb.isChanged = false;
                var props = obj.GetType().GetProperties();
                foreach (var prop in props.Where(p =>
                    typeof(ICollection).IsAssignableFrom(p.PropertyType) ||
                    p.PropertyType.IsSubclassOf(typeof(BindableBase))))
                {
                    object val = prop.GetValue(obj);
                    if (val is ICollection propertyCollection)
                    {
                        foreach (object subObj in propertyCollection)
                        {
                            UnsetChanged(subObj);
                        }
                    }
                    if (val is BindableBase bbSub)
                    {
                        UnsetChanged(bbSub);
                    }
                }
            }
            if (obj is ICollection collection)
            {
                foreach (object subObj in collection)
                {
                    UnsetChanged(subObj);
                }
            }
        }
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
        public bool CertificateRevoked { get; set; }

        public override string ToString()
        {
            return $"[{Id ?? "null"}]: \"{Name}\"";
        }
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
        public ObservableCollection<DomainOption> DomainOptions { get; private set; }

        /// <summary>
        /// Configuration options for this request 
        /// </summary>
        public CertRequestConfig RequestConfig { get; set; }
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