using System;

namespace Certify.Models.Config
{
    public class StoredCredential
    {
        public string? ProviderType { get; set; } = string.Empty;
        public string? Title { get; set; } = string.Empty;
        public string? StorageKey { get; set; }
        public DateTimeOffset DateCreated { get; set; }

        /// <summary>
        /// Optionally set expiry date for this credential, if not set we do not expect it to expire
        /// </summary>
        public DateTimeOffset? DateExpiry { get; set; }

        /// <summary>
        /// Secret is only populated in the client when saving, the secret is not available to the UI 
        /// </summary>
        public string? Secret { get; set; }

        /// <summary>
        /// If true, this item can be unlocked for download or sharing to authorized consumers
        /// </summary>
        public bool AllowUnlock { get; set; }
    }

    public class StoredCredentialUnlockResult : ActionResult<StoredCredential>
    {

    }
}
