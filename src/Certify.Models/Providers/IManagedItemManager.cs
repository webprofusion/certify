using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Providers;

namespace Certify.Providers
{
    public interface IManagedItemStore
    {
        bool Init(string connectionString, ILog log);
        Task DeleteAll();
        Task StoreAll(IEnumerable<ManagedCertificate> list);
        Task Delete(ManagedCertificate site);
        Task DeleteByName(string nameStartsWith);
        Task<ManagedCertificate> GetById(string siteId);
        Task<List<ManagedCertificate>> Find(ManagedCertificateFilter filter);
        Task<ManagedCertificate> Update(ManagedCertificate managedCertificate);

        Task PerformMaintenance();

        Task<bool> IsInitialised();
    }
}
