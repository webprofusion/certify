using System.Collections.Concurrent;
using Certify.Management;
using Certify.Models.Hub;
using Microsoft.AspNetCore.Mvc;
using ActionResultConfig = Certify.Models.Config.ActionResult;

namespace Certify.Service.Controllers
{
    [ApiController]
    [Route("api/tags")]
    public class TagController : ControllerBase
    {
        private ICertifyManager _certifyManager;

        // Cache for permission checks: key = "userId:action", value = (result, expiry)
        private static readonly ConcurrentDictionary<string, (bool Result, DateTime Expiry)> _permissionCache = new();
        private static readonly TimeSpan _cacheExpiry = TimeSpan.FromSeconds(30);

        public TagController(ICertifyManager certifyManager)
        {
            _certifyManager = certifyManager;
        }

        /// <summary>
        /// Check if the current user has the specified tag action permission (with short-lived caching)
        /// </summary>
        private async Task<bool> HasTagPermission(string action)
        {
            var userId = GetContextUserId();
            var cacheKey = $"{userId}:{action}";

            // Check cache first
            if (_permissionCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                return cached.Result;
            }

            // Cache miss or expired - check the database
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var check = new AccessCheck(userId, ResourceTypes.Tag, action);
            var result = await accessControl.IsSecurityPrincipalAuthorised(userId, check);

            // Store in cache with expiry
            _permissionCache[cacheKey] = (result, DateTime.UtcNow.Add(_cacheExpiry));

            // Periodically clean up expired entries (simple cleanup on cache miss)
            CleanupExpiredCacheEntries();

            return result;
        }

