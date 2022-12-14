using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Certify.Models.Shared
{
    public enum ACMECompatibilityMode
    {
        /// <summary>
        /// ACME provider should follow compatibility requirements for current Let's Encrypt service
        /// </summary>
        Standard = 1,
        /// <summary>
        /// ACME provider follows compatibility requirements for alternative ACME APIs which may include deviations from spec or different behaviours
        /// </summary>
        AltProvider1 = 2
    }

    public class RenewalInfo
    {
        public Uri? ExplanationURL { get; set; }
        public RenewalWindow? SuggestedWindow { get; set; }
    }
    public class RenewalWindow
    {
        public DateTimeOffset? Start { get; set; }
        public DateTimeOffset? End { get; set; }
    }

    /// <summary>
    /// The ACME directory resource, independent of the ACME Provider implementation
    /// </summary>
    public class AcmeDirectoryInfo
    {
        public class AcmeDirectoryMeta
        {
            [JsonProperty("termsOfService")]
            public Uri? TermsOfService { get; }

            [JsonProperty("website")]
            public Uri? Website { get; }

            [JsonProperty("caaIdentities")]
            public IList<string>? CaaIdentities { get; }

            [JsonProperty("externalAccountRequired")]
            public bool? ExternalAccountRequired { get; }
        }

        [JsonProperty("newNonce")]
        public Uri? NewNonce { get; }

        [JsonProperty("newAccount")]
        public Uri? NewAccount { get; }

        [JsonProperty("newOrder")]
        public Uri? NewOrder { get; }

        [JsonProperty("revokeCert")]
        public Uri? RevokeCert { get; }

        [JsonProperty("keyChange")]
        public Uri? KeyChange { get; }

        [JsonProperty("renewalInfo")]
        public Uri? RenewalInfo { get; }

        [JsonProperty("meta")]
        public AcmeDirectoryMeta? Meta { get; }
    }

    public class PendingOrder
    {
        public PendingOrder() { }

        /// <summary>
        /// if failure message is provider a default failed pending order object is created
        /// </summary>
        /// <param name="failureMessage"></param>
        public PendingOrder(string failureMessage)
        {
            IsFailure = true;
            FailureMessage = failureMessage;
        }

        public List<PendingAuthorization> Authorizations { get; set; } = new List<PendingAuthorization>();
        public string? OrderUri { get; set; }
        public bool IsPendingAuthorizations { get; set; } = true;

        public bool IsFailure { get; set; }
        public string? FailureMessage { get; set; }
    }
}
