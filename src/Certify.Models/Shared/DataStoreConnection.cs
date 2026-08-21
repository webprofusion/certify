namespace Certify.Shared
{
    public class DataStoreConnection
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string TypeId { get; set; } = string.Empty;
        public string ConnectionConfig { get; set; } = string.Empty;
        public bool IsDefault { get; set; }

        /// <summary>
        /// True when ConnectionConfig has been masked for display and does not contain the real secret values.
        /// A masked value can be sent back unchanged when saving, which leaves the stored connection details as
        /// they are. Only set on connections being sent to a client, never on a stored connection.
        /// </summary>
        public bool IsProtected { get; set; }
    }
}
