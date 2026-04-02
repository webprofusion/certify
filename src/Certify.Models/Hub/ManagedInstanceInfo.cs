using System;
using System.Collections.Generic;
using Certify.Models.Reporting;
using Registration.Core.Models.Shared;

namespace Certify.Models.Hub
{
    public class ManagedInstanceInfo : ConfigurationStoreItem
    {
        /// <summary>
        /// Instance Id is the unique identifier for this instance assigned by the Hub, not the clients own generated instance id
        /// </summary>
        public string InstanceId { get; set; } = string.Empty;

        /// <summary>
        /// Internal instance identifier reported by the managed instance from CoreAppSettings.InstanceId.
        /// </summary>
        public string InternalInstanceId { get; set; } = string.Empty;

        /// <summary>
        /// Linked security principal id used for access control checks.
        /// </summary>
        public string SecurityPrincipalId { get; set; } = string.Empty;

        /// <summary>
        /// Optional custom friendly name set by hub admin. If not set, use <see cref="Title"/>.
        /// </summary>
        public string? CustomTitle { get; set; }

        /// <summary>
        /// Effective friendly name for UI/display (custom title preferred).
        /// </summary>
        public string DisplayTitle => string.IsNullOrWhiteSpace(CustomTitle) ? Title : CustomTitle;

        public string OS { get; set; } = string.Empty;
        public string OSVersion { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;

        public List<TagSummary> Tags { get; set; } = [];
        public DateTimeOffset DateLastReported { get; set; }
        public DateTimeOffset DateRegistered { get; set; }

        public string ConnectionStatus { get; set; } = string.Empty;
        public bool IsAuthenticated { get; set; }

        /// <summary>
        /// Indicates whether dashboard reporting is enabled for this instance.
        /// </summary>
        public bool IsDashboardEnabled { get; set; }

        /// <summary>
        /// Base64-encoded SHA-256 hash of the per-instance request authentication secret.
        /// The hash value is used as the derived HMAC key for privileged instance-authenticated hub requests.
        /// </summary>
        public string RequestAuthSecretHash { get; set; } = string.Empty;

        public LicenseCheckResult License { get; set; } = new LicenseCheckResult();

        public StatusSummary? Summary { get; set; }
    }

    public class ConnectionStatus
    {
        public const string Connected = "connected";
        public const string Disconnected = "disconnected";
        public const string Away = "away";
    }
    public record ManagedInstanceItems
    {
        public string InstanceId { get; set; } = string.Empty;
        public DateTimeOffset? LastRefreshed { get; set; }
        public List<ManagedCertificate> Items { get; set; } = [];
    }
}
