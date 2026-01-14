using System;
using System.Collections.Generic;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Constants for tagged item types
    /// </summary>
    public static class TaggedItemTypes
    {
        public const string ManagedCertificate = "ManagedCertificate";
        public const string ManagedInstance = "ManagedInstance";
        public const string StoredCredential = "StoredCredential";
        public const string DeploymentTask = "DeploymentTask";
    }

    /// <summary>
    /// Links a configuration item to a category:value tag
    /// </summary>
    public class ItemTag : ConfigurationStoreItem
    {
        public ItemTag() { }

        public ItemTag(string taggedItemId, string taggedItemType, string categoryKey, string value)
        {
            TaggedItemId = taggedItemId;
            TaggedItemType = taggedItemType;
            CategoryKey = categoryKey;
            Value = value;
        }

        /// <summary>
        /// ID of the tagged item (e.g., ManagedCertificate.Id)
        /// </summary>
        public string TaggedItemId { get; set; } = default!;

        /// <summary>
        /// Type of the tagged item (use TaggedItemTypes constants)
        /// </summary>
        public string TaggedItemType { get; set; } = default!;

        /// <summary>
        /// Category key (e.g., "department", "environment")
        /// </summary>
        public string CategoryKey { get; set; } = default!;

        /// <summary>
        /// Value within the category (e.g., "Finance", "Production")
        /// </summary>
        public string Value { get; set; } = default!;
    }

    /// <summary>
    /// Admin-controlled tag category definition
    /// </summary>
    public class TagCategory : ConfigurationStoreItem
    {
        /// <summary>
        /// Unique identifier for the category (lowercase, e.g., "department", "environment")
        /// </summary>
        public string CategoryKey { get; set; } = default!;

        /// <summary>
        /// Display name for the category (e.g., "Department", "Environment")
        /// </summary>
        public string DisplayName { get; set; } = default!;

        /// <summary>
        /// Optional description of what this category represents
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Color hint for UI display (hex color e.g., "#4CAF50" or named color)
        /// </summary>
        public string? ColorHint { get; set; }

        /// <summary>
        /// If true, items should have at most one value from this category
        /// </summary>
        public bool IsSingleValue { get; set; } = false;

        /// <summary>
        /// Sort order for display in UI (lower numbers first)
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// If true, this category is a system default and cannot be deleted
        /// </summary>
        public bool IsSystemCategory { get; set; } = false;
    }

    /// <summary>
    /// Dynamic tag value within a category, created when users tag items
    /// </summary>
    public class TagValue : ConfigurationStoreItem
    {
        /// <summary>
        /// Reference to the parent category key
        /// </summary>
        public string CategoryKey { get; set; } = default!;

        /// <summary>
        /// The value string (e.g., "Finance", "Production")
        /// </summary>
        public string Value { get; set; } = default!;

        /// <summary>
        /// Optional description for this value
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Number of items using this tag value (denormalized for performance)
        /// </summary>
        public int UsageCount { get; set; } = 0;

        /// <summary>
        /// Date this value was first created
        /// </summary>
        public DateTimeOffset DateCreated { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Defines a tag scope for filtering or access control
    /// </summary>
    public class TagScope
    {
        /// <summary>
        /// Category to scope by (required)
        /// </summary>
        public string CategoryKey { get; set; } = default!;

        /// <summary>
        /// Specific value within category (optional - null means any value in category)
        /// </summary>
        public string? Value { get; set; }

        /// <summary>
        /// Human-readable description of this scope
        /// </summary>
        public string Description => Value != null
            ? $"{CategoryKey}:{Value}"
            : $"{CategoryKey}:*";
    }

    /// <summary>
    /// Lightweight tag representation for API responses and UI display
    /// </summary>
    public class TagSummary
    {
        public string CategoryKey { get; set; } = default!;
        public string CategoryDisplayName { get; set; } = default!;
        public string Value { get; set; } = default!;
        public string? ColorHint { get; set; }

        /// <summary>
        /// Formatted display string (e.g., "Department: Finance")
        /// </summary>
        public string DisplayText => $"{CategoryDisplayName}: {Value}";
    }

    /// <summary>
    /// Result of a scope preview operation
    /// </summary>
    public class ScopePreviewResult
    {
        public int TotalMatchingItems { get; set; }
        public int UnmatchedItemsCount { get; set; }
        public string ScopeDescription { get; set; } = string.Empty;
        public Dictionary<string, ScopePreviewResourceType> MatchesByResourceType { get; set; } = new();
    }

    /// <summary>
    /// Scope preview results for a specific resource type
    /// </summary>
    public class ScopePreviewResourceType
    {
        public int Count { get; set; }
        public List<ScopePreviewItem> Items { get; set; } = new();
    }

    /// <summary>
    /// Individual item in scope preview results
    /// </summary>
    public class ScopePreviewItem
    {
        public string Id { get; set; } = default!;
        public string Name { get; set; } = default!;
        public List<TagSummary> Tags { get; set; } = new();
    }

    /// <summary>
    /// Request to get or create a tag value
    /// </summary>
    public class TagValueRequest
    {
        public string CategoryKey { get; set; } = default!;
        public string Value { get; set; } = default!;
    }

    /// <summary>
    /// Request to update a tag value
    /// </summary>
    public class TagValueUpdateRequest
    {
        public string ValueId { get; set; } = default!;
        public string? NewValue { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>
    /// Request to merge tag values
    /// </summary>
    public class TagValueMergeRequest
    {
        public ICollection<string> SourceValueIds { get; set; } = new List<string>();
        public string TargetValueId { get; set; } = default!;
    }

    /// <summary>
    /// Request for bulk tag operations
    /// </summary>
    public class BulkTagOperationRequest
    {
        public ICollection<string> ItemIds { get; set; } = new List<string>();
        public string ItemType { get; set; } = default!;
        public ICollection<TagScope> AddTags { get; set; } = new List<TagScope>();
        public ICollection<TagScope> RemoveTags { get; set; } = new List<TagScope>();
    }

    /// <summary>
    /// Request for scope preview
    /// </summary>
    public class ScopePreviewRequest
    {
        public ICollection<TagScope> TagScopes { get; set; } = new List<TagScope>();
        public ICollection<string>? ResourceTypes { get; set; }
        public bool RequireAll { get; set; } = false;
    }
}
