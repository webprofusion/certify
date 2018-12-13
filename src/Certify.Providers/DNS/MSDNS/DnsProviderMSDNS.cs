using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Providers.DNS.MSDNS
{
    public class DnsProviderMSDNS : IDnsProvider
    {
        private string _username;
        private string _domain;
        private string _password;
        private string _server;
        private ILog _log;

        public DnsProviderMSDNS(Dictionary<string, string> credentials)
        {
            _server = credentials["dnsservername"];
            _username = credentials["username"];
            _password = credentials["password"];
            _domain = credentials["domain"];
        }

        public static ProviderDefinition Definition
        {
            get
            {
                return new ProviderDefinition
                {
                    Id = "DNS01.API.MSDNS",
                    Title = "Microsoft DNS API",
                    Description = "Validates via local Microsoft DNS APIs using credentials",
                    HelpUrl = "https://docs.microsoft.com/en-us/windows/desktop/dns/dns-wmi-classes",
                    PropagationDelaySeconds = 5,
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{ Key="dnsservername", Name="Server Name", IsRequired=true },
                        new ProviderParameter{ Key="username", Name="User Name", IsRequired=false, IsCredential = true},
                        new ProviderParameter{ Key="password", Name="Password", IsRequired = false, IsPassword = true},
                        new ProviderParameter{ Key="domain", Name="Domain", IsRequired = false}
                    },
                    ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.MSDNS",
                    HandlerType = ChallengeHandlerType.INTERNAL
                };
            }
        }

        public int PropagationDelaySeconds => DnsProviderMSDNS.Definition.PropagationDelaySeconds;

        public string ProviderId => DnsProviderMSDNS.Definition.Id;

        public string ProviderTitle => DnsProviderMSDNS.Definition.Title;

        public string ProviderDescription => DnsProviderMSDNS.Definition.Description;

        public string ProviderHelpUrl => DnsProviderMSDNS.Definition.HelpUrl;

        public List<ProviderParameter> ProviderParameters => DnsProviderMSDNS.Definition.ProviderParameters;

        public Task<ActionResult> CreateRecord(DnsRecord request)
        {
            ManagementScope mgmtScope = this.EstablishScope();
            mgmtScope.Connect();
            string strQuery = string.Format("SELECT * FROM MicrosoftDNS_TXTType WHERE OwnerName = '{0}'", request.RecordName);
            ManagementObjectSearcher mgmtSearch = new ManagementObjectSearcher(mgmtScope, new ObjectQuery(strQuery));
            ManagementObjectCollection mgmtDNSRecords = mgmtSearch.Get();
            ManagementClass mgmtClass = new ManagementClass(mgmtScope, new ManagementPath("MicrosoftDNS_TXTType"), null);
            ManagementBaseObject mgmtParams = mgmtClass.GetMethodParameters("CreateInstanceFromPropertyData");
            mgmtParams["DnsServerName"] = Environment.MachineName;
            mgmtParams["ContainerName"] = request.RecordName.Split('.')[request.RecordName.Split('.').Count() - 2] + "." + request.RecordName.Split('.')[request.RecordName.Split('.').Count() - 1];
            mgmtParams["OwnerName"] = request.RecordName;
            mgmtParams["DescriptiveText"] = request.RecordValue;
            mgmtClass.InvokeMethod("CreateInstanceFromPropertyData", mgmtParams, null);

            return new Task<ActionResult>(() => new ActionResult { IsSuccess = true, Message = "DNS record updated" });
        }

        public Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            ManagementScope mgmtScope = this.EstablishScope();
            mgmtScope.Connect();
            string strQuery = string.Format("SELECT * FROM MicrosoftDNS_TXTType WHERE OwnerName = '{0}' AND DescriptiveText = '{1}'", request.RecordName, request.RecordValue);
            ManagementObjectSearcher mgmtSearch = new ManagementObjectSearcher(mgmtScope, new ObjectQuery(strQuery));
            ManagementObjectCollection mgmtDNSRecords = mgmtSearch.Get();
			foreach(ManagementObject delMe in mgmtDNSRecords)
            {
                delMe.Delete();
            }

            return Task.FromResult(new ActionResult { IsSuccess = true, Message = "DNS record removed." });
        }

        public Task<List<DnsZone>> GetZones()
        {
            var zones = new List<DnsZone>();

            ManagementScope mgmtScope = this.EstablishScope();
            mgmtScope.Connect();
            string strQuery = "SELECT * FROM MicrosoftDNS_Zone";
            ManagementObjectSearcher mgmtSearch = new ManagementObjectSearcher(mgmtScope, new ObjectQuery(strQuery));
            ManagementObjectCollection mgmtDNSZones = mgmtSearch.Get();
            ManagementClass mgmtClass;
            foreach (ManagementObject zone in mgmtDNSZones)
            {
                string zoneName = zone.InvokeMethod("GetDistinguishedName", new object[0]) as string;
                zones.Add(new DnsZone() { ZoneId = zoneName, Name = zoneName });
            }
            return Task.FromResult(zones);
        }

        public async Task<bool> InitProvider(ILog log = null)
        {
            _log = log;
            return await Task.FromResult(true);
        }

        public Task<ActionResult> Test()
        {
            return Task.FromResult(new ActionResult { IsSuccess = true, Message = "Test Completed OK." });
        }

		private ManagementScope EstablishScope()
        {
            return new ManagementScope(@"\\\\" + this._server + "\\Root\\MicrosoftDNS", new ConnectionOptions("MS_409", this._username, this._password, "ntlmdomain:" + this._domain, System.Management.ImpersonationLevel.Impersonate, System.Management.AuthenticationLevel.Default, true, null, System.TimeSpan.MaxValue));
        }
    }
}
