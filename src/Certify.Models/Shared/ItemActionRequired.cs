namespace Certify.Models.Shared
{
    public class ItemActionRequired
    {
        public string ManagedItemId { get; set; }
        public string InstanceId { get; set; }

        public string NotificationEmail { get; set; }

        /// <summary>
        /// Server name
        /// </summary>
        public string InstanceTitle { get; set; }

        /// <summary>
        /// Managed certificate title
        /// </summary>
        public string ItemTitle { get; set; }

        /// <summary>
        /// Required action
        /// </summary>
        public string ActionType { get; set; }

        /// <summary>
        /// Required action details
        /// </summary>
        public string Message { get; set; }

        public string AppVersion { get; set; }
    }
}
