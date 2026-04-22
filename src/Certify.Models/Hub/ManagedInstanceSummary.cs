using System;
using System.Collections.Generic;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Summary information for a managed instance for public API responses.
    /// </summary>
    public record ManagedInstanceSummary
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayTitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string OS { get; set; } = string.Empty;
        public string OSVersion { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
        public List<Tag> Tags { get; set; } = [];

        public DateTimeOffset? DateLastReported { get; set; }
    }
}
