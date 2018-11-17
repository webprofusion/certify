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

        List<ProviderParameter> IDnsProvider.ProviderParameters => Definition.ProviderParameters;

        private ILog _log;

        public static ProviderDefinition Definition
        {
            get
            {
                return new ProviderDefinition
                {
                    Id = "DNS01.Manual",
                    Title = "(Update DNS Manually)",
                    Description = "When a DSN update is required, wait for manual changes.",
                    HelpUrl = "http://docs.certifytheweb.com/",
                    PropagationDelaySeconds = -1,
                    ProviderParameters = new List<ProviderParameter>() { new ProviderParameter { Description = "Email address to prompt changes", IsRequired = false, Key = "email", Name = "Email to Notify (optional)", IsCredential = false } },
                    ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.Manual",
                    HandlerType = ChallengeHandlerType.MANUAL
                };
            }
        }

        public DnsProviderManual(Dictionary<string, string> parameters)
        {
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        public async Task<ActionResult> CreateRecord(DnsRecord request)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            return new ActionResult
            {
                IsSuccess = true,
                Message = $"Please login to your DNS control panel for the domain '{request.TargetDomainName}' and create a new TXT record named: \r\n{request.RecordName} \r\nwith the value:\r\n{request.RecordValue}"
            };
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            return new ActionResult
            {
                IsSuccess = true,
                Message = $"Please login to your DNS control panel for the domain '{request.TargetDomainName}' and delete the TXT record named '{request.RecordName}'."
            };
        }

        Task<List<DnsZone>> IDnsProvider.GetZones()
        {
            return Task.FromResult(new List<DnsZone>());
        }

        Task<bool> IDnsProvider.InitProvider(ILog log)
        {
            _log = log;

            return Task.FromResult(true);
        }

        async Task<ActionResult> IDnsProvider.Test()
        {
            return await Task.FromResult(new ActionResult { IsSuccess = true, Message = "The user will manually update DNS as required." });
        }
    }
}
