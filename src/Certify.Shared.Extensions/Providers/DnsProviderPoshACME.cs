using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;

namespace Certify.Core.Management.Challenges.DNS
{
    /// <summary>
    /// DNS Provider bridge to Posh-ACME DNS Scripts
    /// </summary>
    public class DnsProviderPoshACME : IDnsProvider
    {

        /*
            Implemented providers (Posh-ACME: https://github.com/rmbolger/Posh-ACME)
            [Akamai](https://poshac.me/docs/v4/Plugins/Akamai),
            [AutoDNS](https://poshac.me/docs/v4/Plugins/AutoDNS),
            [All-Inkl](https://poshac.me/docs/v4/Plugins/All-Inkl),
            [Bunny](https://poshac.me/docs/v4/Plugins/Bunny),
            [ClouDNS](https://poshac.me/docs/v4/Plugins/ClouDNS),
            [Combell](https://poshac.me/docs/v4/Plugins/Combell),
            [Constellix](https://poshac.me/docs/v4/Plugins/Constellix),
            [DMEasy](https://poshac.me/docs/v4/Plugins/DMEasy),
            [DNSPod](https://poshac.me/docs/v4/Plugins/DNSPod),
            [DNSimple](https://poshac.me/docs/v4/Plugins/DNSimple),
            [DomainOffensive](https://poshac.me/docs/v4/Plugins/DomainOffensive),
            [Domeneshop](https://poshac.me/docs/v4/Plugins/Domeneshop)
            [deSEC](https://poshac.me/docs/v4/Plugins/DeSEC),
            [DigitalOcean](https://poshac.me/docs/v4/Plugins/DOcean),
            [Dreamhost](https://poshac.me/docs/v4/Plugins/Dreamhost),
            [Dynu](https://poshac.me/docs/v4/Plugins/Dynu),
            [EasyDNS](https://poshac.me/docs/v4/Plugins/EasyDNS),
            [Gandi](https://poshac.me/docs/v4/Plugins/Gandi),
            [Google Cloud](https://poshac.me/docs/v4/Plugins/GCloud),
            [Google Domains](https://poshac.me/docs/v4/Plugins/GoogleDomains),
            [Hetzner](https://poshac.me/docs/v4/Plugins/Hetzner),
            [HostingDe](https://poshac.me/docs/v4/Plugins/HostingDe),
            [Hurricane Electric](https://poshac.me/docs/v4/Plugins/HurricaneElectric),
            [Infoblox](https://poshac.me/docs/v4/Plugins/Infoblox),
            [Infomaniak](https://poshac.me/docs/v4/Plugins/Infomaniak)
            [IONOS](https://poshac.me/docs/v4/Plugins/IONOS)
            [IBM Cloud/SoftLayer](https://poshac.me/docs/v4/Plugins/IBMSoftLayer),
            [ISPConfig](https://poshac.me/docs/v4/Plugins/ISPConfig),
            [Leaseweb](https://poshac.me/docs/v4/Plugins/LeaseWeb/),
            [Linode](https://poshac.me/docs/v4/Plugins/Linode),
            [Loopia](https://poshac.me/docs/v4/Plugins/Loopia),
            [LuaDns](https://poshac.me/docs/v4/Plugins/LuaDns),
            [name.com](https://poshac.me/docs/v4/Plugins/NameCom),
            [Namecheap](https://poshac.me/docs/v4/Plugins/Namecheap)
            [NS1](https://poshac.me/docs/v4/Plugins/NS1),
            [PointDNS](https://poshac.me/docs/v4/Plugins/PointDNS),
            [PowerDNS](https://poshac.me/docs/v4/Plugins/PowerDNS),
            [Rackspace](https://poshac.me/docs/v4/Plugins/Rackspace),
            [RFC2136](https://poshac.me/docs/v4/Plugins/RFC2136),
            [Selectel](https://poshac.me/docs/v4/Plugins/Selectel),
            [Simply](https://poshac.me/docs/v4/Plugins/Simply),
            [TotalUptime](https://poshac.me/docs/v4/Plugins/TotalUptime),
            [UKFast](https://poshac.me/docs/v4/Plugins/UKFast),
            [Yandex](https://poshac.me/docs/v4/Plugins/Yandex),
            [Zilore](https://poshac.me/docs/v4/Plugins/Zilore)
            [Zonomi](https://poshac.me/docs/v4/Plugins/Zonomi)

            Adding a new provider:
            - update the list above
            - add a new ChallengeProviderDefinition to the ExtendedProviders list
            - check that the expected parameters are defined in the provider definition 
            - check that the _paramIsSecureStringConfig config is included for secure string params (legacy entries might use alt config)
        */

