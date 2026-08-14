using System;
using System.Collections.Generic;

namespace Certify.Models.Hub
{
    public class ManagementHubMessages
    {
        public const string SendCommandRequest = "SendCommandRequest";
        public const string ReceiveCommandResult = "ReceiveCommandResult";
        public const string Notification = "Notification";
        public const string GetCommandResult = "GetCommandResult";
    }

    /// <summary>
    /// Command and notification identifiers used by the management hub protocol.
    /// The value of each constant is the identifier sent on the wire, and instances may be running an older version, so
    /// a value is never changed once shipped. Most constants therefore have a value matching their name, but a renamed
    /// constant deliberately keeps its original value - see the individual remarks
    /// </summary>
    public class ManagementHubCommands
    {
        public const string GetInstanceInfo = "GetInstanceInfo";
        public const string GetStatusSummary = "GetStatusSummary";
        public const string GetManagedItems = "GetManagedItems";
        public const string GetManagedItem = "GetManagedItem";
        public const string GetManagedItemLog = "GetManagedItemLog";
        public const string GetManagedItemRenewalPreview = "GetManagedItemRenewalPreview";
        public const string ExportCertificate = "ExportCertificate";

        public const string UpdateManagedItem = "UpdateManagedItem";
        public const string RemoveManagedItem = "RemoveManagedItem";
        public const string TestManagedItemConfiguration = "TestManagedItemConfiguration";
        public const string PerformManagedItemRequest = "PerformManagedItemRequest";
        public const string ResetManagedItemStatus = "ResetManagedItemStatus";

        public const string GetCertificateAuthorities = "GetCertificateAuthorities";
        public const string UpdateCertificateAuthority = "UpdateCertificateAuthority";
        public const string RemoveCertificateAuthority = "RemoveCertificateAuthority";

        public const string GetAcmeAccounts = "GetAcmeAccounts";
        public const string AddAcmeAccount = "AddAcmeAccount";
        public const string RemoveAcmeAccount = "RemoveAcmeAccount";

        public const string GetStoredCredentials = "GetStoredCredentials";
        public const string UpdateStoredCredential = "UpdateStoredCredential";
        public const string RemoveStoredCredential = "RemoveStoredCredential";
        public const string UnlockStoredCredential = "UnlockStoredCredential";

        public const string GetChallengeProviders = "GetChallengeProviders";
        public const string GetDnsZones = "GetDnsZones";

        public const string GetDeploymentProviders = "GetDeploymentProviders";
        public const string GetDeploymentProviderDefinition = "GetDeploymentProviderDefinition";
        public const string ExecuteDeploymentTask = "ExecuteDeploymentTask";

        public const string GetTargetServiceTypes = "GetTargetServiceTypes";
        public const string GetTargetServiceItems = "GetTargetServiceItems";
        public const string GetTargetServiceItemIdentifiers = "GetTargetServiceItemIdentifiers";
        public const string GetTargetIPAddresses = "GetTargetIPAddresses";

        public const string PerformImport = "PerformImport";
        public const string PerformExport = "PerformExport";

        public const string GetSystemStatusItems = "GetSystemStatusItems";
        public const string GetSystemLogFiles = "GetSystemLogFiles";
        public const string GetSystemLog = "GetSystemLog";
        public const string GetServiceConfig = "GetServiceConfig";
        public const string GetServiceCoreSettings = "GetServiceCoreSettings";
        public const string UpdateServiceConfig = "UpdateServiceConfig";
        public const string UpdateServiceCoreSettings = "UpdateServiceCoreSettings";

        public const string GetDataStoreProviders = "GetDataStoreProviders";
        public const string GetDataStores = "GetDataStores";
        public const string TestDataStore = "TestDataStore";
        public const string UpdateDataStore = "UpdateDataStore";
        public const string SetDefaultDataStore = "SetDefaultDataStore";
        public const string CopyDataStoreToTarget = "CopyDataStoreToTarget";
        public const string RemoveDataStore = "RemoveDataStore";

        public const string ApplyLicense = "ActivateManagedLicense";
        public const string DeactivateLicense = "DeactivateManagedLicense";
        public const string QueueAllStatusReports = "QueueAllStatusReports";

