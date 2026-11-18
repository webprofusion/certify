using System.Threading.Tasks;
using Certify.Models.Config.Migration;

namespace Certify.UI.ViewModel
{
    /// <summary>
    /// Migration of certificates from other certificate managers (win-acme, simple-acme etc) into this instance
    /// </summary>
    public partial class AppViewModel
    {
        public Task<ExternalMigrationPlan> PreviewExternalItemMigration(string externalItemId, ExternalMigrationRequest request)
            => _certifyClient.PreviewExternalItemMigration(externalItemId, request);

        public async Task<ExternalMigrationResult> PerformExternalItemImport(string externalItemId, ExternalMigrationRequest request)
            => await RefreshAfter(await _certifyClient.PerformExternalItemImport(externalItemId, request));

        public async Task<ExternalMigrationResult> TestExternalItemMigration(string managedItemId)
            => await RefreshAfter(await _certifyClient.TestExternalItemMigration(managedItemId));

        public async Task<ExternalMigrationResult> CutOverExternalItemMigration(string managedItemId)
            => await RefreshAfter(await _certifyClient.CutOverExternalItemMigration(managedItemId));

        public async Task<ExternalMigrationResult> RollbackExternalItemMigration(string managedItemId)
            => await RefreshAfter(await _certifyClient.RollbackExternalItemMigration(managedItemId));

        public async Task<ExternalMigrationResult> RemoveExternalItemSource(string managedItemId, ExternalMigrationActionRequest request)
        {
            var result = await _certifyClient.RemoveExternalItemSource(managedItemId, request);

            return request.PreviewOnly ? result : await RefreshAfter(result);
        }

        /// <summary>
        /// Each stage can add, change or hide items (the source item is hidden on cut over), so the whole list is reloaded
        /// </summary>
        private async Task<ExternalMigrationResult> RefreshAfter(ExternalMigrationResult result)
        {
            await RefreshManagedCertificates();

            return result;
        }
    }
}
