using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Hub;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        private static readonly Regex CategoryKeyRegex = new Regex(@"^[a-z][a-z0-9\-]*$", RegexOptions.Compiled);

        private readonly SemaphoreSlim _itemTagAddSync = new SemaphoreSlim(1, 1);

        private static bool IsValidTagValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            value = value.Trim();

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];

                if (c is '/' or ':' or '\\')
                {
                    return false;
                }

                if (char.IsSurrogate(c))
                {
                    if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        i++;
                        continue;
                    }

                    return false;
                }

                var category = char.GetUnicodeCategory(c);
                if (category is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                {
                    return false;
                }
            }

            return true;
        }

        #region Tag Ids

        // Tag categories, values and item tags are stored under ids derived from what identifies them, so concurrent creates
        // of the same tag upsert one stored item instead of adding duplicates. Renaming a value changes what identifies it,
        // so a renamed value and its item tags are stored again under their new ids.

        private static string GetTagCategoryId(string categoryKey) => $"tagcategory:{categoryKey}";

        // values match case insensitively
        private static string GetTagValueId(string categoryKey, string value) => $"tagvalue:{HashTagKey(categoryKey, value?.ToUpperInvariant())}";

        private static string GetItemTagId(ItemTag tag) => $"itemtag:{HashTagKey(tag.TaggedItemType, tag.TaggedItemId, tag.CategoryKey, tag.Value)}";

        // hashed to keep ids short and safe to use in a url whatever the value text
        private static string HashTagKey(params string?[] parts) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts))));

        /// <summary>
        /// Store every tag category, value and item tag under its derived id. Items written before ids were derived get their
        /// derived id, and items stored more than once are merged into one. Categories and values are referred to by key and
        /// value text, never by id, so nothing else needs updating.
        /// </summary>
        private async Task MergeDuplicateTags()
        {
            try
            {
                await StoreUnderDerivedIds<TagCategory>(c => GetTagCategoryId(c.CategoryKey));
                var merged = await StoreUnderDerivedIds<TagValue>(v => GetTagValueId(v.CategoryKey, v.Value))
                    + await StoreUnderDerivedIds<ItemTag>(GetItemTagId);

                if (merged > 0)
                {
                    var itemTags = await GetAllHubItemTags();
                    foreach (var tagValue in await _configStore.GetItems<TagValue>(nameof(TagValue)))
                    {
                        var usageCount = itemTags.Count(t => t.CategoryKey == tagValue.CategoryKey && t.Value == tagValue.Value);
                        if (tagValue.UsageCount != usageCount)
                        {
                            tagValue.UsageCount = usageCount;
                            await _configStore.Update<TagValue>(nameof(TagValue), tagValue);
                        }
                    }
                }
            }
            catch (Exception exp)
            {
                _serviceLog?.Error(exp, "Failed to merge duplicate tags. {Message}", exp.Message);
            }
        }

        /// <returns>the number of duplicate items removed</returns>
        private async Task<int> StoreUnderDerivedIds<T>(Func<T, string> getId) where T : ConfigurationStoreItem
        {
            var itemType = typeof(T).Name;
            var stored = await _configStore.GetItems<T>(itemType);
            var removed = 0;

            foreach (var group in stored.GroupBy(getId))
            {
                var storedIds = group.Select(i => i.Id).ToList();
                if (storedIds.Count == 1 && storedIds[0] == group.Key)
                {
                    continue;
                }

                var kept = group
                    .OrderByDescending(i => i.Id == group.Key)
                    .ThenBy(i => i.Id, StringComparer.Ordinal)
                    .First();

                if (kept.Id != group.Key)
                {
                    kept.Id = group.Key;
                    await _configStore.Update<T>(itemType, kept);
                }

                foreach (var id in storedIds.Where(id => id != group.Key))
                {
                    await _configStore.Delete<T>(itemType, id);
                }

                removed += storedIds.Count - 1;
            }

            if (removed > 0)
            {
                _serviceLog?.Information("Merged {Count} duplicate {ItemType} items", removed, itemType);
            }

            return removed;
        }

        #endregion

        #region Tag Categories

        /// <summary>
        /// Get all tag categories
        /// </summary>
        public async Task<ICollection<TagCategory>> GetTagCategories()
        {
            var list = await _configStore.GetItems<TagCategory>(nameof(TagCategory));
            return list.OrderBy(c => c.SortOrder).ThenBy(c => c.DisplayName).ToList();
        }

        /// <summary>
        /// Get a specific tag category by key
        /// </summary>
        public async Task<TagCategory?> GetTagCategory(string categoryKey)
        {
            var list = await _configStore.GetItems<TagCategory>(nameof(TagCategory));
            return list.FirstOrDefault(c => c.CategoryKey == categoryKey);
        }

        /// <summary>
        /// Add or update a tag category
        /// </summary>
        public async Task<ActionResult> AddOrUpdateTagCategory(TagCategory category)
        {
            if (string.IsNullOrWhiteSpace(category.CategoryKey))
            {
                return new ActionResult("Category key is required", false);
            }

            category.CategoryKey = category.CategoryKey.ToLowerInvariant().Trim();

            if (!CategoryKeyRegex.IsMatch(category.CategoryKey))
            {
                return new ActionResult("Category key must start with a letter and contain only lowercase letters, numbers, and hyphens", false);
            }

            if (string.IsNullOrWhiteSpace(category.DisplayName))
            {
                return new ActionResult("Display name is required", false);
            }

            var existing = await GetTagCategory(category.CategoryKey);

            if (existing != null)
            {
                // Update existing
                existing.DisplayName = category.DisplayName;
                existing.Description = category.Description;
                existing.ColorHint = category.ColorHint;
                existing.IsSingleValue = category.IsSingleValue;
                existing.SortOrder = category.SortOrder;
                // Don't allow changing IsSystemCategory after creation

                await _configStore.Update<TagCategory>(nameof(TagCategory), existing);
                return new ActionResult("Category updated", true);
            }
            else
            {
                // Create new
                category.Id = GetTagCategoryId(category.CategoryKey);
                await _configStore.Add<TagCategory>(nameof(TagCategory), category);
                return new ActionResult("Category created", true);
            }
        }

        /// <summary>
        /// Delete a tag category (fails if values exist)
        /// </summary>
        public async Task<ActionResult> DeleteTagCategory(string categoryKey)
        {
            var category = await GetTagCategory(categoryKey);
            if (category == null)
            {
                return new ActionResult("Category not found", false);
            }

            if (category.IsSystemCategory)
            {
                return new ActionResult("Cannot delete system categories", false);
            }

            // Check if any values exist for this category
            var values = await GetTagValues(categoryKey);
            if (values.Any())
            {
                return new ActionResult($"Cannot delete category with existing values. Remove {values.Count} value(s) first.", false);
            }

            await _configStore.Delete<TagCategory>(nameof(TagCategory), category.Id);
            return new ActionResult("Category deleted", true);
        }

        /// <summary>
        /// Merge duplicate tags, then create default system categories if they don't exist
        /// </summary>
        public async Task EnsureDefaultTagCategories()
        {
            await MergeDuplicateTags();

            var existing = await GetTagCategories();

            var defaults = new[]
            {
                new TagCategory { CategoryKey = "environment", DisplayName = "Environment", Description = "Deployment environment (Production, Staging, Development, etc.)", ColorHint = "#4CAF50", IsSingleValue = true, SortOrder = 1, IsSystemCategory = true },
                new TagCategory { CategoryKey = "application", DisplayName = "Application", Description = "Application or service name", ColorHint = "#2196F3", IsSingleValue = false, SortOrder = 2, IsSystemCategory = true },
                new TagCategory { CategoryKey = "department", DisplayName = "Department", Description = "Business unit or team ownership", ColorHint = "#9C27B0", IsSingleValue = false, SortOrder = 3, IsSystemCategory = true },
                new TagCategory { CategoryKey = "criticality", DisplayName = "Criticality", Description = "Business criticality level (e.g. Critical, High, Medium, Low)", ColorHint = "#450017", IsSingleValue = true, SortOrder = 4, IsSystemCategory = true },
                new TagCategory { CategoryKey = "region", DisplayName = "Region", Description = "Geographic location or data center region", ColorHint = "#FF9800", IsSingleValue = false, SortOrder = 5, IsSystemCategory = true },
                new TagCategory { CategoryKey = "organization", DisplayName = "Organization", Description = "Customer or tenant organization (when managing multiple companies)", ColorHint = "#00BCD4", IsSingleValue = true, SortOrder = 6, IsSystemCategory = true }
            };

            foreach (var defaultCat in defaults)
            {
                if (!existing.Any(e => e.CategoryKey == defaultCat.CategoryKey))
                {
                    defaultCat.Id = GetTagCategoryId(defaultCat.CategoryKey);
                    await _configStore.Add<TagCategory>(nameof(TagCategory), defaultCat);
                }
            }

            // Seed common values for certain categories
            await EnsureDefaultTagValues();
        }

        /// <summary>
        /// Create default tag values for system categories
        /// </summary>
        private async Task EnsureDefaultTagValues()
        {
            var defaultValues = new Dictionary<string, string[]>
            {
                ["environment"] = ["Production", "Staging", "Development", "Test", "QA"],
                ["criticality"] = ["Critical", "High", "Medium", "Low"]
            };

            foreach (var kvp in defaultValues)
            {
                foreach (var value in kvp.Value)
                {
                    await GetOrCreateTagValue(kvp.Key, value);
                }
            }
        }

        #endregion

        #region Tag Values

        /// <summary>
        /// Get all tag values, optionally filtered by category
        /// </summary>
        public async Task<ICollection<TagValue>> GetTagValues(string? categoryKey = null)
        {
            var list = await _configStore.GetItems<TagValue>(nameof(TagValue));

            if (!string.IsNullOrEmpty(categoryKey))
            {
                list = list.Where(v => v.CategoryKey == categoryKey).ToList();
            }

            return list.OrderBy(v => v.CategoryKey).ThenBy(v => v.Value).ToList();
        }

        /// <summary>
        /// Get or create a tag value (used when tagging items)
        /// </summary>
        public async Task<TagValue?> GetOrCreateTagValue(string categoryKey, string value)
        {
            if (string.IsNullOrWhiteSpace(categoryKey) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            categoryKey = categoryKey.ToLowerInvariant().Trim();
            value = value.Trim();

            // Validate category exists
            var category = await GetTagCategory(categoryKey);
            if (category == null)
            {
                return null;
            }

            // Validate value format
            if (!IsValidTagValue(value))
            {
                return null;
            }

            // Check if value already exists
            var existingValues = await GetTagValues(categoryKey);
            var existing = existingValues.FirstOrDefault(v => v.Value.Equals(value, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                return existing;
            }

            // Create new value
            var tagValue = new TagValue
            {
                Id = GetTagValueId(categoryKey, value),
                CategoryKey = categoryKey,
                Value = value,
                DateCreated = DateTimeOffset.UtcNow,
                UsageCount = 0
            };

            await _configStore.Add<TagValue>(nameof(TagValue), tagValue);
            return tagValue;
        }

        /// <summary>
        /// Update a tag value (for renaming)
        /// </summary>
        public async Task<ActionResult> UpdateTagValue(string valueId, string newValue, string? description = null)
        {
            var allValues = await _configStore.GetItems<TagValue>(nameof(TagValue));
            var tagValue = allValues.FirstOrDefault(v => v.Id == valueId);

            if (tagValue == null)
            {
                return new ActionResult("Value not found", false);
            }

            if (!string.IsNullOrWhiteSpace(newValue))
            {
                newValue = newValue.Trim();
                if (!IsValidTagValue(newValue))
                {
                    return new ActionResult("Invalid value format. Use printable text, including letters from any language and emojis. Avoid /, :, or \\.", false);
                }

                // Check for duplicates
                if (allValues.Any(v => v.Id != valueId && v.CategoryKey == tagValue.CategoryKey && v.Value.Equals(newValue, StringComparison.OrdinalIgnoreCase)))
                {
                    return new ActionResult("A value with this name already exists in this category", false);
                }

                // Update all item tags that use this value
                var itemTags = await GetAllHubItemTags(null, null, null);
                foreach (var itemTag in itemTags.Where(t => t.CategoryKey == tagValue.CategoryKey && t.Value == tagValue.Value))
                {
                    await UpdateItemTagValue(itemTag, newValue);
                }

                tagValue.Value = newValue;
            }

            if (description != null)
            {
                tagValue.Description = description;
            }

            var previousId = tagValue.Id;
            tagValue.Id = GetTagValueId(tagValue.CategoryKey, tagValue.Value);
            await _configStore.Update<TagValue>(nameof(TagValue), tagValue);

            if (tagValue.Id != previousId)
            {
                await _configStore.Delete<TagValue>(nameof(TagValue), previousId);
            }

            return new ActionResult("Value updated", true);
        }

        /// <summary>
        /// Change an item tag's value, storing it under the id for its new value. If the item already has the new value the two merge.
        /// </summary>
        private async Task UpdateItemTagValue(ItemTag itemTag, string newValue)
        {
            var previousId = itemTag.Id;
            itemTag.Value = newValue;
            itemTag.Id = GetItemTagId(itemTag);
            await _configStore.Update<ItemTag>(nameof(ItemTag), itemTag);

            if (itemTag.Id != previousId)
            {
                await _configStore.Delete<ItemTag>(nameof(ItemTag), previousId);
            }
        }

        /// <summary>
        /// Delete a tag value (updates usage counts, removes from items)
        /// </summary>
        public async Task<ActionResult> DeleteTagValue(string valueId)
        {
            var allValues = await _configStore.GetItems<TagValue>(nameof(TagValue));
            var tagValue = allValues.FirstOrDefault(v => v.Id == valueId);

            if (tagValue == null)
            {
                return new ActionResult("Value not found", false);
            }

            // Remove all item tags that use this value
            var itemTags = await GetAllHubItemTags(null, null, null);
            foreach (var itemTag in itemTags.Where(t => t.CategoryKey == tagValue.CategoryKey && t.Value == tagValue.Value))
            {
                await _configStore.Delete<ItemTag>(nameof(ItemTag), itemTag.Id);
            }

            await _configStore.Delete<TagValue>(nameof(TagValue), valueId);
            return new ActionResult("Value deleted", true);
        }

        /// <summary>
        /// Merge multiple values into a target value
        /// </summary>
        public async Task<ActionResult> MergeTagValues(ICollection<string> sourceValueIds, string targetValueId)
        {
            var allValues = await _configStore.GetItems<TagValue>(nameof(TagValue));
            var targetValue = allValues.FirstOrDefault(v => v.Id == targetValueId);

            if (targetValue == null)
            {
                return new ActionResult("Target value not found", false);
            }

            var sourceValues = allValues.Where(v => sourceValueIds.Contains(v.Id) && v.Id != targetValueId).ToList();

            if (!sourceValues.Any())
            {
                return new ActionResult("No source values found to merge", false);
            }

            // Verify all sources are from the same category
            if (sourceValues.Any(s => s.CategoryKey != targetValue.CategoryKey))
            {
                return new ActionResult("All values must be from the same category", false);
            }

            // Update all item tags from source values to target value
            var itemTags = await GetAllHubItemTags(null, null, null);
            foreach (var sourceValue in sourceValues)
            {
                foreach (var itemTag in itemTags.Where(t => t.CategoryKey == sourceValue.CategoryKey && t.Value == sourceValue.Value))
                {
                    await UpdateItemTagValue(itemTag, targetValue.Value);
                }

                // Delete the source value
                await _configStore.Delete<TagValue>(nameof(TagValue), sourceValue.Id);
            }

            // Update usage count on target
            await RecalculateTagValueUsageCount(targetValue.CategoryKey, targetValue.Value);

            return new ActionResult($"Merged {sourceValues.Count} value(s) into {targetValue.Value}", true);
        }

        /// <summary>
        /// Recalculate usage count for a tag value
        /// </summary>
        private async Task RecalculateTagValueUsageCount(string categoryKey, string value)
        {
            var allValues = await _configStore.GetItems<TagValue>(nameof(TagValue));
            var tagValue = allValues.FirstOrDefault(v => v.CategoryKey == categoryKey && v.Value == value);

            if (tagValue != null)
            {
                var itemTags = await GetAllHubItemTags(null, null, null);
                tagValue.UsageCount = itemTags.Count(t => t.CategoryKey == categoryKey && t.Value == value);
                await _configStore.Update<TagValue>(nameof(TagValue), tagValue);
            }
        }

        #endregion

        #region Item Tags

        /// <summary>
        /// Add tags to items
        /// </summary>
        public async Task<ActionResult> AddHubItemTags(ICollection<ItemTag> tags)
        {
            // one add at a time, otherwise overlapping adds of different values to a single-value category each clear the
            // item's existing value and then add their own, leaving the item with several
            await _itemTagAddSync.WaitAsync();

            try
            {
                foreach (var itemTag in tags)
                {
                    // Validate category exists
                    var category = await GetTagCategory(itemTag.CategoryKey);
                    if (category == null)
                    {
                        continue; // Skip invalid categories
                    }

                    // Ensure the value exists
                    var tagValue = await GetOrCreateTagValue(itemTag.CategoryKey, itemTag.Value);
                    if (tagValue == null)
                    {
                        continue; // Skip invalid values
                    }

                    // For single-value categories, remove existing tags in same category
                    if (category.IsSingleValue)
                    {
                        var existingTags = await GetItemTagsInternal(itemTag.TaggedItemId, itemTag.TaggedItemType);
                        foreach (var existingInCategory in existingTags.Where(t => t.CategoryKey == itemTag.CategoryKey))
                        {
                            await _configStore.Delete<ItemTag>(nameof(ItemTag), existingInCategory.Id);
                            await RecalculateTagValueUsageCount(existingInCategory.CategoryKey, existingInCategory.Value);
                        }
                    }

                    // Check if this exact tag already exists
                    var allTags = await GetAllHubItemTags(null, null, null);
                    var duplicate = allTags.FirstOrDefault(t =>
                        t.TaggedItemId == itemTag.TaggedItemId &&
                        t.TaggedItemType == itemTag.TaggedItemType &&
                        t.CategoryKey == itemTag.CategoryKey &&
                        t.Value == itemTag.Value);

                    if (duplicate == null)
                    {
                        itemTag.Id = GetItemTagId(itemTag);
                        await _configStore.Add<ItemTag>(nameof(ItemTag), itemTag);

                        // Update usage count
                        tagValue.UsageCount++;
                        await _configStore.Update<TagValue>(nameof(TagValue), tagValue);
                    }
                }

                return new ActionResult("Tags added", true);
            }
            finally
            {
                _itemTagAddSync.Release();
            }
        }

        /// <summary>
        /// Remove tags by their IDs
        /// </summary>
        public async Task<ActionResult> RemoveHubItemTags(ICollection<string> tagIds)
        {
            var allTags = await GetAllHubItemTags(null, null, null);

            foreach (var id in tagIds)
            {
                var tag = allTags.FirstOrDefault(t => t.Id == id);
                if (tag != null)
                {
                    await _configStore.Delete<ItemTag>(nameof(ItemTag), id);
                    await RecalculateTagValueUsageCount(tag.CategoryKey, tag.Value);
                }
            }

            return new ActionResult("Tags removed", true);
        }

        /// <summary>
        /// Remove all tags for a tagged item.
        /// </summary>
        public async Task RemoveHubItemTagsForItem(string itemTypeId, string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemTypeId) || string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            var allTags = await GetAllHubItemTags(null, null, itemTypeId);
            var tagIds = allTags
                .Where(t => string.Equals(t.TaggedItemId, itemId, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Id)
                .ToList();

            if (!tagIds.Any())
            {
                return;
            }

            await RemoveHubItemTags(tagIds);
        }

        /// <summary>
        /// Remove a tag by its composite key (more efficient than fetching all tags first)
        /// </summary>
        public async Task<ActionResult> RemoveHubItemTagByKey(string itemId, string itemType, string categoryKey, string value, string? instanceId = null)
        {
            var itemTags = await GetItemTagsInternal(itemId, itemType);

            // Filter by instanceId if specified
            if (!string.IsNullOrEmpty(instanceId))
            {
                itemTags = itemTags.Where(t => t.InstanceId == instanceId).ToList();
            }

            var tagToRemove = itemTags.FirstOrDefault(t => t.CategoryKey == categoryKey && t.Value == value);

            if (tagToRemove == null)
            {
                return new ActionResult("Tag not found", false);
            }

            await _configStore.Delete<ItemTag>(nameof(ItemTag), tagToRemove.Id);
            await RecalculateTagValueUsageCount(categoryKey, value);

            return new ActionResult("Tag removed", true);
        }

        /// <summary>
        /// Get all item tags, optionally filtered by category, value, item type, or instance
        /// </summary>
        public async Task<ICollection<ItemTag>> GetAllHubItemTags(string? categoryKey = null, string? value = null, string? itemTypeId = null, string? instanceId = null)
        {
            var list = await _configStore.GetItems<ItemTag>(nameof(ItemTag));

            if (!string.IsNullOrEmpty(categoryKey))
            {
                list = list.Where(t => t.CategoryKey == categoryKey).ToList();
            }

            if (!string.IsNullOrEmpty(value))
            {
                list = list.Where(t => t.Value == value).ToList();
            }

            if (!string.IsNullOrEmpty(itemTypeId))
            {
                list = list.Where(t => t.TaggedItemType == itemTypeId).ToList();
            }

            if (!string.IsNullOrEmpty(instanceId))
            {
                list = list.Where(t => t.InstanceId == instanceId).ToList();
            }

            return list;
        }

        /// <summary>
        /// Internal helper to get raw item tags for a specific item (used by AddHubItemTags)
        /// </summary>
        private async Task<ICollection<ItemTag>> GetItemTagsInternal(string itemId, string itemTypeId)
        {
            var list = await _configStore.GetItems<ItemTag>(nameof(ItemTag));
            return list.Where(i => i.TaggedItemId == itemId && i.TaggedItemType == itemTypeId).ToList();
        }

        /// <summary>
        /// Get tag summaries for a specific item (includes display info)
        /// </summary>
        public async Task<ICollection<TagSummary>> GetHubItemTags(string itemTypeId, string itemId)
        {
            var list = await _configStore.GetItems<ItemTag>(nameof(ItemTag));
            var itemTags = list.Where(i => i.TaggedItemId == itemId && i.TaggedItemType == itemTypeId).ToList();
            var categories = await GetTagCategories();

            return itemTags.Select(t =>
            {
                var category = categories.FirstOrDefault(c => c.CategoryKey == t.CategoryKey);
                return new TagSummary
                {
                    CategoryKey = t.CategoryKey,
                    CategoryDisplayName = category?.DisplayName ?? t.CategoryKey,
                    Value = t.Value,
                    ColorHint = category?.ColorHint,
                    InstanceId = t.InstanceId
                };
            }).ToList();
        }

        /// <summary>
        /// Get items matching tag scopes
        /// </summary>
        public async Task<ICollection<ItemTag>> GetItemsByTagScopes(ICollection<TagScope> scopes, string? itemType = null, bool requireAll = false, string? instanceId = null)
        {
            var allTags = await GetAllHubItemTags(null, null, null, instanceId);

            if (itemType != null)
            {
                allTags = allTags.Where(t => t.TaggedItemType == itemType).ToList();
            }

            if (!scopes.Any())
            {
                return allTags;
            }

            // Group tags by item
            var tagsByItem = allTags.GroupBy(t => new { t.TaggedItemId, t.TaggedItemType });

            var matchingItemIds = new HashSet<string>();

            foreach (var itemGroup in tagsByItem)
            {
                var itemTags = itemGroup.ToList();

                // tag scope matching is shared, see ResourceAccess.IsResourceTagScopeMatch
                if (ResourceAccess.IsResourceTagScopeMatch(ResourceAccess.ToTagSummaries(itemTags), scopes.ToList(), requireAll))
                {
                    matchingItemIds.Add(itemGroup.Key.TaggedItemId);
                }
            }

            return allTags.Where(t => matchingItemIds.Contains(t.TaggedItemId)).ToList();
        }

        /// <summary>
        /// Bulk tag operation - add and/or remove tags from multiple items
        /// </summary>
        public async Task<ActionResult> BulkTagOperation(
            ICollection<string> itemIds,
            string itemType,
            string? instanceId,
            ICollection<TagScope>? addTags,
            ICollection<TagScope>? removeTags)
        {
            var addedCount = 0;
            var removedCount = 0;

            // Add tags
            if (addTags != null && addTags.Any())
            {
                var tagsToAdd = new List<ItemTag>();
                foreach (var itemId in itemIds)
                {
                    foreach (var scope in addTags.Where(s => s.Value != null))
                    {
                        tagsToAdd.Add(new ItemTag(itemId, itemType, scope.CategoryKey, scope.Value!, instanceId));
                    }
                }

                if (tagsToAdd.Any())
                {
                    await AddHubItemTags(tagsToAdd);
                    addedCount = tagsToAdd.Count;
                }
            }

            // Remove tags
            if (removeTags != null && removeTags.Any())
            {
                var allTags = await GetAllHubItemTags(null, null, null, instanceId);
                var idsToRemove = new List<string>();

                foreach (var itemId in itemIds)
                {
                    foreach (var scope in removeTags)
                    {
                        var matching = allTags.Where(t =>
                            t.TaggedItemId == itemId &&
                            t.TaggedItemType == itemType &&
                            string.Equals(t.CategoryKey, scope.CategoryKey, StringComparison.OrdinalIgnoreCase) &&
                            (scope.Value == null || string.Equals(t.Value, scope.Value, StringComparison.OrdinalIgnoreCase)));

                        idsToRemove.AddRange(matching.Select(t => t.Id));
                    }
                }

                if (idsToRemove.Any())
                {
                    await RemoveHubItemTags(idsToRemove);
                    removedCount = idsToRemove.Count;
                }
            }

            return new ActionResult($"Added {addedCount} tag(s), removed {removedCount} tag(s)", true);
        }

        /// <summary>
        /// Preview resources matching tag scopes
        /// </summary>
        public async Task<ScopePreviewResult> PreviewTagScope(ICollection<TagScope> scopes, ICollection<string>? resourceTypes = null, bool requireAll = false, string? instanceId = null)
        {
            var matching = await GetItemsByTagScopes(scopes, itemType: null, requireAll: requireAll, instanceId: instanceId);

            if (resourceTypes != null && resourceTypes.Any())
            {
                matching = matching.Where(t => resourceTypes.Contains(t.TaggedItemType)).ToList();
            }

            var result = new ScopePreviewResult
            {
                TotalMatchingItems = matching.Select(t => t.TaggedItemId).Distinct().Count(),
                UnmatchedItemsCount = 0,
                ScopeDescription = string.Join(" OR ", scopes.Select(s => s.Value != null ? $"{s.CategoryKey}:{s.Value}" : $"{s.CategoryKey}:*"))
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