        /// <summary>
        /// Remove expired entries from the permission cache
        /// </summary>
        private static void CleanupExpiredCacheEntries()
        {
            var now = DateTime.UtcNow;
            var expiredKeys = _permissionCache
                .Where(kvp => kvp.Value.Expiry <= now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _permissionCache.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// Check if the current user has permission to modify the specified item type (with short-lived caching)
        /// </summary>
        private async Task<bool> HasItemUpdatePermission(string itemType)
        {
            var userId = GetContextUserId();
            var cacheKey = $"{userId}:itemupdate:{itemType}";

            // Check cache first
            if (_permissionCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                return cached.Result;
            }

            var accessControl = await _certifyManager.GetCurrentAccessControl();

            // Map item types to their corresponding resource types and update actions
            var (resourceType, action) = itemType switch
            {
                TaggedItemTypes.ManagedCertificate => (ResourceTypes.ManagedItem, StandardResourceActions.ManagedItemUpdate),
                TaggedItemTypes.ManagedInstance => (ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceUpdate),
                TaggedItemTypes.StoredCredential => (ResourceTypes.StoredCredential, StandardResourceActions.StoredCredentialUpdate),
                TaggedItemTypes.ManagedChallenge => (ResourceTypes.ManagedChallenge, StandardResourceActions.ManagedChallengeUpdate),
                TaggedItemTypes.SecurityPrincipal => (ResourceTypes.SecurityPrincipal, StandardResourceActions.SecurityPrincipalUpdate),
                _ => (null, null)
            };

            if (resourceType == null || action == null)
            {
                return false;
            }

            var check = new AccessCheck(null, resourceType, action);
            var result = await accessControl.IsSecurityPrincipalAuthorised(userId, check);

            // Store in cache with expiry
            _permissionCache[cacheKey] = (result, DateTime.UtcNow.Add(_cacheExpiry));

            return result;
        }

        /// <summary>
        /// Check if the current user has tag-scoped access (which restricts tagging) - with short-lived caching
        /// </summary>
        private async Task<bool> HasTagScopedAccess()
        {
            var userId = GetContextUserId();

            if (string.IsNullOrEmpty(userId))
            {
                return false;
            }

            var cacheKey = $"{userId}:tagscoped";

            // Check cache first
            if (_permissionCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                return cached.Result;
            }

            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var assignedRoles = await accessControl.GetAssignedRoles(userId, userId);

            // Check if any assigned role has scoped tags
            var result = assignedRoles?.Any(r => r.ScopedTags != null && r.ScopedTags.Any()) == true;

            // Store in cache with expiry
            _permissionCache[cacheKey] = (result, DateTime.UtcNow.Add(_cacheExpiry));

            return result;
        }

        #region Tag Categories

        [HttpGet("categories")]
        public async Task<ICollection<TagCategory>> GetTagCategories()
        {
            // Tag list is readable by anyone with TagList permission (included in ManagementHubReader)
            if (!await HasTagPermission(StandardResourceActions.TagList))
            {
                return new List<TagCategory>();
            }

            return await _certifyManager.GetTagCategories();
        }

        [HttpGet("categories/{categoryKey}")]
        public async Task<TagCategory?> GetTagCategory(string categoryKey)
        {
            if (!await HasTagPermission(StandardResourceActions.TagList))
            {
                return null;
            }

            return await _certifyManager.GetTagCategory(categoryKey);
        }

        [HttpPost("categories")]
        public async Task<ActionResultConfig> AddOrUpdateTagCategory([FromBody] TagCategory category)
        {
            // Only TagAdmin can create/modify categories
            if (!await HasTagPermission(StandardResourceActions.TagAdd))
            {
                return new ActionResultConfig("Unauthorized: Tag administration permission required", false);
            }

            return await _certifyManager.AddOrUpdateTagCategory(category);
        }

        [HttpDelete("categories/{categoryKey}")]
        public async Task<ActionResultConfig> DeleteTagCategory(string categoryKey)
        {
            // Only TagAdmin can delete categories
            if (!await HasTagPermission(StandardResourceActions.TagDelete))
            {
                return new ActionResultConfig("Unauthorized: Tag administration permission required", false);
            }

            return await _certifyManager.DeleteTagCategory(categoryKey);
        }

        #endregion

        #region Tag Values

        [HttpGet("values")]
        public async Task<ICollection<TagValue>> GetTagValues([FromQuery] string? categoryKey = null)
        {
            if (!await HasTagPermission(StandardResourceActions.TagList))
            {
                return new List<TagValue>();
            }

            return await _certifyManager.GetTagValues(categoryKey);
        }

        [HttpPost("values")]
        public async Task<TagValue?> GetOrCreateTagValue([FromBody] TagValueRequest request)
        {
            // Only TagAdmin can create tag values directly
            if (!await HasTagPermission(StandardResourceActions.TagAdd))
            {
                return null;
            }

            return await _certifyManager.GetOrCreateTagValue(request.CategoryKey, request.Value);
        }

        [HttpPost("values/update")]
        public async Task<ActionResultConfig> UpdateTagValue([FromBody] TagValueUpdateRequest request)
        {
            // Only TagAdmin can update tag values
            if (!await HasTagPermission(StandardResourceActions.TagUpdate))
            {
                return new ActionResultConfig("Unauthorized: Tag administration permission required", false);
            }

            return await _certifyManager.UpdateTagValue(request.ValueId, request.NewValue, request.Description);
        }

        [HttpDelete("values/{valueId}")]
        public async Task<ActionResultConfig> DeleteTagValue(string valueId)
        {
            // Only TagAdmin can delete tag values
            if (!await HasTagPermission(StandardResourceActions.TagDelete))
            {
                return new ActionResultConfig("Unauthorized: Tag administration permission required", false);
            }

            return await _certifyManager.DeleteTagValue(valueId);
        }

        [HttpPost("values/merge")]
        public async Task<ActionResultConfig> MergeTagValues([FromBody] TagValueMergeRequest request)
        {
            // Only TagAdmin can merge tag values
            if (!await HasTagPermission(StandardResourceActions.TagUpdate))
            {
                return new ActionResultConfig("Unauthorized: Tag administration permission required", false);
            }

            return await _certifyManager.MergeTagValues(request.SourceValueIds, request.TargetValueId);
        }

        #endregion

        #region Item Tags

        [HttpPost, Route("add")]
        public async Task<ActionResultConfig> AddTag([FromBody] ItemTag tag)
        {
            // Check authorization for adding tags
            var authResult = await CheckItemTaggingAuthorization(tag.TaggedItemType);
            if (!authResult.IsSuccess)
            {
                return authResult;
            }

            return await _certifyManager.AddHubItemTags([tag]);
        }

        [HttpDelete, Route("delete/{id}")]
        public async Task<ActionResultConfig> DeleteTag(string id)
        {
            // Only TagAdmin can delete tags via this endpoint
            if (!await HasTagPermission(StandardResourceActions.TagDelete))
            {
                return new ActionResultConfig("Unauthorized: Tag administration permission required", false);
            }

            return await _certifyManager.RemoveHubItemTags([id]);
        }

        [HttpGet, Route("list")]
        public async Task<ICollection<ItemTag>> GetTags()
        {
            if (!await HasTagPermission(StandardResourceActions.TagList))
            {
                return new List<ItemTag>();
            }

            return await _certifyManager.GetAllHubItemTags(null, null, null);
        }

        [HttpGet("items/{itemType}/{itemId}")]
        public async Task<ICollection<TagSummary>> GetItemTags(string itemType, string itemId)
        {
            if (!await HasTagPermission(StandardResourceActions.TagList))
            {
                return new List<TagSummary>();
            }

            return await _certifyManager.GetHubItemTags(itemType, itemId);
        }

        [HttpGet("items")]
        public async Task<ICollection<ItemTag>> GetItemTags([FromQuery] string? categoryKey = null, [FromQuery] string? value = null, [FromQuery] string? itemType = null, [FromQuery] string? instanceId = null, [FromQuery] bool requireAll = false)
        {
            if (!await HasTagPermission(StandardResourceActions.TagList))
            {
                return new List<ItemTag>();
            }

            if (categoryKey == null && value == null)
            {
                var allTags = await _certifyManager.GetAllHubItemTags(null, null, itemType, instanceId);
                return allTags;
            }

            var scopes = new List<TagScope>();
            if (categoryKey != null)
            {
                scopes.Add(new TagScope { CategoryKey = categoryKey, Value = value });
            }

            return await _certifyManager.GetItemsByTagScopes(scopes, itemType, requireAll, instanceId);
        }

        [HttpPost("items")]
        public async Task<ActionResultConfig> AddItemTags([FromBody] ICollection<ItemTag> tags)
        {
            if (tags == null || !tags.Any())
            {
                return new ActionResultConfig("No tags provided", false);
            }

            // Check authorization for all item types being tagged
            var itemTypes = tags.Select(t => t.TaggedItemType).Distinct();
            foreach (var itemType in itemTypes)
            {
                var authResult = await CheckItemTaggingAuthorization(itemType);
                if (!authResult.IsSuccess)
                {
                    return authResult;
                }
            }

            return await _certifyManager.AddHubItemTags(tags);
        }

        [HttpDelete("items")]
        public async Task<ActionResultConfig> RemoveItemTags([FromBody] ICollection<string> tagIds)
        {
            // Only TagAdmin can remove tags via this endpoint
            if (!await HasTagPermission(StandardResourceActions.TagDelete))
            {
                return new ActionResultConfig("Unauthorized: Tag administration permission required", false);
            }

            return await _certifyManager.RemoveHubItemTags(tagIds);
        }

        [HttpDelete("items/{itemType}/{itemId}/{categoryKey}/{value}")]
        public async Task<ActionResultConfig> RemoveItemTagByKey(string itemType, string itemId, string categoryKey, string value, [FromQuery] string? instanceId = null)
        {
            // Check authorization - need either TagAdmin or item update permission (and no scoped access)
            var authResult = await CheckItemTaggingAuthorization(itemType);
            if (!authResult.IsSuccess)
            {
                return authResult;
            }

            return await _certifyManager.RemoveHubItemTagByKey(itemId, itemType, categoryKey, value, instanceId);
        }

        [HttpPost("items/bulk")]
        public async Task<ActionResultConfig> BulkTagOperation([FromBody] BulkTagOperationRequest request)
        {
            // Only TagAdmin can perform bulk operations
            if (!await HasTagPermission(StandardResourceActions.TagUpdate))
            {
                return new ActionResultConfig("Unauthorized: Tag administration permission required", false);
            }

            return await _certifyManager.BulkTagOperation(request.ItemIds, request.ItemType, request.InstanceId, request.AddTags, request.RemoveTags);
        }

        /// <summary>
        /// Check if the current user is authorized to tag items of the specified type.
        /// Users with tag-scoped access cannot tag items (to prevent privilege escalation).
        /// </summary>
        private async Task<ActionResultConfig> CheckItemTaggingAuthorization(string itemType)
        {
            // Check if user has TagAdmin permission (full tag access)
            if (await HasTagPermission(StandardResourceActions.TagAdd))
            {
                return new ActionResultConfig("Authorized", true);
            }

            // Check if user has tag-scoped access - if so, they cannot tag items
            // (this prevents users from removing their scope restrictions by re-tagging items)
            if (await HasTagScopedAccess())
            {
                return new ActionResultConfig("Unauthorized: Users with tag-scoped access cannot modify item tags", false);
            }

            // Check if user has permission to update the item type being tagged
            if (await HasItemUpdatePermission(itemType))
            {
                return new ActionResultConfig("Authorized", true);
            }

            return new ActionResultConfig($"Unauthorized: Permission required to tag {itemType} items", false);
        }

        #endregion

        #region Scope Preview

        [HttpPost("scope-preview")]
        public async Task<ScopePreviewResult> PreviewTagScope([FromBody] ScopePreviewRequest request)
        {
            // Only TagAdmin can preview scopes (used for access control configuration)
            if (!await HasTagPermission(StandardResourceActions.TagList))
            {
                return new ScopePreviewResult { TotalMatchingItems = 0 };
            }

            // Basic implementation using tag scopes
            var matching = await _certifyManager.GetItemsByTagScopes(request.TagScopes, itemType: null, requireAll: request.RequireAll, instanceId: null);

            var result = new ScopePreviewResult
            {
                TotalMatchingItems = matching.Select(t => t.TaggedItemId).Distinct().Count(),
                UnmatchedItemsCount = 0, // not calculated here
                ScopeDescription = string.Join(" OR ", request.TagScopes.Select(s => s.Value != null ? $"{s.CategoryKey}:{s.Value}" : $"{s.CategoryKey}:*"))
            };

            var grouped = matching.GroupBy(t => t.TaggedItemType);
            foreach (var group in grouped)
            {
                result.MatchesByResourceType[group.Key] = new ScopePreviewResourceType
                {
                    Count = group.Select(g => g.TaggedItemId).Distinct().Count(),
                    Items = new List<ScopePreviewItem>()
                };
            }

            return result;
        }

        #endregion
    }
}
