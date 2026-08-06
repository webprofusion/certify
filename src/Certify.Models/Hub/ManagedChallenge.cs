using System;
using System.Collections.Generic;
using Certify.Models.Config;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Configuration for a managed challenge, such as a DNS challenge for a specific domain/zone
    /// A managed challenge is one the management hub can complete on behalf of another ACME client
    /// </summary>
    public class ManagedChallenge : ConfigurationStoreItem
    {
        public CertRequestChallengeConfig? ChallengeConfig { get; set; }
    }

    /// <summary>
    /// Managed challenge with tag information for API responses
    /// </summary>
    public class ManagedChallengeSummary
    {
        /// <summary>
        /// Unique identifier for the managed challenge
        /// </summary>
        public string Id { get; set; } = default!;

        /// <summary>
        /// Title/description of the managed challenge
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Challenge configuration details
        /// </summary>
        public CertRequestChallengeConfig? ChallengeConfig { get; set; }

        /// <summary>
        /// Tags assigned to this managed challenge
        /// </summary>
        public List<TagSummary> Tags { get; set; } = [];
    }

    public class ManagedChallengeRequest
    {
        /// <summary>
        /// The type of challenge to perform (e.g. dns-01)
        /// </summary>
        public string ChallengeType { get; set; } = string.Empty;

        /// <summary>
        /// domain etc challenge is being performed for
        /// </summary>
        public string Identifier { get; set; } = string.Empty;
        public string ResponseKey { get; set; } = string.Empty;
        public string ResponseValue { get; set; } = string.Empty;
        public string AuthKey { get; set; } = string.Empty;
        public string AuthSecret { get; set; } = string.Empty;

        public DateTimeOffset? DateTimePerformed { get; set; }
        public string? ManagedCertId { get; set; }

        /// <summary>
        /// Optional security principal on whose behalf the challenge is being performed.
        /// When set, managed challenge selection honours that principal's scoped roles.
        /// </summary>
        public string? SecurityPrincipalId { get; set; }

        /// <summary>
        /// Optional assigned-role ids that further scope the principal (e.g. from EAB/API token).
        /// </summary>
        public List<string>? ScopedAssignedRoles { get; set; }
    }

    public static class ManagedChallengeOperationStates
    {
        public const string Pending = "pending";
        public const string Running = "running";
        public const string Succeeded = "succeeded";
        public const string Failed = "failed";
    }

    public class ManagedChallengeOperation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Status { get; set; } = ManagedChallengeOperationStates.Pending;
        public ManagedChallengeRequest Request { get; set; } = new ManagedChallengeRequest();
        public ActionResult? Result { get; set; }
        public DateTimeOffset DateCreated { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset DateLastUpdated { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? DateStarted { get; set; }
        public DateTimeOffset? DateCompleted { get; set; }

        public bool IsCompleted => Status == ManagedChallengeOperationStates.Succeeded || Status == ManagedChallengeOperationStates.Failed;
        public bool IsSuccess => Status == ManagedChallengeOperationStates.Succeeded;
    }
}
