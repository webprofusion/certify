using System;
using System.Collections.Generic;
using System.Text;
using Certify.Models;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Configuration for a managed challenge, such as a DNS challenge for a specific domain/zone
    /// A managed challenge is one the management hub can complete on behalf of another ACME client
    /// </summary>
    public class ManagedChallenge
    {
        public ManagedChallenge()
        {
            Id = Guid.NewGuid().ToString();
        }
        public string Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;

        public CertRequestChallengeConfig ChallengeConfig { get; set; }
    }
}
