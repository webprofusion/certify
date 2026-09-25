using Certify.Client;
using Certify.Models.Hub;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Hub item tags as they apply to an item on one managed instance.
    ///
    /// An item id is only known to be unique within its instance, so a tag recorded against an instance applies to the
    /// item on that instance alone. A tag recorded without an instance applies wherever the id occurs, as tags were
    /// written that way before instances were recorded on them. Scope decisions read tags through here so that an item
    /// created elsewhere under a reused id does not pick up another item's tags. Items held by the hub itself, such as
    /// managed challenges, are looked up with no instance and every tag on them applies.
    /// </summary>
    public static class HubItemTags
    {
        public static bool AppliesToInstance(string? tagInstanceId, string? instanceId)
            => string.IsNullOrWhiteSpace(instanceId)
                || string.IsNullOrWhiteSpace(tagInstanceId)
                || string.Equals(tagInstanceId, instanceId, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The tags applying to one item on one instance
        /// </summary>
        public static async Task<List<TagSummary>> GetItemTags(ICertifyInternalApiClient client, string itemType, string? instanceId, string? itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return [];
            }

            var tags = await client.GetHubItemTags(itemType, itemId, PrincipalAccess.SystemAuthContext) ?? [];

            return tags.Where(t => AppliesToInstance(t.InstanceId, instanceId)).ToList();
        }

        /// <summary>
        /// Every tag for items of a type, by item id, for evaluating many items at once
        /// </summary>
        public static async Task<ILookup<string, ItemTag>> GetAllItemTags(ICertifyInternalApiClient client, string itemType)
        {
            var tags = await client.GetAllHubItemTags(null, null, itemType, null, PrincipalAccess.SystemAuthContext) ?? [];

            return tags
                .Where(t => !string.IsNullOrWhiteSpace(t.TaggedItemId))
                .ToLookup(t => t.TaggedItemId, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The tags from <see cref="GetAllItemTags"/> applying to one item on one instance
        /// </summary>
        public static List<ItemTag> ForItem(ILookup<string, ItemTag> tagsByItemId, string? instanceId, string? itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return [];
            }

            return tagsByItemId[itemId].Where(t => AppliesToInstance(t.InstanceId, instanceId)).ToList();
        }
    }
}
