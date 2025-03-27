using System.Collections.Generic;

namespace Certify.Models
{
    /// <summary>
    /// The result of a new/renewed certificate request, encompassing status, most relevant message, individual task/deployment action results
    /// </summary>
    public class CertificateRequestResult
    {
        public CertificateRequestResult(ManagedCertificate item, bool isSuccess, string msg)
        {
            ManagedItem = item;
            IsSuccess = isSuccess;
            Message = msg;
        }

        public CertificateRequestResult(ManagedCertificate item)
        {
            ManagedItem = item;
            Message = string.Empty;
        }

        /// <summary>
        /// Update existing request result, preserving actions
        /// </summary>
        /// <param name="update"></param>
        public void ApplyChanges(CertificateRequestResult update)
        {
            Message = update.Message;
            IsSuccess = update.IsSuccess;
            ManagedItem = update.ManagedItem;
            Result = update.Result;
            Abort = update.Abort;
        }

        public CertificateRequestResult()
        {
            Message = string.Empty;
        }

        public ManagedCertificate? ManagedItem { get; set; }
        public bool IsSuccess { get; set; }
        public bool Abort { get; set; }
        public string? Message { get; set; }
        public object? Result { get; set; }
        public List<ActionStep> Actions { get; set; } = new();

        /// <summary>
        /// if specified, one or more of our automated challenges required a propagation delay before
        /// checking responses.
        /// </summary>
        public int ChallengeResponsePropagationSeconds { get; set; }
    }
}
