using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.Providers
{
    public interface IManagedItemStore
    {
        Task DeleteAll();
        Task StoreAll(IEnumerable<ManagedCertificate> list);
        Task Delete(ManagedCertificate site);
        Task DeleteByName(string nameStartsWith);
        Task<ManagedCertificate> GetById(string siteId);
        Task<List<ManagedCertificate>> GetAll(ManagedCertificateFilter filter = null);
        Task<ManagedCertificate> Update(ManagedCertificate managedCertificate);

        Task PerformMaintenance();

        Task<bool> IsInitialised();
    }
}