        public class PoshACMEDnsProviderProvider : IDnsProviderProviderPlugin
        {

            public IDnsProvider GetProvider(Type pluginType, string id)
            {
                foreach (var provider in ExtendedProviders)
                {
                    if (provider.Id == id)
                    {
                        var appBasePath = AppContext.BaseDirectory;

                        var scriptPath = Path.Combine(new string[] { appBasePath, "Scripts", "DNS", "PoshACME" });

                        // TODO : move this out, shared config should be injected
                        var config = SharedUtils.ServiceConfigManager.GetAppServiceConfig();
                        return new DnsProviderPoshACME(scriptPath, config.PowershellExecutionPolicy) { DelegateProviderDefinition = provider };
                    }
                }

                return null;
            }

            public List<ChallengeProviderDefinition> GetProviders(Type pluginType)
            {
                return ExtendedProviders.ToList();
            }
        }

        private const int DefaultPropagationDelay = 90;

        private ILog _log;

        int IDnsProvider.PropagationDelaySeconds => (_customPropagationDelay != null ? (int)_customPropagationDelay : Definition.PropagationDelaySeconds);

        string IDnsProvider.ProviderId => Definition.Id;

        string IDnsProvider.ProviderTitle => DelegateProviderDefinition?.Title ?? Definition.Title;

        string IDnsProvider.ProviderDescription => DelegateProviderDefinition?.Description ?? Definition.Description;

        string IDnsProvider.ProviderHelpUrl => DelegateProviderDefinition?.HelpUrl ?? Definition.HelpUrl;

        public bool IsTestModeSupported => Definition.IsTestModeSupported;

        List<ProviderParameter> IDnsProvider.ProviderParameters => Definition.ProviderParameters;

        private int? _customPropagationDelay = null;

        private Dictionary<string, string> _parameters;
        private Dictionary<string, string> _credentials;

        private string _poshAcmeScriptPath = @"Scripts\DNS\PoshACME";
        private string _scriptExecutionPolicy = "Unrestricted";

        private string[] ignoredCommandExceptions = { "Get-PAAccount", "Join-Path", "Test-Path" };

        private const string _paramAltKeyConfig = "{\"IsSecureString\": false, \"AltParamKey\":\"PARAMKEY\"}";
        private const string _paramIsSecureStringConfig = "{\"IsSecureString\": true}";
        private const string _paramIsSecureStringAltKeyConfig = "{\"IsSecureString\": true, \"AltParamKey\":\"PARAMKEY\"}";

        private static ProviderParameter _defaultPropagationDelayParam = new ProviderParameter
        {
            Key = "propagationdelay",
            Name = "Propagation Delay Seconds",
            IsRequired = false,
            IsPassword = false,
            Value = DefaultPropagationDelay.ToString(),
            IsCredential = false
        };

        /// <summary>
        /// if another provider uses this one as a base, consumer must set the delegate to override settings
        /// </summary>
        public ChallengeProviderDefinition DelegateProviderDefinition { get; set; }
        public static ChallengeProviderDefinition Definition => new ChallengeProviderDefinition
        {
            Id = "DNS01.Powershell",
            Title = "Powershell/PoshACME DNS",
            Description = "Validates DNS challenges via a user provided custom powershell script",
            HelpUrl = "https://docs.certifytheweb.com/docs/dns/validation",
            PropagationDelaySeconds = DefaultPropagationDelay,
            ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{ Key="args",Name="Script arguments", IsRequired=false, IsPassword=false, Value=DefaultPropagationDelay.ToString(), IsCredential=false },
                       _defaultPropagationDelayParam
                    },
            ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
            Config = "Provider=Certify.Providers.DNS.Powershell",
            HandlerType = ChallengeHandlerType.POWERSHELL
        };

        /// <summary>
        /// List of definitions that use this provider as a base. Each defines the info, parameters, credentials and script to be run.
        /// </summary>