        public const string Reconnect = "Reconnect";
        public const string RejoinManagementHub = "RejoinManagementHub";
        public const string RefreshExternalManagedCertificates = "RefreshExternalManagedCertificates";

        /// <summary>
        /// Notification messages are used to send topic specific info ad-hoc back to the mgmt hub
        /// </summary>
        public const string NotificationRemovedManagedItem = "NotificationRemovedManagedItem";
        public const string NotificationUpdatedManagedItem = "NotificationUpdatedManagedItem";
        public const string NotificationManagedItemRequestProgress = "NotificationManagedItemRequestProgress";

        /// <summary>
        /// Command sent to an instance to indicate the source certificate for one of its certificate subscriptions
        /// has changed and should be fetched.
        /// </summary>
        /// <remarks>
        /// The value is part of the management hub protocol and is not changed when the name is, so that instances
        /// running an older version are still understood
        /// </remarks>
        public const string PushSubscriptionUpdate = "PushExternalManagedCertificateUpdate";

        /// <summary>
        /// Notification from an instance to request the hub trigger a certificate subscription push update.
        /// </summary>
        /// <remarks>
        /// The value is part of the management hub protocol and is not changed when the name is, so that instances
        /// running an older version are still understood
        /// </remarks>
        public const string NotificationRequestSubscriptionUpdate = "NotificationRequestExternalManagedCertificateUpdate";

        /// <summary>
        /// Caller instance needs to authenticate before proceeding, they should reacquire a valid auth token and reconnect
        /// </summary>
        public const string NotificationAuthenticationRequired = "NotificationAuthenticationRequired";
    }

    public class SubscriptionUpdate
    {
        public string? ManagedCertificateId { get; set; }
        public string? SourceVersion { get; set; }
    }

    public class SubscriptionUpdateRequest
    {
        public string? TargetManagedCertificateId { get; set; }
        public string? SourceInstanceId { get; set; }
        public string? SourceManagedCertificateId { get; set; }
    }

    /// <summary>
    /// A command that can be sent asynchronously to an instance (each instance is a hub client)
    /// </summary>
    public class InstanceCommandRequest
    {
        public InstanceCommandRequest()
        {

        }

        public InstanceCommandRequest(string commandType)
        {
            CommandId = Guid.NewGuid();
            CommandType = commandType;
        }
        public InstanceCommandRequest(string commandType, KeyValuePair<string, string>[] values) : this(commandType)
        {
            Value = System.Text.Json.JsonSerializer.Serialize(values);
        }
        /// <summary>
        /// Unique ID of this command
        /// </summary>
        public Guid CommandId { get; set; }

        /// <summary>
        /// Command type
        /// </summary>
        public string CommandType { get; set; } = string.Empty;

        /// <summary>
        /// Command associated value
        /// </summary>
        public string? Value { get; set; } = string.Empty;

        /// <summary>
        /// If true we are directly returning this result to a caller
        /// </summary>
        public bool IsResultAwaited { get; set; }
    }

    /// <summary>
    /// A result (eventually) received from an instance command
    /// </summary>
    public class InstanceCommandResult
    {
        /// <summary>
        /// Guid of the original command being responded to
        /// </summary>
        public Guid CommandId { get; set; }

        public string CommandType { get; set; } = string.Empty;

        /// <summary>
        /// If false, message was sent without being requested by the management hub
        /// </summary>
        public bool IsCommandResponse { get; set; } = true;
        public string? InstanceId { get; set; }
        public DateTimeOffset? Received { get; set; }
        /// <summary>
        /// Response value
        /// </summary>
        public string? Value { get; set; }

        public object? ObjectValue { get; set; }
    }

    /// <summary>
    /// General ad-hoc message sent from an instance to the management hub such as a progress report or new/updated managed item
    /// </summary>
    public class InstanceMessage
    {
        /// <summary>
        /// Type of message instance is sending
        /// </summary>
        public string MessageType { get; set; } = string.Empty;

        /// <summary>
        /// Value of message instance is sending, to be interpreted by the management hub
        /// </summary>
        public string? Value { get; set; }
    }

    public class ManagementHubRejoinRequest
    {
        public HubJoiningClientSecret JoiningCredential { get; set; } = new();
        public bool ReissueRequestAuthSecret { get; set; } = true;
    }
}
