using System.Collections.Generic;

namespace Certify.Models
{
    public class CertificateRequestResult
    {
        public ManagedCertificate ManagedItem { get; set; }
        public bool IsSuccess { get; set; }
        public bool Abort { get; set; }
        public string Message { get; set; }
        public object Result { get; set; }
        public List<ActionStep> Actions { get; set; }

        /// <summary>
        /// if specified, one or more of our automated challenges required a propagation delay before
        /// checking responses.
        /// </summary>
        public int ChallengeResponsePropagationSeconds { get; set; }
    }
}