using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Hub;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        public async Task<ActionResult> AddHubItemTags(ICollection<ItemTag> tags)
        {
            foreach (var itemTag in tags)
            {
                itemTag.Id = Guid.NewGuid().ToString();
                await _configStore.Add<ItemTag>(nameof(ItemTag), itemTag);
            }

            return new ActionResult("Added", true);
        }

        public async Task<ActionResult> RemoveHubItemTags(ICollection<string> tagsIds)
        {
            foreach (var id in tagsIds)
            {
                await _configStore.Delete<ItemTag>(nameof(ItemTag), id);
            }

            return new ActionResult("Removed", true);
        }

        public async Task<ICollection<ItemTag>> GetAllHubItemTags()
        {
            var list = await _configStore.GetItems<ItemTag>(nameof(ItemTag));
            return list;
        }
        public async Task<ICollection<ItemTag>> GetHubItemTags(string itemId, string itemTypeId)
        {
            var list = await _configStore.GetItems<ItemTag>(nameof(ItemTag));
            return list.Where(i => i.TaggedItemId == itemId).ToList();
        }
    }
}
