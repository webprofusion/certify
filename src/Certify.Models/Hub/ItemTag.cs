namespace Certify.Models.Hub
{
    /// <summary>
    /// Generic tagging for different configuration items
    /// </summary>
    public class ItemTag : ConfigurationStoreItem
    {
        public ItemTag(string taggedItemId, string taggedItemType, string tag, string? value)
        {
            TaggedItemId = taggedItemId;
            TaggedItemType = taggedItemType;
            Tag = tag;
            Value = value;
        }

        public string TaggedItemId { get; set; }
        public string TaggedItemType { get; set; }
        public string Tag { get; set; }
        public string? Value { get; set; }
    }

    public class TagDefinition : ConfigurationStoreItem
    {
        public string Tag { get; set; } = default!;
        public string? ParentTag { get; set; }
    }
}
