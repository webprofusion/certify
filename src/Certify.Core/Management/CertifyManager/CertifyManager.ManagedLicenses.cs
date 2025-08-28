using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Hub;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        public async Task<ICollection<ManagedLicense>> GetManagedLicenses()
        {
            var list = await _configStore.GetItems<ManagedLicense>(nameof(ManagedLicense));
            return list;
        }

        public async Task<ActionResult> AddManagedLicense(ManagedLicense item)
        {
            await _configStore.Add<ManagedLicense>(nameof(ManagedLicense), item);
            return new ActionResult("Added", true);
        }

        public async Task<ActionResult> UpdateManagedLicense(ManagedLicense item)
        {
            await _configStore.Update<ManagedLicense>(nameof(ManagedLicense), item);
            return new ActionResult("Updated", true);
        }

        public async Task<ActionResult> RemoveManagedLicenses(string id)
        {
            await _configStore.Delete<ManagedLicense>(nameof(ManagedLicense), id);
            return new ActionResult("Removed", true);
        }

        public async Task<ManagedLicense> GetManagedLicense(string id)
        {
            return await _configStore.Get<ManagedLicense>(nameof(ManagedLicense), id);
        }
    }
}
