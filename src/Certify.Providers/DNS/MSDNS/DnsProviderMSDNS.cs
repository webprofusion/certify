using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;

namespace Certify.Providers.DNS.MSDNS
{
    public class DnsProviderMSDNS : IDnsProvider
    {
        private readonly string _serverip;
        private int? _customPropagationDelay = null;
        private readonly string _username;
        private readonly string _domain;
        private SecureString _password;
        private readonly string _server;
        private readonly string _serverConnectionName;
        private ILog _log;
        private readonly PasswordAuthenticationMechanism _authMechanism = PasswordAuthenticationMechanism.NtlmDomain;

        public DnsProviderMSDNS(Dictionary<string, string> credentials, Dictionary<string, string> parameters)
        {
            _server = parameters.ContainsKey("dnsservername") ? parameters["dnsservername"] : Environment.MachineName;
            _serverip = parameters.ContainsKey("ipaddress") ? parameters["ipaddress"] : null;
            _serverConnectionName = string.IsNullOrEmpty(_serverip) ? _server : _serverip;
            _username = credentials.ContainsKey("username") ? credentials["username"] : null;
            if (credentials.ContainsKey("password"))
            {
                _password = new SecureString();
                credentials["password"].ToList().ForEach(o => _password.AppendChar(o));
                _password.MakeReadOnly();
            }
            else
            {
                _password = null;
            }
            _domain = credentials.ContainsKey("domain") ? credentials["domain"] : null;
            _customPropagationDelay = credentials.ContainsKey("propagationdelay") ? Convert.ToInt32(credentials["propagationdelay"]) : (int?)null;

            if (credentials.ContainsKey("authentication"))
            {
                switch (credentials["authentication"].ToLowerInvariant())
                {
                    case "basic":
                        _authMechanism = PasswordAuthenticationMechanism.Basic;
                        break;
                    case "credssp":
                        _authMechanism = PasswordAuthenticationMechanism.CredSsp;
                        break;
                    case "default":
                        _authMechanism = PasswordAuthenticationMechanism.Default;
                        break;
                    case "digest":
                        _authMechanism = PasswordAuthenticationMechanism.Digest;
                        break;
                    case "kerberos":
                        _authMechanism = PasswordAuthenticationMechanism.Kerberos;
                        break;
                    case "negotiate":
                        _authMechanism = PasswordAuthenticationMechanism.Negotiate;
                        break;
                    default:
                        _authMechanism = PasswordAuthenticationMechanism.NtlmDomain;
                        break;
                }
            }
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
                        new ProviderParameter{ Key="dnsservername", Name="Server Name", IsRequired=true, IsCredential=false, Value=Environment.MachineName },
                        new ProviderParameter{ Key="ipaddress", Name="DNS Server IP Address", IsRequired=false, IsCredential=false},
                        new ProviderParameter{ Key="username", Name="User Name", IsRequired=false, IsCredential = true, IsPassword = false },
                        new ProviderParameter{ Key="password", Name="Password", IsRequired = false, IsCredential = true, IsPassword = true},
                        new ProviderParameter{ Key="domain", Name="Domain", IsRequired = false, IsCredential = true, IsPassword = false},
                        new ProviderParameter{ Key="authentication", Name="Authentication", IsRequired = false, IsCredential = true, IsPassword = false, Description="Must be one of the following: Basic, CredSsp, Default, Digest, Kerberos, Negotiate, NtlmDomain", Value="NtlmDomain" },
                        new ProviderParameter{ Key="propagationdelay",Name="Propagation Delay Seconds (optional)", IsRequired=false, IsPassword=false, Value="60", IsCredential=false },
                    },
                    ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.MSDNS",
                    HandlerType = ChallengeHandlerType.INTERNAL
                };
            }
        }

        public int PropagationDelaySeconds => (_customPropagationDelay != null ? (int)_customPropagationDelay : Definition.PropagationDelaySeconds);

        public string ProviderId => DnsProviderMSDNS.Definition.Id;

        public string ProviderTitle => DnsProviderMSDNS.Definition.Title;

        public string ProviderDescription => DnsProviderMSDNS.Definition.Description;

        public string ProviderHelpUrl => DnsProviderMSDNS.Definition.HelpUrl;

        public List<ProviderParameter> ProviderParameters => DnsProviderMSDNS.Definition.ProviderParameters;

        public Task<ActionResult> CreateRecord(DnsRecord request)
        {
            var session = CreateCimSession();
            var parameters = new CimMethodParametersCollection
            {
                CimMethodParameter.Create("DnsServerName", _server, CimType.String, CimFlags.None),
                CimMethodParameter.Create("ContainerName", request.RecordName.Split('.')[request.RecordName.Split('.').Count() - 2] + "." + request.RecordName.Split('.')[request.RecordName.Split('.').Count() - 1], CimType.String, CimFlags.None),
                CimMethodParameter.Create("OwnerName", request.RecordName, CimType.String, CimFlags.None),
                CimMethodParameter.Create("DescriptiveText", request.RecordValue, CimType.String, CimFlags.None)
            };
            session.InvokeMethod(@"root\MicrosoftDNS", "MicrosoftDNS_TXTType", "CreateInstanceFromPropertyData", parameters);

            return new Task<ActionResult>(() => new ActionResult { IsSuccess = true, Message = "DNS record updated" });
        }

        public Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            var session = CreateCimSession();
            var strQuery = string.Format("SELECT * FROM MicrosoftDNS_TXTType WHERE OwnerName = '{0}' AND DescriptiveText = '{1}'", request.RecordName, request.RecordValue);
            var txtRecords = session.QueryInstances(@"root\MicrosoftDNS", "WQL", strQuery);
            foreach (var txtRecord in txtRecords)
            {
                session.DeleteInstance(txtRecord);
            }

            return Task.FromResult(new ActionResult { IsSuccess = true, Message = "DNS record removed." });
        }

        public Task<List<DnsZone>> GetZones()
        {
            var zones = new List<DnsZone>();

            var session = CreateCimSession();
            var strQuery = "SELECT * FROM MicrosoftDNS_Zone";
            var dnsZones = session.QueryInstances(@"root\MicrosoftDNS", "WQL", strQuery);
            foreach (var dnsZone in dnsZones)
            {
                var result = session.InvokeMethod(dnsZone, "GetDistinguishedName", null);
                zones.Add(new DnsZone() { ZoneId = result.ReturnValue.Value as string, Name = result.ReturnValue.Value as string });
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
            var session = CreateCimSession();
            if (session.TestConnection())
            {
                return Task.FromResult(new ActionResult { IsSuccess = true, Message = "Test Completed OK." });
            }
            else
            {
                return Task.FromResult(new ActionResult { IsSuccess = false, Message = "Cim Session failed to establish." });
            }
        }

        private CimSession CreateCimSession()
        {

            var options = new WSManSessionOptions();
            if (!string.IsNullOrEmpty(_username))
            {
                options.AddDestinationCredentials(new CimCredential(_authMechanism, _domain, _username, _password));
                options.UseSsl = true;
            }
            return CimSession.Create(_serverConnectionName, options);
        }
    }
}
