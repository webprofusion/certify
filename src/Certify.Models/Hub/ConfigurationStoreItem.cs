using System;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Common data store base type for access control, configuration and security
    /// </summary>
    public class ConfigurationStoreItem
    {
        public ConfigurationStoreItem()
        {
            Id = Guid.NewGuid().ToString();
        }
        public string Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
    }
}
