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
        private readonly int? _customPropagationDelay = null;
        private readonly string _username;
        private readonly string _domain;
        private SecureString _password;
        private readonly string _server;
        private readonly string _serverConnectionName;
        private ILog _log;
        private readonly PasswordAuthenticationMechanism _authMechanism = PasswordAuthenticationMechanism.Default;
        private readonly WindowsRemotingProtocol _protocol = WindowsRemotingProtocol.DCOM;

        private enum WindowsRemotingProtocol
        {
            DCOM,
            WinRM
        }

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
            _customPropagationDelay = parameters.ContainsKey("propagationdelay") ? Convert.ToInt32(parameters["propagationdelay"]) : (int?)null;
            if (credentials.ContainsKey("protocol") && credentials["protocol"].ToLowerInvariant() == "winrm")
            {
                _protocol = WindowsRemotingProtocol.WinRM;
            }

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
                        new ProviderParameter{ Key="protocol", Name="Remote Management Protocol", IsRequired = true, IsCredential = false, IsPassword = false, Description="Must be one of the following: DCOM, WinRM", Value="DCOM", OptionsList="DCOM;WinRM" },
                        new ProviderParameter{ Key="authentication", Name="Authentication", IsRequired = true, IsCredential = false, IsPassword = false, Description="Must be one of the following: Basic, CredSsp, Default, Digest, Kerberos, Negotiate, NtlmDomain", Value="Default", OptionsList="Basic;CredSsp;Default;Digest;Kerberos;Negotiate;NtlmDomain" },
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

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            var session = CreateCimSession();

            var parameters = new CimMethodParametersCollection
            {
                CimMethodParameter.Create("DnsServerName", _server, CimType.String, CimFlags.None),
                CimMethodParameter.Create("ContainerName", SolveContainerName(session, request.RecordName), CimType.String, CimFlags.None),
                CimMethodParameter.Create("OwnerName", request.RecordName, CimType.String, CimFlags.None),
                CimMethodParameter.Create("DescriptiveText", request.RecordValue, CimType.String, CimFlags.None)
            };
            session.InvokeMethod(@"root\MicrosoftDNS", "MicrosoftDNS_TXTType", "CreateInstanceFromPropertyData", parameters);

            return new ActionResult() { IsSuccess = true, Message = "DNS record updated" };
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            var session = CreateCimSession();
            var strQuery = string.Format("SELECT * FROM MicrosoftDNS_TXTType WHERE OwnerName = '{0}' AND DescriptiveText = '{1}'", request.RecordName, request.RecordValue);
            var txtRecords = session.QueryInstances(@"root\MicrosoftDNS", "WQL", strQuery);
            foreach (var txtRecord in txtRecords)
            {
                session.DeleteInstance(txtRecord);
            }

            return new ActionResult { IsSuccess = true, Message = "DNS record removed." };
        }

        public async Task<List<DnsZone>> GetZones()
        {
            var session = CreateCimSession();
            var zones = new List<DnsZone>();
            GetZones(session).ForEach(o => zones.Add(new DnsZone() { Name = o, ZoneId = o }));
            return zones;
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

        private List<string> GetZones(CimSession session)
        {
            var zones = new List<string>();
            var strQuery = "SELECT * FROM MicrosoftDNS_Zone";
            var dnsZones = session.QueryInstances(@"root\MicrosoftDNS", "WQL", strQuery);
            foreach (var dnsZone in dnsZones)
            {
                zones.Add(dnsZone.CimInstanceProperties["ContainerName"].Value as string);
            }

            return zones;
        }

        private CimSession CreateCimSession()
        {
            CimSessionOptions options = null;
            switch (_protocol)
            {
                case WindowsRemotingProtocol.DCOM:
                    options = new DComSessionOptions();
                    if (!string.IsNullOrEmpty(_username))
                    {
                        options.AddDestinationCredentials(new CimCredential(_authMechanism, _domain, _username, _password));
                    }
                    break;
                case WindowsRemotingProtocol.WinRM:
                    var wsmanOptions = new WSManSessionOptions();
                    if (!string.IsNullOrEmpty(_username))
                    {
                        wsmanOptions.AddDestinationCredentials(new CimCredential(_authMechanism, _domain, _username, _password));
                        wsmanOptions.UseSsl = true;
                    }
                    options = wsmanOptions;
                    break;
            }
            return CimSession.Create(_serverConnectionName, options);
        }

        private string SolveContainerName(CimSession session, string recordName)
        {
            var zones = GetZones(session);
            var partCount = recordName.Split('.').Count();
            var partAssembly = 2;
            var containerName = recordName.Split('.')[partCount - 1];
            while (partAssembly <= partCount && !zones.Contains(containerName))
            {
                containerName = recordName.Split('.')[partCount - partAssembly] + "." + containerName;
                partAssembly++;
            }

            if (!zones.Contains(containerName))
            {
                throw new InvalidOperationException("The zone for " + recordName + " does not appear to exist on this DNS server.");
            }

            return containerName;
        }
    }
}
