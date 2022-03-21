namespace Certify.Models
{
    /// <summary>
    /// General purpose result for any item validation or check/test
    /// </summary>
    public class ValidationResult
    {
        public ValidationResult(bool isValid, string message, string errorCode = "ERROR")
        {
            IsValid = isValid;
            Message = message;
            ErrorCode = errorCode;
        }

        public bool IsValid { get; set; }
        public string Message { get; set; }

        /// <summary>
        /// Can optionally be used to pass back a custom error code to indicate the error type encountered.
        /// </summary>
        public string ErrorCode { get; set; } = "ERROR";
    }
}
