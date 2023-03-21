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
            public Uri? TermsOfService { get; set; }

            [JsonProperty("website")]
            public Uri? Website { get; set; }

            [JsonProperty("caaIdentities")]
            public IList<string>? CaaIdentities { get; set; }

            [JsonProperty("externalAccountRequired")]
            public bool? ExternalAccountRequired { get; set; }
        }

        [JsonProperty("newNonce")]
        public Uri? NewNonce { get; set; }

        [JsonProperty("newAccount")]
        public Uri? NewAccount { get; set; }

        [JsonProperty("newOrder")]
        public Uri? NewOrder { get; set; }

        [JsonProperty("revokeCert")]
        public Uri? RevokeCert { get; set; }

        [JsonProperty("keyChange")]
        public Uri? KeyChange { get; set; }

        [JsonProperty("renewalInfo")]
        public Uri? RenewalInfo { get; set; }

        [JsonProperty("meta")]
        public AcmeDirectoryMeta? Meta { get; set; }
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
