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

        public TagController(ICertifyManager certifyManager)
        {
            _certifyManager = certifyManager;
        }

        #region Tag Categories

        [HttpGet("categories")]
        public async Task<ICollection<TagCategory>> GetTagCategories()
        {
            return await _certifyManager.GetTagCategories();
        }

        [HttpGet("categories/{categoryKey}")]
        public async Task<TagCategory?> GetTagCategory(string categoryKey)
        {
            return await _certifyManager.GetTagCategory(categoryKey);
        }

        [HttpPost("categories")]
        public async Task<ActionResultConfig> AddOrUpdateTagCategory([FromBody] TagCategory category)
        {
            return await _certifyManager.AddOrUpdateTagCategory(category);
        }

        [HttpDelete("categories/{categoryKey}")]
        public async Task<ActionResultConfig> DeleteTagCategory(string categoryKey)
        {
            return await _certifyManager.DeleteTagCategory(categoryKey);
        }

        #endregion

        #region Tag Values

        [HttpGet("values")]
        public async Task<ICollection<TagValue>> GetTagValues([FromQuery] string? categoryKey = null)
        {
            return await _certifyManager.GetTagValues(categoryKey);
        }

        [HttpPost("values")]
        public async Task<TagValue?> GetOrCreateTagValue([FromBody] TagValueRequest request)
        {
            return await _certifyManager.GetOrCreateTagValue(request.CategoryKey, request.Value);
        }

        [HttpPost("values/update")]
        public async Task<ActionResultConfig> UpdateTagValue([FromBody] TagValueUpdateRequest request)
        {
            return await _certifyManager.UpdateTagValue(request.ValueId, request.NewValue, request.Description);
        }

        [HttpDelete("values/{valueId}")]
        public async Task<ActionResultConfig> DeleteTagValue(string valueId)
        {
            return await _certifyManager.DeleteTagValue(valueId);
        }

        [HttpPost("values/merge")]
        public async Task<ActionResultConfig> MergeTagValues([FromBody] TagValueMergeRequest request)
        {
            return await _certifyManager.MergeTagValues(request.SourceValueIds, request.TargetValueId);
        }

        #endregion

        #region Item Tags

        [HttpPost, Route("add")]
        public async Task<ActionResultConfig> AddTag([FromBody] ItemTag tag)
        {
            return await _certifyManager.AddHubItemTags([tag]);
        }

        [HttpDelete, Route("delete/{id}")]
        public async Task<ActionResultConfig> DeleteTag(string id)
        {
            return await _certifyManager.RemoveHubItemTags([id]);
        }

        [HttpGet, Route("list")]
        public async Task<ICollection<ItemTag>> GetTags()
        {
            return await _certifyManager.GetAllHubItemTags(null, null, null);
        }

            [HttpGet("items/{itemType}/{itemId}")]
            public async Task<ICollection<TagSummary>> GetItemTags(string itemType, string itemId)
            {
                return await _certifyManager.GetHubItemTags(itemType, itemId);
            }

            [HttpGet("items")]
            public async Task<ICollection<ItemTag>> GetItemTags([FromQuery] string? categoryKey = null, [FromQuery] string? value = null, [FromQuery] string? itemType = null, [FromQuery] string? instanceId = null, [FromQuery] bool requireAll = false)
            {
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
                return await _certifyManager.AddHubItemTags(tags);
            }

            [HttpDelete("items")]
            public async Task<ActionResultConfig> RemoveItemTags([FromBody] ICollection<string> tagIds)
            {
                return await _certifyManager.RemoveHubItemTags(tagIds);
            }

            [HttpDelete("items/{itemType}/{itemId}/{categoryKey}/{value}")]
            public async Task<ActionResultConfig> RemoveItemTagByKey(string itemType, string itemId, string categoryKey, string value, [FromQuery] string? instanceId = null)
            {
                return await _certifyManager.RemoveHubItemTagByKey(itemId, itemType, categoryKey, value, instanceId);
            }

            [HttpPost("items/bulk")]
            public async Task<ActionResultConfig> BulkTagOperation([FromBody] BulkTagOperationRequest request)
            {
                return await _certifyManager.BulkTagOperation(request.ItemIds, request.ItemType, request.InstanceId, request.AddTags, request.RemoveTags);
            }

            #endregion

            #region Scope Preview

            [HttpPost("scope-preview")]
            public async Task<ScopePreviewResult> PreviewTagScope([FromBody] ScopePreviewRequest request)
            {
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
