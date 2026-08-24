namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// An ACME request failure which maps to a specific RFC 8555 error type, so the error type
    /// survives back to the caller instead of being flattened to a generic malformed error.
    /// </summary>
    public class AcmeRequestException : Exception
    {
        /// <summary>
        /// The RFC 8555 error type urn to report, see <see cref="AcmeErrorResponseService.AcmeErrorTypes"/>
        /// </summary>
        public string ErrorType { get; }

        public AcmeRequestException(string errorType, string message) : base(message)
        {
            ErrorType = errorType;
        }

        public AcmeRequestException(string errorType, string message, Exception innerException) : base(message, innerException)
        {
            ErrorType = errorType;
        }
    }
}
