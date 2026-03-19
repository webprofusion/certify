using System;
using Newtonsoft.Json;

namespace Certify.Models
{
    public static class ExternalCertificateSourceTypes
    {
        public const string ManagementHub = "ManagementHub";
        public const string SecretsStore = "SecretsStore";
    }

    public static class ExternalCertificateRetrievalModes
    {
        public const string Pull = "Pull";
        public const string Push = "Push";
        public const string Auto = "PullAndPush";
    }

    public class ExternalCertificateSubscription
    {
        /// <summary>
        /// If false, this subscription is ignored.
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Source type, e.g. ManagementHub or AzureKeyVault.
        /// </summary>
        public string? SourceType { get; set; }

        /// <summary>
        /// Retrieval mode: Pull, Push, or PullAndPush.
        /// </summary>
        public string? RetrievalMode { get; set; } = ExternalCertificateRetrievalModes.Auto;

        /// <summary>
        /// Source endpoint/connection string (e.g. hub API base URL or vault URI).
        /// </summary>
        public string? SourceConnection { get; set; }

        /// <summary>
        /// Source-specific reference:
        /// - ManagementHub: "{instanceId}/{managedCertId}"
        /// - AzureKeyVault: "{secretName}" or "{secretName}/{secretVersion}"
        /// </summary>
        public string? ExternalReference { get; set; }

        /// <summary>
        /// Optional stored credential key for source authentication.
        /// </summary>
        public string? CredentialKey { get; set; }

        /// <summary>
        /// Last known display name of the remote source item.
        /// </summary>
        public string? SourceItemName { get; set; }

        /// <summary>
        /// Poll interval in minutes for pull-capable sources.
        /// </summary>
        public int PollIntervalMinutes { get; set; } = 30;

        /// <summary>
        /// Date the source was last checked.
        /// </summary>
        public DateTimeOffset? DateLastPoll { get; set; }

        /// <summary>
        /// Most recently deployed source version marker.
        /// </summary>
        public string? LastSourceVersion { get; set; }

        /// <summary>
        /// Latest pending source version marker (if waiting for maintenance window).
        /// </summary>
        public string? PendingSourceVersion { get; set; }

        /// <summary>
        /// Path to pending certificate asset awaiting deployment window.
        /// </summary>
        public string? PendingCertificatePath { get; set; }

        /// <summary>
        /// Last source error (if any).
        /// </summary>
        public string? LastError { get; set; }

        [JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public string RemoteNameOrReferenceDisplay => !string.IsNullOrWhiteSpace(SourceItemName)
            ? SourceItemName
            : !string.IsNullOrWhiteSpace(ExternalReference)
                ? ExternalReference
                : "-";

        [JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public string CurrentSyncStatus => !string.IsNullOrWhiteSpace(LastError)
            ? "Source Error"
            : !string.IsNullOrWhiteSpace(PendingCertificatePath) || !string.IsNullOrWhiteSpace(PendingSourceVersion)
                ? "Pending Deployment"
                : DateLastPoll.HasValue
                    ? !string.IsNullOrWhiteSpace(LastSourceVersion)
                        ? "In Sync"
                        : "Checked"
                    : "Awaiting First Sync";
    }
}
