using System;

namespace Certify.Models.Config
{
    public class StoredCredential
    {
        public string ProviderType { get; set; }
        public string Title { get; set; }
        public string StorageKey { get; set; }
        public DateTime DateCreated { get; set; }

        /// <summary>
        /// Secret is only populated in the client when saving, the secret is not available to the UI 
        /// </summary>
        public string Secret { get; set; }
    }
}
