using System;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Hub level feature settings, stored as a single item in the configuration data store
    /// (rather than a config file or per-instance preferences).
    /// </summary>
    public class HubSettings : ConfigurationStoreItem
    {
        /// <summary>
        /// Fixed id for the single hub settings item in the configuration data store
        /// </summary>
        public const string SettingsId = "hub-settings";

        public HubSettings()
        {
            Id = SettingsId;
            Title = "Hub Settings";
            ItemType = nameof(HubSettings);
        }

        /// <summary>
        /// Settings for the hub Managed Challenge feature
        /// </summary>
        public ManagedChallengeSettings ManagedChallenge { get; set; } = new ManagedChallengeSettings();

        /// <summary>
        /// Settings for the hub Managed ACME feature
        /// </summary>
        public ManagedAcmeSettings ManagedAcme { get; set; } = new ManagedAcmeSettings();

        /// <summary>
        /// Settings for the hub activity history
        /// </summary>
        public ActivitySettings Activity { get; set; } = new ActivitySettings();

        /// <summary>
        /// Settings for managed certificate subscriptions
        /// </summary>
        public SubscriptionSettings Subscriptions { get; set; } = new SubscriptionSettings();

        /// <summary>
        /// Settings for managed instance connections to the hub
        /// </summary>
        public InstanceConnectionSettings InstanceConnections { get; set; } = new InstanceConnectionSettings();
    }

    /// <summary>
    /// Settings for managed instance connections to the hub
    /// </summary>
    public class InstanceConnectionSettings
    {
        /// <summary>
        /// If true, an instance holding a request auth secret must sign its joining check with it to be issued a joining
        /// token. Instances before 7.3.0 do not sign it, so they cannot connect. If false, the shared joining credentials
        /// and an instance id are enough to connect as that instance. Enabled when a new hub is installed, false on an
        /// upgraded hub until an administrator enables it.
        /// </summary>
        public bool EnforceSignedJoiningChecks { get; set; }
    }

    /// <summary>
    /// Settings for managed certificate subscriptions
    /// </summary>
    public class SubscriptionSettings
    {
        /// <summary>
        /// If true, the hub's own instance may subscribe to the managed certificates it holds itself, so that one
        /// certificate can have several subscriptions on the hub with deployment tasks split between them.
        /// Default is false, where an instance is never offered its own certificates.
        /// </summary>
        public bool AllowHubSelfSubscription { get; set; }
    }

    /// <summary>
    /// Settings for the hub activity history (activity events and request run history)
    /// </summary>
    public class ActivitySettings
    {
        public const int DefaultRetentionDays = 90;
        public const int MinRetentionDays = 7;
        public const int MaxRetentionDays = 730;

        /// <summary>
        /// Number of days activity events and request runs are kept before being removed
        /// </summary>
        public int RetentionDays { get; set; } = DefaultRetentionDays;

        /// <summary>
        /// The retention period, limited to the supported range
        /// </summary>
        public static int ResolveRetentionDays(ActivitySettings? settings)
        {
            var days = settings?.RetentionDays ?? DefaultRetentionDays;

            if (days <= 0)
            {
                return DefaultRetentionDays;
            }

            return Math.Min(Math.Max(days, MinRetentionDays), MaxRetentionDays);
        }
    }

    /// <summary>
    /// Settings for the hub Managed Challenge feature
    /// </summary>
    public class ManagedChallengeSettings
    {
        /// <summary>
        /// If true, security principals whose authorizing roles are tag-scoped may also use managed
        /// challenges which have no tags applied. Default is false (strict scoping).
        /// </summary>
        public bool AllowUnscopedForScopedPrincipals { get; set; }
    }

    /// <summary>
    /// Settings for the hub Managed ACME feature
    /// </summary>
    public class ManagedAcmeSettings
    {
    }
}
