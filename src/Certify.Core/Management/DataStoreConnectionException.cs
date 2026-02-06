using System;

namespace Certify.Management
{
    /// <summary>
    /// Exception thrown when a data store connection or operation fails
    /// </summary>
    public class DataStoreConnectionException : Exception
    {
        /// <summary>
        /// The ID of the data store that failed to connect
        /// </summary>
        public string? DataStoreId { get; }

        /// <summary>
        /// The type of data store (e.g., sqlite, postgres, sqlserver)
        /// </summary>
        public string? DataStoreType { get; }

        public DataStoreConnectionException(string message) : base(message)
        {
        }

        public DataStoreConnectionException(string message, string? dataStoreId, string? dataStoreType) : base(message)
        {
            DataStoreId = dataStoreId;
            DataStoreType = dataStoreType;
        }

        public DataStoreConnectionException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public DataStoreConnectionException(string message, string? dataStoreId, string? dataStoreType, Exception innerException)
            : base(message, innerException)
        {
            DataStoreId = dataStoreId;
            DataStoreType = dataStoreType;
        }
    }
}
