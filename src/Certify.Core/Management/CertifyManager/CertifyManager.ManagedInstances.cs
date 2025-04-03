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
            var existing = await _configStore.Get<ManagedInstanceInfo>(nameof(ManagedInstanceInfo), item.Id);

            if (existing != null)
            {
                existing.OS = item.OS;
                existing.OSVersion = item.OSVersion;

                existing.ClientName = item.ClientName;
                existing.ClientVersion = item.ClientVersion;

                existing.Title = item.Title;
                existing.Description = item.Description;

                existing.DateLastReported = item.DateLastReported;

                existing.Tags = item.Tags;

                await _configStore.Update(nameof(ManagedInstanceInfo), existing);

                return new ActionResult("Updated", true);
            }
            else
            {
                return new ActionResult("Item Not found. Cannot update.", false);
            }
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
