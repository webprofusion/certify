using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Shared.Core.Management;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        private TypedConfigurationStore<OidcProviderConfig> _oidcStore => new TypedConfigurationStore<OidcProviderConfig>(_configStore);
        public async Task<ICollection<OidcProviderConfig>> GetOidcProviders(bool includeSecret = false)
        {
            var list = await _oidcStore.GetItems();
            var result = list.Select(t => t.GetItem()).ToList();

            if (!includeSecret)
            {
                result.ForEach(r => r.ClientSecret = "");
            }

            return result;
        }
        public async Task<ActionResult> AddOidcProvider(OidcProviderConfig item)
        {
            await _oidcStore.Add(item);
            return new ActionResult("Added", true);
        }
        public async Task<ActionResult> UpdateOidcProvider(OidcProviderConfig item)
        {
            await _oidcStore.Update(item.Id, item);
            return new ActionResult("Updated", true);
        }
        public async Task<ActionResult> RemovOidcProvider(string id)
        {
            var deleted = await _oidcStore.Delete(id);
            return new ActionResult("Deleted", deleted);
        }
    }
}