        public static List<ChallengeProviderDefinition> ExtendedProviders = new List<ChallengeProviderDefinition>
        {
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Akamai",
                Title = "Akamai DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Akamai/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "AKHost", Name = "Host", IsRequired = true, Description = "e.g. myhost.akamaiapis.net", IsCredential = false },
                    new ProviderParameter { Key = "AKClientToken", Name = "Client Token", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "AKClientSecretInsecure", Name = "Client Secret", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","AKClientSecret") },
                    new ProviderParameter { Key = "AKAccessToken", Name = "Access Token", IsRequired = true, IsCredential = true },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Akamai",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.AkamaiEdgeRC",
                Title = "Akamai DNS API with .edgerc file (using Posh-ACME)",
                Description = "Validates via DNS API using .edgerc file",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Akamai/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "AKUseEdgeRC", Name = "Use EdgeRC file", IsRequired = true, Description = "Set to true", Value="true", IsHidden=true, Type= OptionType.Boolean, IsCredential = false },
                    new ProviderParameter { Key = "AKEdgeRCFile", Name = "EdgeRC File Path", IsRequired = true, Description = "Full path to .edgerc", IsCredential = false },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Akamai",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.All-Inkl",
                Title = "All-Inkl API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/All-Inkl/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "KasUsername", Name = "Username", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "KasPwd", Name = "Password", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringConfig },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=All-Inkl",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Aliyun",
                Title = "Aliyun (Alibaba Cloud) DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Aliyun/",
                PropagationDelaySeconds = 120,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "AliKeyId", Name = "Access Key ID", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "AliSecret", Name = "Access Key Secret", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringConfig },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Aliyun",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.AutoDNS",
                Title = "AutoDNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/AutoDNS/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "AutoDNSUser", Name = "Username", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "AutoDNSPasswordInsecure", Name = "Password", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","AutoDNSPassword") },
                    new ProviderParameter { Key = "AutoDNSContext", Name = "Context", IsRequired = true, IsCredential = false, Value="4" },
                    new ProviderParameter { Key = "AutoDNSGateway", Name = "Gateway Host", IsRequired = true, IsCredential = true, Value="gateway.autodns.com" },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=AutoDNS",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
                 new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Bunny",
                Title = "Bunny.net DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Bunny/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "BunnyAccessKey", Name = "API Key", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringConfig },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Bunny",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.ClouDNS",
                Title = "ClouDNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/ClouDNS/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "CDUserType", Name = "User Type", IsRequired = true, IsCredential = false, Value="auth-id",  OptionsList="auth-id;sub-auth-id;sub-auth-user;" , Type= OptionType.Select },
                    new ProviderParameter { Key = "CDUsername", Name = "Username", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "CDPasswordInsecure", Name = "Password", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","CDPassword") },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=ClouDNS",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Combell",
                Title = "Combell API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Combell/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "CombellApiKey", Name = "API Key", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringConfig },
                    new ProviderParameter { Key = "CombellApiSecret", Name = "API Secret", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringConfig },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Combell",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Constellix",
                Title = "Constellix API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Constellix/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "ConstellixKey", Name = "API Key", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "ConstellixSecret", Name = "Password", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringConfig },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Constellix",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.DMEasy",
                Title = "DNS Made Easy DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/DMEasy/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "DMEKey", Name = "API Key", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "DMESecret", Name = "API Secret", IsRequired = true, IsCredential = true, ExtendedConfig=_paramIsSecureStringConfig },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=DMEasy",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.DNSPod",
                Title = "DNSPod DNS API (Deprecated - Use v2 instead)",
                Description = "Validates via DNS API using credentials. This provider is deprecated and you should switch to the V2 version.",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/DNSPod/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "DNSPodUsername", Name = "Username", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "DNSPodPwdInsecure", Name = "Password", IsRequired = true, IsCredential = true },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=DNSPod",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.DNSPod.v2",
                Title = "DNSPod (v2) DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/DNSPod/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "DNSPodKeyID", Name = "Key ID", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "DNSPodKeyTokenInsecure", Name = "Key Token", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","DNSPodToken")},
                    new ProviderParameter { Key = "DNSPodApiRoot", Name = "API Root", IsRequired = true, IsCredential = false, Value="https://api.dnspod.com" },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=DNSPod",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.DNSimple",
                Title = "DNSimple DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/DNSimple/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "DSTokenInsecure", Name = "Token", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","DSToken")},
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=DNSimple",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.DigitalOcean",
                Title = "DigitalOcean DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials (Personal Access Token)",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/DOcean/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "DOToken", Name = "Access Token", IsRequired = true, Description = "Personal Access Token", IsCredential = true, ExtendedConfig= _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","DOTokenSecure") },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=DOcean",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.DeSEC",
                Title = "deSEC DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/DeSEC/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "DSTokenInsecure", Name = "Token", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","DSCToken") },
                    new ProviderParameter { Key = "DSTTL", Name = "TTL", IsRequired = true, IsCredential = false, Type = OptionType.Integer, Value = "3600", ExtendedConfig= _paramAltKeyConfig.Replace("PARAMKEY","DSCTTL") },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=DeSEC",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.DomainOffensive",
                Title = "DomainOffensive DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/DomainOffensive/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "DomOffTokenInsecure", Name = "Token", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","DomOffToken") },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=DomainOffensive",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Domeneshop",
                Title = "Domeneshop DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Domeneshop",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "DomeneshopToken", Name = "Token", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "DomeneshopSecret", Name = "Secret", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringConfig },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Domeneshop",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Dreamhost",
                Title = "Dreamhost DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Dreamhost/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "DreamhostApiKey", Name = "Token", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","DreamhostApiKeySecure") },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Dreamhost",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Dynu",
                Title = "Dynu DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Dynu/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "DynuClientID", Name = "Client ID", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "DynuSecret", Name = "Secret", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","DynuSecretSecure") },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Dynu",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.EasyDNS",
                Title = "EasyDNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/EasyDNS/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "EDToken", Name = "Token", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "EDKey", Name = "Key", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","EDKeySecure") },
                    new ProviderParameter { Key = "EDUseSandbox", Name = "Use EasyDNS Sandbox API", Type= OptionType.Boolean,  Value="false", IsCredential=false, IsHidden=false },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=EasyDNS",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.GCloud",
                Title = "Google Cloud DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/GCloud/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "GCKeyFile", Name = "Key File Path", IsRequired = true, Description = "Full path to JSON account file", IsCredential = false },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=GCloud",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
               new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.GoogleDomains",
                Title = "Google Domains API (using Posh-ACME)",
                Description = "Validates via Google Domains DNS ACME Challenge API",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/GoogleDomains/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "RootDomain", Name = "Root Domain", IsRequired = true, IsCredential = false },
                    new ProviderParameter { Key = "AccessToken", Name = "Access Token", IsRequired = true, IsCredential = true },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=GoogleDomains;Credential=GDomCredential,RootDomain,AccessToken;",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Gandi",
                Title = "Gandi DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Gandi/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "GandiTokenInsecure", Name = "Token", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","GandiToken") },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Gandi",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Hetzner",
                Title = "Hetzner DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Hetzner/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "HetznerTokenInsecure", Name = "API Token", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","HetznerToken")},
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Hetzner",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.HostingDE",
                Title = "Hosting.de DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/HostingDE/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "HDEToken", Name = "API Token", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringConfig },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=HostingDe",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.HurricaneElectric",
                Title = "Hurricane Electric DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/HurricaneElectric/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    // HECredential is a PS Credential constructed from HEUsername and HEPassword
                    new ProviderParameter { Key = "HEUsername", Name = "Username", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "HEPassword", Name = "Password", IsRequired = true, IsCredential = true },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=HurricaneElectric;Credential=HECredential,HEUsername,HEPassword;",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.IBMSoftLayer",
                Title = "IBM Cloud/SoftLayer DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/IBMSoftLayer/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                      // IBMCredential is a PS Credential constructed from IBMUser and IBMKey
                    new ProviderParameter { Key = "IBMUser", Name = "Username", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "IBMKey", Name = "Key", IsRequired = true, IsCredential = true },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=IBMSoftLayer;Credential=IBMCredential,IBMUser,IBMKey;",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.ISPConfig",
                Title = "ISPConfig DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/ISPConfig/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    // IBCred is a PS Credential constructed from IBUsername and IBPassword
                    
                    new ProviderParameter { Key = "ISPConfigUsername", Name = "Username", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "ISPConfigPassword", Name = "Password", IsRequired = true, IsCredential = true, IsPassword=true },
                    new ProviderParameter { Key = "ISPConfigEndpoint", Name = "Server", IsRequired = true, IsCredential = false, Description="e.g. https://ispc.example.com:8080/remote/json.php"  },
                    new ProviderParameter { Key = "ISPConfigIgnoreCert", Name = "Skip Cert Validation", Type= OptionType.Boolean,  Value="true", IsCredential=false, IsHidden=false },

                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=ISPConfig;Credential=ISPConfigCredential,ISPConfigUsername,ISPConfigPassword;",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Infoblox",
                Title = "Infoblox DDI DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Infoblox/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    // IBCred is a PS Credential constructed from IBUsername and IBPassword
                    new ProviderParameter { Key = "IBServer", Name = "Server", IsRequired = true, IsCredential = false, Description="e.g. gridmaster.example.com"  },
                    new ProviderParameter { Key = "IBUsername", Name = "Username", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "IBPassword", Name = "Password", IsRequired = true, IsCredential = true, IsPassword=true },
                    new ProviderParameter { Key = "IBView", Name = "DNS View", IsRequired = true, IsCredential = false, Description="e.g. default", Value="default"},
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Infoblox;Credential=IBCred,IBUsername,IBPassword;",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
              new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Infomaniak",
                Title = "Infomaniak DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Infomaniak",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "InfomaniakToken", Name = "API Token", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringConfig },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Infomaniak;",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
              new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.IONOS",
                Title = "IONOS DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/IONOS",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "IONOSKeyPrefix", Name = "API Public Prefix", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "IONOSKeySecret", Name = "API Secret", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringConfig },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=IONOS;",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
             new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.LeaseWeb",
                Title = "Leaseweb DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/LeaseWeb/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "LSWApiKey", Name = "API Key", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringConfig },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=LeaseWeb",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Linode",
                Title = "Linode DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Linode/",
                PropagationDelaySeconds = 1020,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "LITokenInsecure", Name = "Token", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","LIToken") },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Linode",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Loopia",
                Title = "Loopia DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Loopia/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "LoopiaUser", Name = "API Username", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "LoopiaPassInsecure", Name = "API User Password", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","LoopiaPass") },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Loopia",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.LuaDns",
                Title = "LuaDns API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/LuaDns/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                     // LuaCredential is a PS Credential constructed from LuaUsername and LuaPassword
                    new ProviderParameter { Key = "LuaUsername", Name = "Username", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "LuaPassword", Name = "API Token", IsRequired = true, IsCredential = true },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=LuaDns;Credential=LuaCredential,LuaUsername,LuaPassword;",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.NameCom",
                Title = "name.com DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/NameCom/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "NameComUserName", Name = "API Username", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "NameComToken", Name = "API Token", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","NameComTokenSecure") },
                    new ProviderParameter { Key = "NameComUseTestEnv", Name = "Use Test Environment", IsRequired = true, Value="false", Type= OptionType.Boolean, IsHidden=true, IsCredential=false },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=NameCom",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.NS1",
                Title = "NS1 DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/NS1/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "NS1KeyInsecure", Name = "Key", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","NS1Key") },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=NS1",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.NameCheap",
                Title = "Namecheap DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Namecheap/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "NCUsername", Name = "Username", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "NCApiKey", Name = "API Key", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringConfig },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Namecheap",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.PointDNS",
                Title = "PointDNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/PointDNS/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "PDUser", Name = "Username", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "PDKeyInsecure", Name = "API Key", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","PDKey") },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=PointDNS",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.PowerDNS",
                Title = "PowerDNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/PowerDNS/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "PowerDNSApiHost", Name = "API Host", IsRequired = true, IsCredential = false, Description="e.g. pdns.example.com" },
                    new ProviderParameter { Key = "PowerDNSApiKey", Name = "API Key", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringConfig },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=PowerDNS",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Rackspace",
                Title = "Rackspace Cloud DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Rackspace/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "RSUsername", Name = "Username", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "RSApiKeyInsecure", Name = "API Key", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","RSApiKey") },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Rackspace",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.RFC2136",
                Title = "RFC2136 with nsupdate as DNS API (using Posh-ACME)",
                Description = "Validates via nsupdate, using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/RFC2136/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "DDNSNameserver", Name = "Nameserver", IsRequired = true, IsCredential = false, Description="e.g. ns.example.com" },
                    new ProviderParameter { Key = "DDNSExePath", Name = "Path to nsupdate exe", IsRequired = true, IsCredential = false, Description="e.g. C:\\BIND\\nsupdate.exe" },
                    new ProviderParameter { Key = "DDNSPort", Name = "DDNS Port", IsRequired = false, IsCredential = false, Description="e.g. 53 (optional)" },
                    new ProviderParameter { Key = "DDNSKeyType", Name = "Key Type", IsRequired = true, IsCredential = false, Value="hmac-sha256", OptionsList="hmac-md5;hmac-sha1;hmac-sha224;hmac-sha256;hmac-sha384;hmac-sha512"},
                    new ProviderParameter { Key = "DDNSKeyName", Name = "Key Name", IsRequired = true, IsCredential = true, Description="e.g. mykey" },
                    new ProviderParameter { Key = "DDNSKeyValueInsecure", Name = "DDNS Key", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","DDNSKeyValue") },
                    new ProviderParameter { Key = "DDNSZone", Name = "DDNS Zone", IsRequired = false, IsCredential = false, Description="e.g. myzone.domain.net (optional)" },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=RFC2136",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Selectel",
                Title = "Selectel DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Selectel/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "SelectelAdminTokenInsecure", Name = "API Key", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","SelectelAdminToken") },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Selectel",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Simply",
                Title = "Simply.com DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/SimplyCom/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "SimplyAccount", Name = "Account", IsRequired = true, IsCredential = true, Description="e.g. S123456"},
                    new ProviderParameter { Key = "SimplyAPIKeyInsecure", Name = "API Key", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","SimplyAPIKey")},
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=SimplyCom",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.TotalUptime",
                Title = "TotalUptime Cloud DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/TotalUptime/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    // PS Credential constructed from Username and Password
                    new ProviderParameter { Key = "TotalUptimeUsername", Name = "API Username", IsRequired = true, IsCredential = true },
                    new ProviderParameter { Key = "TotalUptimePassword", Name = "API Password", IsRequired = true, IsCredential = true, IsPassword=true },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=TotalUptime;Credential=TotalUptimeCredential,TotalUptimeUsername,TotalUptimePassword;",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.UKFast",
                Title = "UKFast DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/UKFast/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "UKFastApiKey", Name = "API Key", IsRequired = true, IsCredential = true, ExtendedConfig= _paramIsSecureStringConfig},
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=UKFast",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Yandex",
                Title = "Yandex DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Yandex/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "YDAdminTokenInsecure", Name = "Token", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","YDAdminToken")},
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Yandex",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Zonomi",
                Title = "Zonomi DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Zonomi/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "ZonomiApiKey", Name = "API Key", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","ZonomiKey")},
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Zonomi",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.RimuHosting",
                Title = "Rimu Hosting DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Zonomi/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "ZonomiApiKey", Name = "API Key", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringAltKeyConfig.Replace("PARAMKEY","ZonomiKey") },
                    new ProviderParameter { Key = "ZonomiApiUrl", Name = "API Url", IsRequired = true, IsCredential = false, Value="https://rimuhosting.com/dns/dyndns.jsp" },
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Zonomi", // this uses the same plugin as Zonomi because they share the same API
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
            new ChallengeProviderDefinition
            {
                Id = "DNS01.API.PoshACME.Zilore",
                Title = "Zilore DNS API (using Posh-ACME)",
                Description = "Validates via DNS API using credentials",
                HelpUrl = "https://poshac.me/docs/v4/Plugins/Zilore/",
                PropagationDelaySeconds = DefaultPropagationDelay,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = "ZiloreKey", Name = "API Key", IsRequired = true, IsCredential = true, ExtendedConfig = _paramIsSecureStringConfig},
                    _defaultPropagationDelayParam
                },
                ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.PoshACME;Script=Zilore",
                HandlerType = ChallengeHandlerType.POWERSHELL,
                IsTestModeSupported = true,
                IsExperimental = true
            },
        };

        public DnsProviderPoshACME(string scriptPath, string scriptExecutionPolicy)
        {
            _scriptExecutionPolicy = scriptExecutionPolicy;

            if (scriptPath != null)
            {
                _poshAcmeScriptPath = scriptPath;
            }
        }

        private string FormatParamKeyValue(ProviderParameter parameterDefinition, KeyValuePair<string, string> paramKeyValue)
        {
            if (parameterDefinition == null)
            {
                return "";
            }

            var val = paramKeyValue.Value;
            var key = paramKeyValue.Key?.Trim() ?? parameterDefinition.Key?.Trim();

            if (paramKeyValue.Value == null)
            {
                // use default 
                val = parameterDefinition.Value;
            }

            if (val == null)
            {
                return null;
            }

            if (parameterDefinition.Type == OptionType.Boolean)
            {
                // boolean
                return key + "=" + (bool.Parse(val) == true ? "$true" : "$false");
            }
            else if (parameterDefinition.Type == OptionType.Integer)
            {
                // integer
                return key + "=" + val + "";
            }
            else
            {
                if (val.Contains("ConvertTo") || val.Contains("New-Object"))
                {
                    // raw converted object e.g. param=(ConvertTo-Something)
                    return key + "=" + val;
                }
                else
                {
                    // string
                    return key + "='" + val.Replace("'", "''") + "'";
                }
            }
        }

        public class ExtendedParamConfig
        {
            /// <summary>
            /// if true, param represents a secure string in powershell so should be converted
            /// </summary>
            public bool IsSecureString { get; set; }

            /// <summary>
            /// when parameter has migrated to a different name or a secure string with a different parameter name
            /// </summary>
            public string AltParamKey { get; set; }
        }

        private string PrepareScript(string action, string recordName, string recordValue)
        {
            var config = DelegateProviderDefinition.Config.Split(';');

            var script = config.FirstOrDefault(c => c.StartsWith("Script="))?.Split('=')[1];

            // get powershell credential fields spec if used, e.g MyCredentials=MyUsername,MyPassword - only used for some providers
            var psCredentialSpec = config.FirstOrDefault(c => c.StartsWith("Credential="))?.Split('=')[1].Split(',');

            var scriptFile = Path.Combine(_poshAcmeScriptPath, "Plugins", script);

            var wrapper = $" $PoshACMERoot = \"{_poshAcmeScriptPath}\" \r\n";
            wrapper += File.ReadAllText(Path.Combine(_poshAcmeScriptPath, "Posh-ACME-Wrapper.ps1"));

            var scriptContent = wrapper + "\r\n. \"" + scriptFile + ".ps1\" \r\n";

            KeyValuePair<string, string> GetMostSpecificParameterValue(ProviderParameter s)
            {
                if (s == null)
                {
                    return default;
                }

                ExtendedParamConfig cfg = null;
                if (s.ExtendedConfig != null)
                {
                    // if we have extended cofig we will need to check if parameter key has been renamed or become a secure string etc
                    cfg = Newtonsoft.Json.JsonConvert.DeserializeObject<ExtendedParamConfig>(s.ExtendedConfig);
                }

                // check if we have a parameter value in our configuration for this key, we also need to allow for old stored keys incorrectly having whitespace
                if (_parameters?.Keys.Any(k => k.Trim() == s.Key.Trim()) == true)
                {
                    var pa = _parameters.FirstOrDefault(p => p.Key.Trim() == s.Key.Trim());

                    // return parameter value key-pair, using alt key name if present
                    return new KeyValuePair<string, string>(cfg?.AltParamKey.Trim() ?? s.Key.Trim(), pa.Value);
                }
                else if (_credentials?.Keys.Any(k => k.Trim() == s.Key.Trim()) == true)
                {

                    if (cfg != null)
                    {
                        // convert credentials to secure string if required
                        if (cfg.IsSecureString == true)
                        {
                            // check if we have a credential value in our configuration for this key
                            var originalCredential = _credentials.FirstOrDefault(c => c.Key.Trim() == s.Key.Trim());
                            if (originalCredential.Value != null)
                            {
                                var escapedCred = originalCredential.Value?.Replace("'", "''");
                                var kv = new KeyValuePair<string, string>(cfg.AltParamKey ?? s.Key.Trim(), $"(ConvertTo-SecureString '{escapedCred}' -asPlainText -force)");
                                return kv;
                            }
                        }
                        else if (cfg.AltParamKey != null)
                        {
                            //migrate raw credential to new parameter name
                            var cred = _credentials.FirstOrDefault(c => c.Key.Trim() == s.Key.Trim());
                            return new KeyValuePair<string, string>(cfg.AltParamKey, cred.Value);
                        }
                    }

                    // by default return credential value in our configuration for this key
                    return _credentials.FirstOrDefault(c => c.Key.Trim() == s.Key.Trim());
                }
                else
                {
                    // use the default value for this parameter if we have no other value in our config
                    return new KeyValuePair<string, string>(s.Key.Trim(), s.Value);
                }
            }

            // arrange params and credentials into one ordered set of arguments
            var allArgumentKV = DelegateProviderDefinition
                      .ProviderParameters
                      .Where(p => p.Key != "propagationdelay")
                      .Select(p => new { Param = p, KV = GetMostSpecificParameterValue(p) })
                      .ToList();

            // if using PSCredential, convert legacy plaintext individual credentials to primary credential argument
            if (psCredentialSpec?.Length >= 3)
            {
                var credKey = psCredentialSpec[0];
                var credUser = allArgumentKV.FirstOrDefault(a => a.KV.Key == psCredentialSpec[1]).KV.Value;
                var credPwd = allArgumentKV.FirstOrDefault(a => a.KV.Key == psCredentialSpec[2]).KV.Value;

                var credPSCredentials = $"(New-Object System.Management.Automation.PSCredential ('{credUser?.Replace("'", "''")}', (ConvertTo-SecureString '{credPwd?.Replace("'", "''")}' -asPlainText -force)))";

                // remove username/pwd from our list of used arguments as it will now be provided as a combined credential instead
                allArgumentKV.RemoveAll(a => a.KV.Key == psCredentialSpec[1]);
                allArgumentKV.RemoveAll(a => a.KV.Key == psCredentialSpec[2]);

                allArgumentKV.Add(new { Param = new ProviderParameter { Key = credKey }, KV = new KeyValuePair<string, string>(credKey, credPSCredentials) });
            }

            var formattedArgumentValues = allArgumentKV
                .Select(p => FormatParamKeyValue(p.Param, p.KV))
                .Where(i => i != null);

            var args = string.Join("; ", formattedArgumentValues.ToArray());

            scriptContent += " $PluginArgs= @{" + args + "} \r\n";
            scriptContent += $"{action} -RecordName '{recordName}' -TxtValue '{recordValue}' @PluginArgs \r\n";

            return scriptContent;
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            var scriptContent = PrepareScript("Add-DnsTxt", request.RecordName, request.RecordValue);

            var objParams = _parameters.ToDictionary(p => p.Key, p => p.Value as object);

            return await PowerShellManager.RunScript(_scriptExecutionPolicy, parameters: objParams, scriptContent: scriptContent, ignoredCommandExceptions: ignoredCommandExceptions);
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            var scriptContent = PrepareScript("Remove-DnsTxt", request.RecordName, request.RecordValue);

            var objParams = _parameters.ToDictionary(p => p.Key, p => p.Value as object);

            return await PowerShellManager.RunScript(_scriptExecutionPolicy, parameters: objParams, scriptContent: scriptContent, ignoredCommandExceptions: ignoredCommandExceptions);
        }

        Task<List<DnsZone>> IDnsProvider.GetZones() => Task.FromResult(new List<DnsZone>());

        Task<bool> IDnsProvider.InitProvider(Dictionary<string, string> credentials, Dictionary<string, string> parameters, ILog log)
        {
            _log = log;

            _credentials = credentials;
            _parameters = parameters;

            if (parameters?.ContainsKey("propagationdelay") == true)
            {
                if (int.TryParse(parameters["propagationdelay"], out var customPropDelay))
                {
                    _customPropagationDelay = customPropDelay;
                }
            }

            return Task.FromResult(true);
        }

        Task<ActionResult> IDnsProvider.Test() => Task.FromResult(new ActionResult
        {
            IsSuccess = true,
            Message = "Test skipped for DNS updates via Posh-ACME. No test available."
        });
    }
}
