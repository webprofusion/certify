using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Hub;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        public async Task<ActionResult<ManagedInstanceInfo>> AddHubManagedInstance(ManagedInstanceInfo item)
        {
            item.Id = Guid.NewGuid().ToString();
            item.InstanceId = item.Id;
            await _configStore.Add(nameof(ManagedInstanceInfo), item);
            return new ActionResult<ManagedInstanceInfo>("Added", true, item);
        }

        public async Task<ActionResult> UpdateHubManagedInstance(ManagedInstanceInfo item)
        {
            await _configStore.Update(nameof(ManagedInstanceInfo), item);
            return new ActionResult("Updated", true);
        }

        public async Task<ManagedInstanceInfo> GetHubManagedInstance(string id)
        {
            var item = await _configStore.Get<ManagedInstanceInfo>(nameof(ManagedInstanceInfo), id);
            return item;
        }

        public async Task<ICollection<ManagedInstanceInfo>> GetHubManagedInstances()
        {
            return await _configStore.GetItems<ManagedInstanceInfo>(nameof(ManagedInstanceInfo));
        }

        public async Task<ActionResult> RemoveHubManagedInstance(string id)
        {
            var deleted = await _configStore.Delete<ManagedInstanceInfo>(nameof(ManagedInstanceInfo), id);
            if (deleted)
            {
                return new ActionResult("Deleted", true);
            }
            else
            {
                return new ActionResult("Not found", false);
            }
        }
    }
}
