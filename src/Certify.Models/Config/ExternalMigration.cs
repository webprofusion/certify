#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Certify.Models.Config.Migration
{
    /// <summary>
    /// Stage reached by a managed certificate migrated from an external certificate manager (win-acme etc). Preview
    /// writes nothing, so the first stage recorded against an item is Imported
    /// </summary>
    public enum ExternalMigrationState
    {
        /// <summary>
        /// Created from the source, with auto renewal off. The source still renews the certificate
        /// </summary>
        Imported = 1,

        /// <summary>
        /// Challenge and deployment checks have passed. The source still renews the certificate
        /// </summary>
        Tested = 2,

        /// <summary>
        /// The source item is disabled and this item renews the certificate
        /// </summary>
        CutOver = 3,

        /// <summary>
        /// This item has renewed and deployed a certificate of its own since cut over
        /// </summary>
        Verified = 4,

        /// <summary>
        /// The source item has been restored after cut over and this item's auto renewal is off again
        /// </summary>
        RolledBack = 5,

        /// <summary>
        /// The source item has been deleted. Nothing further can be undone
        /// </summary>
        SourceRemoved = 6
    }

    public enum ExternalMigrationFindingLevel
    {
        /// <summary>
        /// The source setting has a direct equivalent
        /// </summary>
        Mapped = 0,

        /// <summary>
        /// Migrates, but behaviour may differ from the source. The user acknowledges it before import
        /// </summary>
        Assumption = 1,

        /// <summary>
        /// The user must supply or choose something before import
        /// </summary>
        InputNeeded = 2,

        /// <summary>
        /// Cannot migrate until the user selects a replacement, or at all if no input is offered
        /// </summary>
        Blocker = 3
    }

    public static class ExternalMigrationInputTypes
    {
        /// <summary>
        /// The value is the storage key of an existing stored credential, of <see cref="ExternalMigrationFinding.CredentialType"/> if set
        /// </summary>
        public const string Credential = "Credential";

        /// <summary>
        /// The value is one of <see cref="ExternalMigrationFinding.Options"/>
        /// </summary>
        public const string Choice = "Choice";
    }

    /// <summary>
    /// Option values of a mapped setting which can either keep the source's value or use this instance's default
    /// </summary>
    public static class ExternalMigrationChoices
    {
        public const string Source = "source";
        public const string Default = "default";
    }

    public class ExternalMigrationInputOption
    {
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;

        public ExternalMigrationInputOption() { }

        public ExternalMigrationInputOption(string value, string label)
        {
            Value = value;
            Label = label;
        }
    }

    /// <summary>
    /// One mapped, assumed, missing or blocking aspect of a migration, as shown in the preview
    /// </summary>
    public class ExternalMigrationFinding
    {
        /// <summary>
        /// Stable key, used to supply an input for this finding in <see cref="ExternalMigrationRequest.Inputs"/>
        /// </summary>
        public string Key { get; set; } = string.Empty;
        public ExternalMigrationFindingLevel Level { get; set; }

        /// <summary>
        /// Area of the source setting, e.g. Certificate, Validation, Store, Installation, Account
        /// </summary>
        public string Category { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Type of input which resolves this finding, see <see cref="ExternalMigrationInputTypes"/>. Null if none can
        /// </summary>
        public string? InputType { get; set; }

        /// <summary>
        /// For a credential input, the stored credential type (provider type) required, or null for any DNS credential
        /// </summary>
        public string? CredentialType { get; set; }

        public List<ExternalMigrationInputOption>? Options { get; set; }

        /// <summary>
        /// The input applied when the plan was prepared: the one supplied, or for a choice, the option in effect
        /// </summary>
        public string? AppliedInput { get; set; }

        /// <summary>
        /// True if this finding still prevents import
        /// </summary>
        public bool IsUnresolved => (Level == ExternalMigrationFindingLevel.Blocker || Level == ExternalMigrationFindingLevel.InputNeeded)
            && string.IsNullOrWhiteSpace(AppliedInput);
    }

    /// <summary>
    /// A secret the source holds which the migration would move into the credential store. Only a description travels in
    /// the plan, the value is read on the instance at import
    /// </summary>
    public class ExternalMigrationSecretReference
    {
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Description of the secret and where it is held, e.g. "Cloudflare token (vault://json/cf)"
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// True if the instance can read the value
        /// </summary>
        public bool IsReadable { get; set; }

        public string? Reason { get; set; }
    }

    /// <summary>
    /// A credential to create at import. Holds references to source secrets, never their values
    /// </summary>
    public class ExternalMigrationCredentialDraft
    {
        /// <summary>
        /// Key used in the proposed item in place of a stored credential key, until the credential is created
        /// </summary>
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ProviderType { get; set; } = string.Empty;

        /// <summary>
        /// Credential parameter names, mapped to the key of a <see cref="ExternalMigrationSecretReference"/> (resolved by the
        /// source at import) or, for non-secret values, to "value:" followed by the value itself
        /// </summary>
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// True for a secret used directly as a parameter value rather than stored as a credential (e.g. an acme-dns
        /// registration). The source resolves it to a single "value" entry, which replaces the draft key where it is used
        /// </summary>
        public bool IsInlineValue { get; set; }

        public const string InlineValueKey = "value";

        public const string KeyPrefix = "migration-credential:";
        public const string LiteralPrefix = "value:";

        public static bool IsDraftKey(string? credentialKey) => credentialKey?.StartsWith(KeyPrefix, StringComparison.Ordinal) == true;
    }

    /// <summary>
    /// What migrating one external item would do. Produced by preview, which writes nothing
    /// </summary>
    public class ExternalMigrationPlan
    {
        public string SourceItemId { get; set; } = string.Empty;
        public string SourceProviderId { get; set; } = string.Empty;
        public string SourceProviderTitle { get; set; } = string.Empty;
        public string SourceItemName { get; set; } = string.Empty;

        /// <summary>
        /// Path of the source item's own file
        /// </summary>
        public string? SourcePath { get; set; }

        /// <summary>
        /// Hash of the source file. Import refuses a plan whose source has changed since preview
        /// </summary>
        public string SourceFileHash { get; set; } = string.Empty;

        public DateTimeOffset DatePrepared { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// The item import would create. Credentials are referenced by draft key until created
        /// </summary>
        public ManagedCertificate? ProposedItem { get; set; }

        public List<ExternalMigrationFinding> Findings { get; set; } = new List<ExternalMigrationFinding>();
        public List<ExternalMigrationSecretReference> Secrets { get; set; } = new List<ExternalMigrationSecretReference>();
        public List<ExternalMigrationCredentialDraft> Credentials { get; set; } = new List<ExternalMigrationCredentialDraft>();

        /// <summary>
        /// True if the source's current certificate can be adopted, so no new order is needed at import
        /// </summary>
        public bool CanAdoptCurrentCertificate { get; set; }
        public DateTimeOffset? CurrentCertificateExpiry { get; set; }
        public string? CurrentCertificateThumbprint { get; set; }

        /// <summary>
        /// Set if this source item has already been imported, to the id of the item created
        /// </summary>
        public string? ExistingMigratedItemId { get; set; }

        /// <summary>
        /// Other source items which must be migrated before the source can be removed, e.g. the other orders of a split
        /// win-acme renewal
        /// </summary>
        public List<string> RelatedSourceItemIds { get; set; } = new List<string>();

        /// <summary>
        /// The plan as a list of steps, for display
        /// </summary>
        public List<ActionStep> Steps { get; set; } = new List<ActionStep>();

        public bool HasBlockers => Findings.Any(f => f.Level == ExternalMigrationFindingLevel.Blocker && f.IsUnresolved);
        public bool RequiresInput => Findings.Any(f => f.Level == ExternalMigrationFindingLevel.InputNeeded && f.IsUnresolved);
        public bool HasAssumptions => Findings.Any(f => f.Level == ExternalMigrationFindingLevel.Assumption);
        public bool CanImport => !HasBlockers && !RequiresInput && ProposedItem != null && ExistingMigratedItemId == null;

        /// <summary>
        /// Short summary of whether and how the item can migrate, for lists
        /// </summary>
        public string Summary
        {
            get
            {
                if (ExistingMigratedItemId != null)
                {
                    return "Already imported";
                }

                var blockers = Findings.Count(f => f.Level == ExternalMigrationFindingLevel.Blocker && f.IsUnresolved);
                var inputs = Findings.Count(f => f.Level == ExternalMigrationFindingLevel.InputNeeded && f.IsUnresolved);

                if (blockers > 0)
                {
                    return $"Blocked by {blockers} issue{(blockers == 1 ? "" : "s")}";
                }

                if (inputs > 0)
                {
                    return $"Can migrate after {inputs} input{(inputs == 1 ? "" : "s")}";
                }

                return HasAssumptions ? "Can migrate, review assumptions" : "Can migrate";
            }
        }
    }

    /// <summary>
    /// Preview or import request for one external item
    /// </summary>
    public class ExternalMigrationRequest
    {
        /// <summary>
        /// Hash of the source file from the preview the user reviewed. Required for import
        /// </summary>
        public string? SourceFileHash { get; set; }

        /// <summary>
        /// Inputs resolving findings, keyed by <see cref="ExternalMigrationFinding.Key"/>
        /// </summary>
        public Dictionary<string, string> Inputs { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// The user has reviewed the plan's assumptions. Required for import if the plan has any
        /// </summary>
        public bool AcknowledgeAssumptions { get; set; }

        /// <summary>
        /// Start the item with the source's current certificate instead of ordering a new one on first renewal
        /// </summary>
        public bool AdoptCurrentCertificate { get; set; } = true;
    }

    /// <summary>
    /// Options for a migration stage acting on an imported item
    /// </summary>
    public class ExternalMigrationActionRequest
    {
        /// <summary>
        /// Report what the action would do without doing it
        /// </summary>
        public bool PreviewOnly { get; set; }

        /// <summary>
        /// Remove the source before a renewal by this instance has verified the migration. Allowed only after cut over
        /// </summary>
        public bool ConfirmUnverified { get; set; }
    }

    public class ExternalMigrationResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ManagedCertificateId { get; set; }
        public ExternalMigrationState? State { get; set; }
        public List<ActionStep> Steps { get; set; } = new List<ActionStep>();

        public ExternalMigrationResult() { }

        public ExternalMigrationResult(bool isSuccess, string message)
        {
            IsSuccess = isSuccess;
            Message = message;
        }
    }

    /// <summary>
    /// Migration state recorded against a managed certificate imported from an external certificate manager
    /// </summary>
    public class ExternalMigrationInfo
    {
        public string SourceProviderId { get; set; } = string.Empty;
        public string SourceProviderTitle { get; set; } = string.Empty;

        /// <summary>
        /// Id of the external item as listed before import (ext-...)
        /// </summary>
        public string SourceItemId { get; set; } = string.Empty;
        public string SourceItemName { get; set; } = string.Empty;
        public string? SourcePath { get; set; }
        public string? SourceFileHash { get; set; }

        /// <summary>
        /// Other source items which share this item's source entry (e.g. the other orders of a split win-acme renewal).
        /// They are cut over, rolled back and removed together, because the source disables them together
        /// </summary>
        public List<string> RelatedSourceItemIds { get; set; } = new List<string>();

        public ExternalMigrationState State { get; set; } = ExternalMigrationState.Imported;

        /// <summary>
        /// Thumbprint of the certificate adopted from the source at import, if any
        /// </summary>
        public string? ImportedCertificateThumbprint { get; set; }

        public DateTimeOffset? DateImported { get; set; }
        public DateTimeOffset? DateTested { get; set; }
        public DateTimeOffset? DateCutOver { get; set; }
        public DateTimeOffset? DateVerified { get; set; }
        public DateTimeOffset? DateRolledBack { get; set; }
        public DateTimeOffset? DateSourceRemoved { get; set; }

        public bool? LastTestPassed { get; set; }
        public string? LastTestMessage { get; set; }

        /// <summary>
        /// What source removal deleted, and what it left in place
        /// </summary>
        public List<string>? SourceRemovalReport { get; set; }

        /// <summary>
        /// True while the source item is disabled but still present, so it can be restored
        /// </summary>
        public bool IsSourceHidden => State == ExternalMigrationState.CutOver || State == ExternalMigrationState.Verified;

        /// <summary>
        /// After a successful renewal by this instance, record the migration as verified if the certificate is new since
        /// cut over
        /// </summary>
        /// <returns>true if the state changed</returns>
        public bool RecordRenewalSuccess(string? currentThumbprint, DateTimeOffset now)
        {
            if (State != ExternalMigrationState.CutOver || string.IsNullOrWhiteSpace(currentThumbprint))
            {
                return false;
            }

            if (string.Equals(currentThumbprint, ImportedCertificateThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            State = ExternalMigrationState.Verified;
            DateVerified = now;
            return true;
        }

        /// <summary>
        /// Whether the source may be removed now, and if not, why not
        /// </summary>
        public ActionResult CanRemoveSource(bool confirmUnverified)
        {
            if (State == ExternalMigrationState.Verified)
            {
                return new ActionResult("The migration is verified.", true);
            }

            if (State == ExternalMigrationState.CutOver)
            {
                return confirmUnverified
                    ? new ActionResult("Removing before a renewal by this instance has verified the migration, as confirmed.", true)
                    : new ActionResult("No renewal by this instance has verified the migration yet. Renew the certificate first, or confirm removal without verification.", false);
            }

            return new ActionResult($"The source can only be removed after cut over. The migration is {State}.", false);
        }
    }

    /// <summary>
    /// What a migration source can do to its own items
    /// </summary>
    public class MigrationSourceCapabilities
    {
        /// <summary>
        /// How an item is disabled at cut over, e.g. "Renames the renewal file"
        /// </summary>
        public string DisableMethod { get; set; } = string.Empty;

        /// <summary>
        /// True if a disabled item drops out of monitoring by itself
        /// </summary>
        public bool DisabledItemHiddenFromMonitoring { get; set; }

        /// <summary>
        /// True if one item can be disabled without the others
        /// </summary>
        public bool CanDisableSingleItem { get; set; } = true;

        /// <summary>
        /// True if the tool must not be running while an item is disabled, restored or removed
        /// </summary>
        public bool RequiresIdleTool { get; set; }
    }

    /// <summary>
    /// The current certificate held by the source, with its private key
    /// </summary>
    public class SourceCertificateMaterial
    {
        public byte[]? PfxData { get; set; }
        public string? PfxPassword { get; set; }
        public string? Thumbprint { get; set; }
        public string? SourcePath { get; set; }
    }

    /// <summary>
    /// The ACME account used by the source, ready for import
    /// </summary>
    public class SourceAccountMaterial
    {
        public string AccountUri { get; set; } = string.Empty;

        /// <summary>
        /// PEM encoded private key
        /// </summary>
        public string AccountKeyPem { get; set; } = string.Empty;
        public string? EmailAddress { get; set; }
    }

    public class SourceChangeResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;

        public SourceChangeResult() { }

        public SourceChangeResult(bool isSuccess, string message)
        {
            IsSuccess = isSuccess;
            Message = message;
        }
    }

    /// <summary>
    /// Exactly what source removal deletes and what it leaves in place
    /// </summary>
    public class SourceRemovalManifest
    {
        public string SourceItemId { get; set; } = string.Empty;
        public List<string> FilesToDelete { get; set; } = new List<string>();

        /// <summary>
        /// Things left in place, with the reason, e.g. shared secrets or the scheduled task
        /// </summary>
        public List<string> Kept { get; set; } = new List<string>();

        /// <summary>
        /// Set if removal cannot proceed
        /// </summary>
        public string? BlockedReason { get; set; }
    }

    /// <summary>
    /// What the instance offers that a migration plan can map onto. Supplied by the instance, so a migration source has no
    /// dependency on the core
    /// </summary>
    public class ExternalMigrationContext
    {
        public List<CertificateAuthority> CertificateAuthorities { get; set; } = new List<CertificateAuthority>();
        public List<AccountDetails> Accounts { get; set; } = new List<AccountDetails>();
        public List<ChallengeProviderDefinition> ChallengeProviders { get; set; } = new List<ChallengeProviderDefinition>();
        public List<DeploymentProviderDefinition> DeploymentProviders { get; set; } = new List<DeploymentProviderDefinition>();

        /// <summary>
        /// Stored credentials, without their secrets
        /// </summary>
        public List<StoredCredential> Credentials { get; set; } = new List<StoredCredential>();

        /// <summary>
        /// Items already imported from external sources, by source item id
        /// </summary>
        public Dictionary<string, string> MigratedItems { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// True if the instance has the built-in http challenge server enabled
        /// </summary>
        public bool HttpChallengeServerEnabled { get; set; }

        /// <summary>
        /// This instance's renewal interval setting, which a migrated item follows unless it keeps the source's interval
        /// </summary>
        public string? RenewalIntervalMode { get; set; }
        public int RenewalIntervalDays { get; set; }

        /// <summary>
        /// This instance's default key type
        /// </summary>
        public string? DefaultKeyType { get; set; }
    }
}
