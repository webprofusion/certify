#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models.Config.Migration;

namespace Certify.Providers.CertificateManagers
{
    /// <summary>
    /// Implemented by a certificate manager provider whose items can be migrated into this instance. Every method runs on
    /// the instance which can read the source, so secrets never leave it except into its own credential store.
    /// Item ids are the external item ids reported by the provider's monitoring (ext-...)
    /// </summary>
    public interface ICertificateManagerMigrationSource
    {
        MigrationSourceCapabilities GetMigrationCapabilities();

        /// <summary>
        /// Describe what migrating the item would do. Writes nothing
        /// </summary>
        /// <param name="itemId">external item id</param>
        /// <param name="context">what the instance offers to map onto</param>
        /// <param name="inputs">inputs resolving findings of an earlier preview, keyed by finding key</param>
        Task<ExternalMigrationPlan?> PrepareMigrationPlan(string itemId, ExternalMigrationContext context, IDictionary<string, string>? inputs);

        /// <summary>
        /// Read the values for a credential draft from the plan. Secret values are only returned to the instance
        /// </summary>
        Task<Dictionary<string, string>> ResolveCredentialValues(string itemId, ExternalMigrationCredentialDraft draft);

        /// <summary>
        /// The certificate the source is currently using, with its private key, if it can be read
        /// </summary>
        Task<SourceCertificateMaterial?> GetCurrentCertificate(string itemId);

        /// <summary>
        /// The source's ACME account, if it can be read and converted
        /// </summary>
        Task<SourceAccountMaterial?> GetAccount(string itemId);

        /// <summary>
        /// Stop the source renewing the item, in a way that can be undone
        /// </summary>
        Task<SourceChangeResult> DisableSourceItem(string itemId);

        Task<SourceChangeResult> RestoreSourceItem(string itemId);

        /// <summary>
        /// Exactly what removing the disabled item would delete and keep
        /// </summary>
        Task<SourceRemovalManifest> PlanSourceRemoval(string itemId);

        /// <summary>
        /// Delete the disabled item, as described by a manifest from <see cref="PlanSourceRemoval"/>
        /// </summary>
        Task<SourceChangeResult> RemoveSourceItem(string itemId, SourceRemovalManifest manifest);
    }
}
