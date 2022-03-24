namespace Certify.Models.Shared
{
    public class ItemActionRequired
    {
        public string? ManagedItemId { get; set; }
        public string? InstanceId { get; set; }

        public string? NotificationEmail { get; set; } = string.Empty;

        /// <summary>
        /// Server name
        /// </summary>
        public string? InstanceTitle { get; set; } = string.Empty;

        /// <summary>
        /// Managed certificate title
        /// </summary>
        public string? ItemTitle { get; set; } = string.Empty;

        /// <summary>
        /// Required action
        /// </summary>
        public string? ActionType { get; set; } = string.Empty;

        /// <summary>
        /// Required action details
        /// </summary>
        public string? Message { get; set; } = string.Empty;

        public string? AppVersion { get; set; } = string.Empty;
    }
}
