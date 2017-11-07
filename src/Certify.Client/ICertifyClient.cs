using Certify.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Client
{
    /// <summary>
    /// Client to talk to the core Certify Service 
    /// </summary>
    public interface ICertifyClient
    {
        Task<string> GetAppVersion();

        Task<UpdateCheck> CheckForUpdates();

        Task<CertificateRequestResult> PerformCertificateRequest(ManagedSite site);

        Task<object> GetRequestsInProgress(); // Could be a signalr service?

        Task<List<CertificateRequestResult>> PerformRenewalAllManagedSites();

        Task<List<ManagedSite>> GetManagedSites(string filter, int maxresults);

        Task<ManagedSite> AddOrUpdateManagedSite(ManagedSite site);

        Task<bool> DeleteManagedSite(ManagedSite site);
    }
}