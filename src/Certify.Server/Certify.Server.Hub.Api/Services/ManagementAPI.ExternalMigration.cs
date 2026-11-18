using System.Text.Json;
using Certify.Client;
using Certify.Models;
using Certify.Models.Config.Migration;
using Certify.Models.Hub;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Migration of externally managed certificates (win-acme etc) into managed certificates. Every stage runs on the
    /// instance which can read the source, the hub only forwards the command and records it
    /// </summary>
    public partial class ManagementAPI
    {
        // reading a source and resolving DNS zones can take longer than the default wait
        private static readonly TimeSpan MigrationCommandTimeout = TimeSpan.FromSeconds(120);

        public async Task<ExternalMigrationPlan?> PreviewExternalItemMigration(string instanceId, string managedCertId, ExternalMigrationRequest? request, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                new("managedCertId", managedCertId),
                new("request", JsonSerializer.Serialize(request ?? new ExternalMigrationRequest()))
            };

            return await PerformInstanceCommandTaskWithResult<ExternalMigrationPlan>(instanceId, args, ManagementHubCommands.PreviewExternalItemMigration, MigrationCommandTimeout);
        }

        public async Task<ExternalMigrationResult> PerformExternalItemImport(string instanceId, string managedCertId, ExternalMigrationRequest request, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                new("managedCertId", managedCertId),
                new("request", JsonSerializer.Serialize(request ?? new ExternalMigrationRequest()))
            };

            var result = await RunMigrationCommand(instanceId, args, ManagementHubCommands.PerformExternalItemImport);

            await RecordMigrationStage(instanceId, result.ManagedCertificateId ?? managedCertId, "Import", result, currentAuthContext);

            return result;
        }

        public async Task<ExternalMigrationResult> TestExternalItemMigration(string instanceId, string managedCertId, AuthContext? currentAuthContext)
        {
            var result = await RunMigrationCommand(instanceId, [new("managedCertId", managedCertId)], ManagementHubCommands.TestExternalItemMigration);

            await RecordMigrationStage(instanceId, managedCertId, "Test", result, currentAuthContext);

            return result;
        }

        public async Task<ExternalMigrationResult> CutOverExternalItemMigration(string instanceId, string managedCertId, AuthContext? currentAuthContext)
        {
            var result = await RunMigrationCommand(instanceId, [new("managedCertId", managedCertId)], ManagementHubCommands.CutOverExternalItemMigration);

            await RecordMigrationStage(instanceId, managedCertId, "Cut over", result, currentAuthContext);

            return result;
        }

        public async Task<ExternalMigrationResult> RollbackExternalItemMigration(string instanceId, string managedCertId, AuthContext? currentAuthContext)
        {
            var result = await RunMigrationCommand(instanceId, [new("managedCertId", managedCertId)], ManagementHubCommands.RollbackExternalItemMigration);

            await RecordMigrationStage(instanceId, managedCertId, "Rollback", result, currentAuthContext);

            return result;
        }

        public async Task<ExternalMigrationResult> RemoveExternalItemSource(string instanceId, string managedCertId, ExternalMigrationActionRequest request, AuthContext? currentAuthContext)
        {
            var args = new KeyValuePair<string, string>[] {
                new("managedCertId", managedCertId),
                new("request", JsonSerializer.Serialize(request ?? new ExternalMigrationActionRequest()))
            };

            var result = await RunMigrationCommand(instanceId, args, ManagementHubCommands.RemoveExternalItemSource);

            if (request?.PreviewOnly != true)
            {
                // the removal report names what was deleted, so it is kept with the event
                var deleted = string.Join("; ", result.Steps.Select(s => s.Description).Where(d => !string.IsNullOrWhiteSpace(d)));
                await RecordMigrationStage(instanceId, managedCertId, "Remove source", result, currentAuthContext, deleted);
            }

            return result;
        }

        private async Task<ExternalMigrationResult> RunMigrationCommand(string instanceId, KeyValuePair<string, string>[] args, string commandType)
        {
            var reply = await QueryInstance<ExternalMigrationResult>(instanceId, args, commandType, MigrationCommandTimeout);

            if (reply.Value == null)
            {
                return new ExternalMigrationResult(false, reply.Message ?? "The instance did not reply.");
            }

            if (reply.Value.IsSuccess)
            {
                // new items appear and migrated external items are hidden, so refresh this instance's item list
                await RefreshInstanceManagedItems(instanceId, null);
            }

            return reply.Value;
        }

        private async Task RecordMigrationStage(string instanceId, string managedCertId, string stage, ExternalMigrationResult result, AuthContext? currentAuthContext, string? extraDetail = null)
        {
            if (_activity == null)
            {
                return;
            }

            var detail = $"{stage}: {(result.IsSuccess ? "succeeded" : "failed")}. {result.Message}";

            if (!string.IsNullOrWhiteSpace(extraDetail))
            {
                detail += $" {extraDetail}";
            }

            await _activity.ItemChangedAsync(instanceId, managedCertId, null, ActivityEventTypes.ItemMigration, currentAuthContext, detail: detail);
        }
    }
}
