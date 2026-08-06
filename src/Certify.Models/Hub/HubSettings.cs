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
