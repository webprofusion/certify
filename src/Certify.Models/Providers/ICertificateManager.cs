using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Models.Providers;
using Microsoft.Extensions.Logging;

#nullable disable

namespace Certify.Providers.CertificateManagers
{
    public interface ICertificateManager
    {

        void Init(ILogger logger, CertificateManagerPreference prefs);

        Task<bool> IsPresent();

        /// <summary>
        /// Get the log path this provider will read from, being the configured log path if set, otherwise the
        /// default log location for this tool on this machine if one can be detected. Returns an empty string
        /// if the provider has no local logs or none could be found
        /// </summary>
        Task<string> ResolveLogPath();

        /// <summary>
        /// Get recent log entries relating to the given item, as recorded by the external tool's own logs
        /// </summary>
        /// <param name="item">the externally managed item to fetch log entries for</param>
        /// <param name="limit">maximum number of log lines to return</param>
        Task<LogItem[]> GetItemLog(ManagedCertificate item, int limit);

        Task<ManagedCertificate> GetManagedCertificate(string id);

        Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter filter = null);

        Task<ManagedCertificate> UpdateManagedCertificate(ManagedCertificate site);

        Task DeleteManagedCertificate(string id);

        Task<List<AccountDetails>> GetAccountRegistrations();

        Task<CertificateRequestResult> PerformCertificateRequest(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null, bool resumePaused = false, bool skipRequest = false, bool failOnSkip = false);

        Task<List<CertificateRequestResult>> PerformRenewalAllManagedCertificates(RenewalSettings settings, Dictionary<string, Progress<RequestProgressState>> progressTrackers = null);

        Task PerformCertificateCleanup();

        ProviderDefinition GetProviderDefinition();
    }
}
