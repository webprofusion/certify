using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Core.Management.Challenges.DNS
{
    public class DnsProviderManual : IDnsProvider
    {
        int IDnsProvider.PropagationDelaySeconds => Definition.PropagationDelaySeconds;

        string IDnsProvider.ProviderId => Definition.Id;

        string IDnsProvider.ProviderTitle => Definition.Title;

        string IDnsProvider.ProviderDescription => Definition.Description;

        string IDnsProvider.ProviderHelpUrl => Definition.HelpUrl;

        bool IDnsProvider.IsTestModeSupported => Definition.IsTestModeSupported;

        List<ProviderParameter> IDnsProvider.ProviderParameters => Definition.ProviderParameters;

        private ILog _log;

        public static ChallengeProviderDefinition Definition => new ChallengeProviderDefinition
        {
            Id = "DNS01.Manual",
            Title = "(Update DNS Manually)",
            Description = "When a DSN update is required, wait for manual changes.",
            HelpUrl = "https://docs.certifytheweb.com/docs/dns-validation/",
            PropagationDelaySeconds = -1,
            ProviderParameters = new List<ProviderParameter>() { new ProviderParameter { Description = "Email address to prompt changes", IsRequired = false, Key = "email", Name = "Email to Notify (optional)", IsCredential = false } },
            ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
            Config = "Provider=Certify.Providers.DNS.Manual",
            HandlerType = ChallengeHandlerType.MANUAL,
            IsTestModeSupported = false
        };

        public DnsProviderManual()
        {
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        public async Task<ActionResult> CreateRecord(DnsRecord request) => new ActionResult
        {
            IsSuccess = true,
            Message = $"Please login to your DNS control panel for the domain '{request.TargetDomainName}' and create a new TXT record named: \r\n{request.RecordName} \r\nwith the value:\r\n{request.RecordValue}"
        };

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        public async Task<ActionResult> DeleteRecord(DnsRecord request) => new ActionResult
        {
            IsSuccess = true,
            Message = $"Please login to your DNS control panel for the domain '{request.TargetDomainName}' and delete the TXT record named '{request.RecordName}'."
        };

        Task<List<DnsZone>> IDnsProvider.GetZones() => Task.FromResult(new List<DnsZone>());

        Task<bool> IDnsProvider.InitProvider(Dictionary<string, string> parameters, ILog log)
        {
            _log = log;

            return Task.FromResult(true);
        }

        async Task<ActionResult> IDnsProvider.Test() => await Task.FromResult(new ActionResult { IsSuccess = true, Message = "The user will manually update DNS as required." });
    }
}
