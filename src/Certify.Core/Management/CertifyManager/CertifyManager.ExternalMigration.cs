using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Config.Migration;
using Certify.Models.Hub;
using Certify.Providers.CertificateManagers;
using Certify.Shared;
using Certify.Shared.Core.Utils.PKI;
using Newtonsoft.Json;

namespace Certify.Management
{
    /// <summary>
    /// Migration of an item managed by an external certificate manager (win-acme etc) into a managed certificate on this
    /// instance: preview, import, test, cut over, then removal of the source. See ExternalMigrationState for the stages.
    /// The source provider reads and changes its own files, this class creates and updates our side
    /// </summary>
    public partial class CertifyManager
    {
        private static readonly HashSet<string> ExternalMigrationCommands =
        [
            ManagementHubCommands.PreviewExternalItemMigration,
            ManagementHubCommands.PerformExternalItemImport,
            ManagementHubCommands.TestExternalItemMigration,
            ManagementHubCommands.CutOverExternalItemMigration,
            ManagementHubCommands.RollbackExternalItemMigration,
            ManagementHubCommands.RemoveExternalItemSource
        ];

        private async Task<object> PerformExternalMigrationCommand(InstanceCommandRequest arg)
        {
            var args = System.Text.Json.JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
            var id = args.FirstOrDefault(a => a.Key == "managedCertId").Value;
            var requestJson = args.FirstOrDefault(a => a.Key == "request").Value;

            T GetRequest<T>() where T : new() => string.IsNullOrWhiteSpace(requestJson)
                ? new T()
                : System.Text.Json.JsonSerializer.Deserialize<T>(requestJson, JsonOptions.DefaultJsonSerializerOptions) ?? new T();

            if (arg.CommandType == ManagementHubCommands.PreviewExternalItemMigration)
            {
                return await PreviewExternalItemMigration(id, GetRequest<ExternalMigrationRequest>());
            }
            else if (arg.CommandType == ManagementHubCommands.PerformExternalItemImport)
            {
                return await PerformExternalItemImport(id, GetRequest<ExternalMigrationRequest>());
            }
            else if (arg.CommandType == ManagementHubCommands.TestExternalItemMigration)
            {
                return await TestExternalItemMigration(id);
            }
            else if (arg.CommandType == ManagementHubCommands.CutOverExternalItemMigration)
            {
                return await CutOverExternalItemMigration(id);
            }
            else if (arg.CommandType == ManagementHubCommands.RollbackExternalItemMigration)
            {
                return await RollbackExternalItemMigration(id);
            }
            else
            {
                return await RemoveExternalItemSource(id, GetRequest<ExternalMigrationActionRequest>());
            }
        }

        public async Task<ExternalMigrationPlan> PreviewExternalItemMigration(string externalItemId, ExternalMigrationRequest? request)
        {
            var (source, providerId, error) = await GetMigrationSourceForExternalItem(externalItemId);

            if (source == null)
            {
                return BlockedPlan(externalItemId, providerId, error);
            }

            try
            {
                var plan = await source.PrepareMigrationPlan(externalItemId, await BuildExternalMigrationContext(), request?.Inputs);

                return plan ?? BlockedPlan(externalItemId, providerId, "The certificate manager could not describe this item. It may have been removed or changed since it was last listed.");
            }
            catch (Exception exp)
            {
                _serviceLog?.Error(exp, "External migration preview failed for {itemId}", externalItemId);
                return BlockedPlan(externalItemId, providerId, $"Preview failed: {exp.Message}");
            }
        }

        public async Task<ExternalMigrationResult> PerformExternalItemImport(string externalItemId, ExternalMigrationRequest request)
        {
            request ??= new ExternalMigrationRequest();

            var (source, providerId, error) = await GetMigrationSourceForExternalItem(externalItemId);
            if (source == null)
            {
                return new ExternalMigrationResult(false, error);
            }

            var plan = await PreviewExternalItemMigration(externalItemId, request);

            var refusal = GetImportRefusal(plan, request);
            if (refusal != null)
            {
                return new ExternalMigrationResult(false, refusal) { Steps = plan.Steps };
            }

            // the plan travels as JSON, so a copy is taken the same way
            var item = JsonConvert.DeserializeObject<ManagedCertificate>(JsonConvert.SerializeObject(plan.ProposedItem))!;

            item.Id = Guid.NewGuid().ToString();
            item.ItemType = ManagedCertificateType.SSL_ACME;
            item.IncludeInAutoRenew = false;
            item.SourceId = null;
            item.SourceName = null;
            item.InstanceId = null;

            var result = new ExternalMigrationResult { ManagedCertificateId = item.Id };
            var createdCredentials = new List<string>();

            try
            {
                var credentialKeys = await CreateMigrationCredentials(source, externalItemId, plan, result.Steps, createdCredentials);
                ApplyMigrationCredentialKeys(item, credentialKeys);

                await ResolveMigrationDnsZones(item, result.Steps);

                await EnsureMigrationAccount(source, externalItemId, item, result.Steps);

                string? adoptedThumbprint = null;
                if (request.AdoptCurrentCertificate && plan.CanAdoptCurrentCertificate)
                {
                    adoptedThumbprint = await AdoptSourceCertificate(source, externalItemId, item, result.Steps);
                }
                else
                {
                    result.Steps.Add(new ActionStep("Certificate", "No certificate adopted", "The item has no certificate until its first request by this instance."));
                }

                item.MigrationInfo = new ExternalMigrationInfo
                {
                    SourceProviderId = plan.SourceProviderId,
                    SourceProviderTitle = plan.SourceProviderTitle,
                    SourceItemId = externalItemId,
                    SourceItemName = plan.SourceItemName,
                    SourcePath = plan.SourcePath,
                    SourceFileHash = plan.SourceFileHash,
                    RelatedSourceItemIds = plan.RelatedSourceItemIds.ToList(),
                    State = ExternalMigrationState.Imported,
                    DateImported = DateTimeOffset.UtcNow,
                    ImportedCertificateThumbprint = adoptedThumbprint
                };

                item = await StoreManagedCertificate(item);
            }
            catch (Exception exp)
            {
                _serviceLog?.Error(exp, "External migration import failed for {itemId}", externalItemId);

                // credentials created for this import would otherwise be left unused
                foreach (var key in createdCredentials)
                {
                    try
                    {
                        await _credentialsManager.Delete(_itemManager, key);
                    }
                    catch (Exception deleteExp)
                    {
                        _serviceLog?.Warning("Could not remove credential {key} after a failed import: {error}", key, deleteExp.Message);
                    }
                }

                result.IsSuccess = false;
                result.Message = $"Import failed: {exp.Message}";
                return result;
            }

            await RefreshExternalManagedCertificateCache();

            _serviceLog?.Information("Imported {source} item {sourceItem} as managed certificate {name} [{id}], auto renewal off until cut over", plan.SourceProviderTitle, externalItemId, item.Name, item.Id);

            result.IsSuccess = true;
            result.State = ExternalMigrationState.Imported;
            result.Message = $"Imported as \"{item.Name}\". Auto renewal is off and {plan.SourceProviderTitle} still renews this certificate until cut over.";
            return result;
        }

        public async Task<ExternalMigrationResult> TestExternalItemMigration(string managedCertId)
        {
            var (item, info, error) = await GetMigratedItem(managedCertId);
            if (item == null)
            {
                return new ExternalMigrationResult(false, error);
            }

            if (info.State != ExternalMigrationState.Imported && info.State != ExternalMigrationState.Tested && info.State != ExternalMigrationState.RolledBack)
            {
                return new ExternalMigrationResult(false, $"Tests run before cut over. The migration is {info.State}.") { ManagedCertificateId = item.Id, State = info.State };
            }

            var result = new ExternalMigrationResult { ManagedCertificateId = item.Id };

            var log = ManagedCertificateLog.GetLogger(item.Id, _loggingLevelSwitch);
            var challengeResults = await TestChallenge(log, item, isPreviewMode: true);
            var challengesPassed = challengeResults.All(r => r.IsOK);

            result.Steps.Add(new ActionStep
            {
                Category = "Test",
                Title = "Challenge test",
                Description = challengesPassed ? "Challenge configuration passed." : "Challenge configuration failed.",
                HasError = !challengesPassed,
                Substeps = challengeResults.Select(r => new ActionStep { Title = r.IsOK ? "Passed" : "Failed", Description = r.Message ?? "", HasError = !r.IsOK }).ToList()
            });

            var preview = await GeneratePreview(item);
            var previewFailed = preview.Any(p => p.HasError);

            result.Steps.Add(new ActionStep
            {
                Category = "Test",
                Title = "Request and deployment preview",
                Description = "What this instance would order, the bindings it would update and the tasks it would run. Check these match what the source does today.",
                HasError = previewFailed,
                Substeps = preview
            });

            var passed = challengesPassed && !previewFailed;

            info.LastTestPassed = passed;
            info.LastTestMessage = passed ? "Tests passed." : "One or more tests failed.";
            info.DateTested = DateTimeOffset.UtcNow;

            if (passed)
            {
                info.State = ExternalMigrationState.Tested;
            }

            await StoreManagedCertificate(item);

            result.IsSuccess = passed;
            result.State = info.State;
            result.Message = passed
                ? "Tests passed. Neither test proves deployment tasks work, so the migration is verified by the first renewal after cut over."
                : "Tests failed. Fix the item and test again before cut over.";

            return result;
        }

        public async Task<ExternalMigrationResult> CutOverExternalItemMigration(string managedCertId)
        {
            var (item, info, error) = await GetMigratedItem(managedCertId);
            if (item == null)
            {
                return new ExternalMigrationResult(false, error);
            }

            if (info.State != ExternalMigrationState.Tested || info.LastTestPassed != true)
            {
                return new ExternalMigrationResult(false, $"Cut over requires passing tests. The migration is {info.State}.") { ManagedCertificateId = item.Id, State = info.State };
            }

            var source = GetMigrationSource(info.SourceProviderId);
            if (source == null)
            {
                return new ExternalMigrationResult(false, $"The certificate manager {info.SourceProviderTitle} is not available on this instance.");
            }

            // the source disables a shared entry as a whole, so every certificate it renews from that entry moves together
            var (group, missing) = await GetMigrationGroup(item);
            if (missing.Any())
            {
                return new ExternalMigrationResult(false, $"{info.SourceProviderTitle} renews this certificate from the same entry as {missing.Count} other{(missing.Count == 1 ? "" : "s")}, which it disables together. Import and test {string.Join(", ", missing)} first.") { ManagedCertificateId = item.Id, State = info.State };
            }

            var untested = group.Where(g => g.MigrationInfo!.State != ExternalMigrationState.Tested || g.MigrationInfo.LastTestPassed != true).ToList();
            if (untested.Any())
            {
                return new ExternalMigrationResult(false, $"{info.SourceProviderTitle} disables this certificate together with others from the same entry. Test {string.Join(", ", untested.Select(u => u.Name))} first.") { ManagedCertificateId = item.Id, State = info.State };
            }

            // the source stops first, so the two never renew the same certificate at once
            var disabled = await source.DisableSourceItem(info.SourceItemId);
            if (!disabled.IsSuccess)
            {
                return new ExternalMigrationResult(false, $"Could not disable the source: {disabled.Message}") { ManagedCertificateId = item.Id, State = info.State };
            }

            var stored = new List<ManagedCertificate>();

            try
            {
                foreach (var member in group)
                {
                    member.IncludeInAutoRenew = true;
                    member.MigrationInfo!.State = ExternalMigrationState.CutOver;
                    member.MigrationInfo.DateCutOver = DateTimeOffset.UtcNow;

                    await StoreManagedCertificate(member);
                    stored.Add(member);
                }
            }
            catch (Exception exp)
            {
                var restored = await source.RestoreSourceItem(info.SourceItemId);
                _serviceLog?.Error(exp, "External migration cut over failed for {name} [{id}], source restored: {restored}", item.Name, item.Id, restored.Message);

                foreach (var member in stored)
                {
                    member.IncludeInAutoRenew = false;
                    member.MigrationInfo!.State = ExternalMigrationState.Tested;
                    member.MigrationInfo.DateCutOver = null;
                    await StoreManagedCertificate(member);
                }

                return new ExternalMigrationResult(false, $"Cut over failed and the source was restored: {exp.Message}") { ManagedCertificateId = item.Id };
            }

            await RefreshExternalManagedCertificateCache();

            _serviceLog?.Information("Cut over {name} [{id}] from {source}: {message}", item.Name, item.Id, info.SourceProviderTitle, disabled.Message);

            var result = new ExternalMigrationResult(true, $"{disabled.Message} This instance now renews the certificate. The first successful renewal verifies the migration.")
            {
                ManagedCertificateId = item.Id,
                State = info.State,
                Steps = [new ActionStep("Cut over", "Source disabled", disabled.Message), new ActionStep("Cut over", "Auto renewal enabled", "This instance renews the certificate from now on.")]
            };

            foreach (var member in group.Where(g => g.Id != item.Id))
            {
                result.Steps.Add(new ActionStep("Cut over", "Related certificate cut over", $"{member.Name} is renewed from the same source entry, so it was cut over too."));
            }

            return result;
        }

        public async Task<ExternalMigrationResult> RollbackExternalItemMigration(string managedCertId)
        {
            var (item, info, error) = await GetMigratedItem(managedCertId);
            if (item == null)
            {
                return new ExternalMigrationResult(false, error);
            }

            if (!info.IsSourceHidden)
            {
                return new ExternalMigrationResult(false, $"Only a migration which has been cut over can be rolled back. The migration is {info.State}.") { ManagedCertificateId = item.Id, State = info.State };
            }

            var source = GetMigrationSource(info.SourceProviderId);
            if (source == null)
            {
                return new ExternalMigrationResult(false, $"The certificate manager {info.SourceProviderTitle} is not available on this instance.");
            }

            var restored = await source.RestoreSourceItem(info.SourceItemId);
            if (!restored.IsSuccess)
            {
                return new ExternalMigrationResult(false, $"Could not restore the source: {restored.Message}") { ManagedCertificateId = item.Id, State = info.State };
            }

            var (group, _) = await GetMigrationGroup(item);
            var wasVerified = group.Any(g => g.MigrationInfo!.State == ExternalMigrationState.Verified);

            var result = new ExternalMigrationResult { ManagedCertificateId = item.Id };

            foreach (var member in group.Where(g => g.MigrationInfo!.IsSourceHidden))
            {
                await MarkMigrationRolledBack(member);

                if (member.Id != item.Id)
                {
                    result.Steps.Add(new ActionStep("Rollback", "Related certificate rolled back", $"{member.Name} is renewed from the same source entry, so it was rolled back too."));
                }
            }

            await RefreshExternalManagedCertificateCache();

            var message = $"{restored.Message} Auto renewal is off here, and {info.SourceProviderTitle} renews the certificate on its own schedule.";
            if (wasVerified)
            {
                message += " The certificate this instance issued stays deployed until the source next renews.";
            }

            result.IsSuccess = true;
            result.Message = message;
            result.State = info.State;
            return result;
        }

        public async Task<ExternalMigrationResult> RemoveExternalItemSource(string managedCertId, ExternalMigrationActionRequest request)
        {
            request ??= new ExternalMigrationActionRequest();

            var (item, info, error) = await GetMigratedItem(managedCertId);
            if (item == null)
            {
                return new ExternalMigrationResult(false, error);
            }

            var source = GetMigrationSource(info.SourceProviderId);
            if (source == null)
            {
                return new ExternalMigrationResult(false, $"The certificate manager {info.SourceProviderTitle} is not available on this instance.");
            }

            // removal deletes the shared source entry, so every certificate renewed from it must be ready
            var (group, missing) = await GetMigrationGroup(item);

            var blockedReason = missing.Any()
                ? $"{info.SourceProviderTitle} renews {string.Join(", ", missing)} from the same entry, which has not been migrated."
                : null;

            foreach (var member in group)
            {
                var allowed = member.MigrationInfo!.CanRemoveSource(request.ConfirmUnverified);
                if (!allowed.IsSuccess)
                {
                    blockedReason ??= member.Id == item.Id ? allowed.Message : $"{member.Name}, renewed from the same source entry: {allowed.Message}";
                }
            }

            var manifest = await source.PlanSourceRemoval(info.SourceItemId);
            blockedReason ??= manifest.BlockedReason;

            var result = new ExternalMigrationResult { ManagedCertificateId = item.Id, State = info.State };
            result.Steps.AddRange(manifest.FilesToDelete.Select(f => new ActionStep("Delete", "Delete", f)));
            result.Steps.AddRange(manifest.Kept.Select(k => new ActionStep("Keep", "Keep", k)));

            if (request.PreviewOnly)
            {
                result.IsSuccess = blockedReason == null;
                result.Message = blockedReason ?? $"Removal would delete {manifest.FilesToDelete.Count} file{(manifest.FilesToDelete.Count == 1 ? "" : "s")}. This cannot be undone.";
                return result;
            }

            if (blockedReason != null)
            {
                result.IsSuccess = false;
                result.Message = blockedReason;
                return result;
            }

            var removed = await source.RemoveSourceItem(info.SourceItemId, manifest);
            if (!removed.IsSuccess)
            {
                result.IsSuccess = false;
                result.Message = $"Source removal failed: {removed.Message}";
                return result;
            }

            var report = manifest.FilesToDelete.Select(f => $"Deleted {f}").Concat(manifest.Kept.Select(k => $"Kept {k}")).ToList();

            foreach (var member in group)
            {
                member.MigrationInfo!.State = ExternalMigrationState.SourceRemoved;
                member.MigrationInfo.DateSourceRemoved = DateTimeOffset.UtcNow;
                member.MigrationInfo.SourceRemovalReport = report;

                await StoreManagedCertificate(member);
            }

            _serviceLog?.Information("Removed {source} source of {name} [{id}]: {files}", info.SourceProviderTitle, item.Name, item.Id, string.Join(", ", manifest.FilesToDelete));

            result.IsSuccess = true;
            result.State = info.State;
            result.Message = removed.Message;
            return result;
        }

        /// <summary>
        /// If a migrated item is removed while its source is still disabled, nothing would renew the certificate, so the
        /// source is restored first. Items renewed from the same source entry are rolled back with it
        /// </summary>
        private async Task RestoreMigrationSourceBeforeDelete(ManagedCertificate item)
        {
            if (item.MigrationInfo?.IsSourceHidden != true)
            {
                return;
            }

            var source = GetMigrationSource(item.MigrationInfo.SourceProviderId);
            var restored = source == null
                ? new SourceChangeResult(false, "The certificate manager is not available on this instance.")
                : await source.RestoreSourceItem(item.MigrationInfo.SourceItemId);

            if (restored.IsSuccess)
            {
                _serviceLog?.Information("Restored the {source} source of deleted item {name} [{id}]: {message}", item.MigrationInfo.SourceProviderTitle, item.Name, item.Id, restored.Message);

                var (group, _) = await GetMigrationGroup(item);
                foreach (var member in group.Where(g => g.Id != item.Id && g.MigrationInfo!.IsSourceHidden))
                {
                    await MarkMigrationRolledBack(member);
                }
            }
            else
            {
                _serviceLog?.Warning("Deleted item {name} [{id}] was migrated from {source}, but the source could not be restored, so nothing renews this certificate: {message}", item.Name, item.Id, item.MigrationInfo.SourceProviderTitle, restored.Message);
            }

            await RefreshExternalManagedCertificateCache();
        }

        private async Task MarkMigrationRolledBack(ManagedCertificate item)
        {
            item.IncludeInAutoRenew = false;
            item.MigrationInfo!.State = ExternalMigrationState.RolledBack;
            item.MigrationInfo.DateRolledBack = DateTimeOffset.UtcNow;

            await StoreManagedCertificate(item);
        }

        /// <summary>
        /// The item, plus the items migrated from other parts of the same source entry (see
        /// <see cref="ExternalMigrationInfo.RelatedSourceItemIds"/>), with the related source items not yet migrated
        /// </summary>
        private async Task<(List<ManagedCertificate> Group, List<string> Missing)> GetMigrationGroup(ManagedCertificate item)
        {
            var group = new List<ManagedCertificate> { item };
            var missing = new List<string>();

            var related = item.MigrationInfo?.RelatedSourceItemIds;
            if (related?.Any() != true)
            {
                return (group, missing);
            }

            var all = await _itemManager.Find(ManagedCertificateFilter.ALL);

            foreach (var sourceItemId in related)
            {
                var match = all.FirstOrDefault(i => i.Id != item.Id && i.MigrationInfo?.SourceItemId == sourceItemId);

                if (match == null)
                {
                    missing.Add(sourceItemId);
                }
                else
                {
                    group.Add(match);
                }
            }

            return (group, missing);
        }

        internal static string? GetImportRefusal(ExternalMigrationPlan plan, ExternalMigrationRequest request)
        {
            if (plan.ExistingMigratedItemId != null)
            {
                return "This item has already been imported.";
            }

            if (string.IsNullOrWhiteSpace(request.SourceFileHash) || !string.Equals(request.SourceFileHash, plan.SourceFileHash, StringComparison.Ordinal))
            {
                return "The source has changed since it was previewed. Preview it again before importing.";
            }

            if (plan.HasBlockers)
            {
                return "The preview has blockers which must be resolved first.";
            }

            if (plan.RequiresInput)
            {
                return "The preview needs inputs which have not been supplied.";
            }

            if (plan.HasAssumptions && !request.AcknowledgeAssumptions)
            {
                return "The preview's assumptions must be acknowledged before import.";
            }

            if (plan.ProposedItem == null)
            {
                return "The preview did not produce an item to import.";
            }

            return null;
        }

        private static ExternalMigrationPlan BlockedPlan(string externalItemId, string? providerId, string reason)
        {
            var plan = new ExternalMigrationPlan { SourceItemId = externalItemId, SourceProviderId = providerId ?? "" };

            plan.Findings.Add(new ExternalMigrationFinding
            {
                Key = "source",
                Level = ExternalMigrationFindingLevel.Blocker,
                Category = "Source",
                Title = "Cannot migrate",
                Description = reason
            });

            plan.Steps.Add(new ActionStep("Source", "Cannot migrate", reason) { HasError = true });

            return plan;
        }

        private async Task<(ICertificateManagerMigrationSource? Source, string? ProviderId, string Error)> GetMigrationSourceForExternalItem(string externalItemId)
        {
            if (!ManagedCertificate.IsExternalItemId(externalItemId))
            {
                return (null, null, "Only externally managed certificates can be migrated.");
            }

            var externalItem = (await GetExternallyManagedCertificates(new ManagedCertificateFilter { IncludeExternal = true }))
                .FirstOrDefault(i => i.Id == externalItemId);

            if (externalItem == null)
            {
                return (null, null, "This certificate is no longer reported by its certificate manager.");
            }

            var source = GetMigrationSource(externalItem.SourceId);

            return source == null
                ? (null, externalItem.SourceId, $"Migration from {externalItem.SourceName ?? externalItem.SourceId} is not supported yet.")
                : (source, externalItem.SourceId, "");
        }

        private ICertificateManagerMigrationSource? GetMigrationSource(string? providerId)
        {
            return string.IsNullOrWhiteSpace(providerId) ? null : GetCertificateManagerProvider(providerId) as ICertificateManagerMigrationSource;
        }

        private async Task<(ManagedCertificate? Item, ExternalMigrationInfo Info, string Error)> GetMigratedItem(string managedCertId)
        {
            var item = string.IsNullOrWhiteSpace(managedCertId) || ManagedCertificate.IsExternalItemId(managedCertId) ? null : await _itemManager.GetById(managedCertId);

            if (item == null)
            {
                return (null, new ExternalMigrationInfo(), "Managed certificate not found.");
            }

            if (item.MigrationInfo == null)
            {
                return (null, new ExternalMigrationInfo(), "This managed certificate was not migrated from an external certificate manager.");
            }

            return (item, item.MigrationInfo, "");
        }

        private async Task<ExternalMigrationContext> BuildExternalMigrationContext()
        {
            var storedItems = await _itemManager.Find(ManagedCertificateFilter.ALL);

            var credentials = await _credentialsManager.GetCredentials();

            return new ExternalMigrationContext
            {
                CertificateAuthorities = await GetCertificateAuthorities(),
                Accounts = await GetAccountRegistrations(),
                ChallengeProviders = await Certify.Core.Management.Challenges.ChallengeProviders.GetChallengeAPIProviders(),
                DeploymentProviders = await GetDeploymentProviders(),

                // the plan travels to the hub and the UI, so secrets are left out
                Credentials = credentials.Select(c => new StoredCredential
                {
                    StorageKey = c.StorageKey,
                    Title = c.Title,
                    ProviderType = c.ProviderType,
                    DateCreated = c.DateCreated
                }).ToList(),
                MigratedItems = storedItems
                    .Where(i => !string.IsNullOrWhiteSpace(i.MigrationInfo?.SourceItemId))
                    .GroupBy(i => i.MigrationInfo!.SourceItemId)
                    .ToDictionary(g => g.Key, g => g.First().Id!),
                HttpChallengeServerEnabled = CoreAppSettings.Current.EnableHttpChallengeServer,
                RenewalIntervalMode = CoreAppSettings.Current.RenewalIntervalMode,
                RenewalIntervalDays = CoreAppSettings.Current.RenewalIntervalDays,
                DefaultKeyType = CoreAppSettings.Current.DefaultKeyType
            };
        }

        /// <summary>
        /// Create the stored credentials the plan needs, reusing one created by an earlier import of the same source
        /// secret, and return the stored credential key for each draft key
        /// </summary>
        private async Task<Dictionary<string, string>> CreateMigrationCredentials(ICertificateManagerMigrationSource source, string externalItemId, ExternalMigrationPlan plan, List<ActionStep> steps, List<string> createdCredentials)
        {
            var keys = new Dictionary<string, string>();
            var existing = await _credentialsManager.GetCredentials();

            foreach (var draft in plan.Credentials)
            {
                if (draft.IsInlineValue)
                {
                    // used as a parameter value, so nothing is stored
                    var inline = await source.ResolveCredentialValues(externalItemId, draft);

                    if (!inline.TryGetValue(ExternalMigrationCredentialDraft.InlineValueKey, out var value) || string.IsNullOrEmpty(value))
                    {
                        throw new InvalidOperationException($"{draft.Title} could not be read from the source.");
                    }

                    keys[draft.Key] = value;
                    continue;
                }

                var match = existing.FirstOrDefault(c => c.ProviderType == draft.ProviderType && c.Title == draft.Title);

                if (match?.StorageKey != null)
                {
                    keys[draft.Key] = match.StorageKey;
                    steps.Add(new ActionStep("Credentials", "Credential reused", $"{draft.Title} was created by an earlier migration."));
                    continue;
                }

                var values = await source.ResolveCredentialValues(externalItemId, draft);

                var created = await _credentialsManager.Update(new StoredCredential
                {
                    ProviderType = draft.ProviderType,
                    Title = draft.Title,
                    DateCreated = DateTimeOffset.UtcNow,
                    Secret = JsonConvert.SerializeObject(values)
                });

                if (created?.StorageKey == null)
                {
                    throw new InvalidOperationException($"The credential {draft.Title} could not be stored.");
                }

                createdCredentials.Add(created.StorageKey);
                keys[draft.Key] = created.StorageKey;
                steps.Add(new ActionStep("Credentials", "Credential created", draft.Title));
            }

            return keys;
        }

        internal static void ApplyMigrationCredentialKeys(ManagedCertificate item, Dictionary<string, string> keys)
        {
            string? Resolve(string? key) => key != null && keys.TryGetValue(key, out var storageKey) ? storageKey : key;

            foreach (var challenge in item.RequestConfig?.Challenges ?? [])
            {
                challenge.ChallengeCredentialKey = Resolve(challenge.ChallengeCredentialKey);

                foreach (var p in challenge.Parameters ?? [])
                {
                    p.Value = Resolve(p.Value);
                }
            }

            if (item.RequestConfig != null)
            {
                item.RequestConfig.CustomPrivateKey = Resolve(item.RequestConfig.CustomPrivateKey);
            }

            foreach (var task in (item.PreRequestTasks ?? []).Concat(item.PostRequestTasks ?? []))
            {
                task.ChallengeCredentialKey = Resolve(task.ChallengeCredentialKey);

                foreach (var p in task.Parameters ?? [])
                {
                    p.Value = Resolve(p.Value);
                }
            }

            item.CertificatePasswordCredentialId = Resolve(item.CertificatePasswordCredentialId);

            var tasks = (item.PreRequestTasks ?? []).Concat(item.PostRequestTasks ?? []).ToList();

            var unresolved = (item.RequestConfig?.Challenges ?? []).Select(c => c.ChallengeCredentialKey)
                .Concat((item.RequestConfig?.Challenges ?? []).SelectMany(c => c.Parameters ?? []).Select(p => p.Value))
                .Concat(tasks.Select(t => t.ChallengeCredentialKey))
                .Concat(tasks.SelectMany(t => t.Parameters ?? []).Select(p => p.Value))
                .Append(item.CertificatePasswordCredentialId)
                .Append(item.RequestConfig?.CustomPrivateKey)
                .FirstOrDefault(ExternalMigrationCredentialDraft.IsDraftKey);

            if (unresolved != null)
            {
                throw new InvalidOperationException($"The plan refers to a credential it does not define ({unresolved}).");
            }
        }

        /// <summary>
        /// DNS providers here need the zone id, which the source tools look up for themselves. Each identifier is matched to
        /// the longest zone name it falls under, with one challenge per zone
        /// </summary>
        private async Task ResolveMigrationDnsZones(ManagedCertificate item, List<ActionStep> steps)
        {
            var challenges = item.RequestConfig?.Challenges;
            if (challenges == null)
            {
                return;
            }

            var identifiers = item.GetCertificateIdentifiers().Select(i => i.Value).ToList();
            var providers = await Certify.Core.Management.Challenges.ChallengeProviders.GetChallengeAPIProviders();

            foreach (var challenge in challenges.ToList())
            {
                if (challenge.ChallengeType != SupportedChallengeTypes.CHALLENGE_TYPE_DNS
                    || !string.IsNullOrWhiteSpace(challenge.ZoneId)
                    || string.IsNullOrWhiteSpace(challenge.ChallengeCredentialKey))
                {
                    continue;
                }

                var definition = providers.FirstOrDefault(p => p.Id == challenge.ChallengeProvider);
                if (definition?.ProviderParameters?.Any(p => p.Key == "zoneid") != true)
                {
                    continue;
                }

                var zoneResult = await GetDnsProviderZones(challenge.ChallengeProvider!, challenge.ChallengeCredentialKey!);
                var zones = zoneResult?.Result?.Where(z => !string.IsNullOrWhiteSpace(z.Name)).ToList() ?? [];

                if (zoneResult?.IsSuccess != true || !zones.Any())
                {
                    steps.Add(new ActionStep("Validation", "DNS zone not found", $"Could not list zones with {definition.Title}: {zoneResult?.Message}. Set the zone on the item before testing.") { HasWarning = true });
                    continue;
                }

                var applicable = string.IsNullOrWhiteSpace(challenge.DomainMatch)
                    ? identifiers
                    : challenge.DomainMatch!.Split([';', ','], StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()).ToList();

                var byZone = MatchIdentifiersToZones(applicable, zones);

                foreach (var unmatched in applicable.Where(a => !byZone.Values.Any(v => v.Contains(a))))
                {
                    steps.Add(new ActionStep("Validation", "DNS zone not found", $"No zone available to the credential covers {unmatched}.") { HasWarning = true });
                }

                if (byZone.Count == 0)
                {
                    continue;
                }

                var first = true;
                foreach (var zone in byZone)
                {
                    var target = challenge;

                    if (!first)
                    {
                        target = JsonConvert.DeserializeObject<CertRequestChallengeConfig>(JsonConvert.SerializeObject(challenge))!;
                        challenges.Add(target);
                    }

                    target.ZoneId = zone.Key.ZoneId;
                    target.DomainMatch = byZone.Count > 1 || !string.IsNullOrWhiteSpace(challenge.DomainMatch) ? string.Join(";", zone.Value) : challenge.DomainMatch;

                    steps.Add(new ActionStep("Validation", "DNS zone resolved", $"{zone.Key.Name} for {string.Join(", ", zone.Value)}"));
                    first = false;
                }
            }
        }

        internal static Dictionary<Models.Providers.DnsZone, List<string>> MatchIdentifiersToZones(IEnumerable<string> identifiers, IEnumerable<Models.Providers.DnsZone> zones)
        {
            var result = new Dictionary<Models.Providers.DnsZone, List<string>>();

            foreach (var identifier in identifiers)
            {
                var name = identifier.TrimStart('*', '.').TrimEnd('.').ToLowerInvariant();

                var zone = zones
                    .Where(z =>
                    {
                        var zoneName = z.Name.TrimEnd('.').ToLowerInvariant();
                        return name == zoneName || name.EndsWith("." + zoneName, StringComparison.Ordinal);
                    })
                    .OrderByDescending(z => z.Name.Length)
                    .FirstOrDefault();

                if (zone != null)
                {
                    if (!result.TryGetValue(zone, out var list))
                    {
                        result[zone] = list = new List<string>();
                    }

                    list.Add(identifier);
                }
            }

            return result;
        }

        /// <summary>
        /// Accounts are chosen by CA, so an existing account is used where there is one. Otherwise the source's account is
        /// imported, which keeps the same account for CAA account binding and for EAB CAs
        /// </summary>
        private async Task EnsureMigrationAccount(ICertificateManagerMigrationSource source, string externalItemId, ManagedCertificate item, List<ActionStep> steps)
        {
            var accounts = await GetAccountRegistrations();

            if (accounts.Any(a => a.CertificateAuthorityId == item.CertificateAuthorityId && a.IsStagingAccount == item.UseStagingMode))
            {
                steps.Add(new ActionStep("Account", "Existing account used", "This instance already has an account for this CA, which is used for this item."));
                return;
            }

            var account = await source.GetAccount(externalItemId);
            if (account == null)
            {
                throw new InvalidOperationException("There is no account for this CA here and the source's account could not be read. Add an account for this CA first.");
            }

            var added = await AddAccount(new ContactRegistration
            {
                CertificateAuthorityId = item.CertificateAuthorityId ?? StandardCertAuthorities.LETS_ENCRYPT,
                IsStaging = item.UseStagingMode,
                EmailAddress = account.EmailAddress,
                AgreedToTermsAndConditions = true,
                ImportedAccountURI = account.AccountUri,
                ImportedAccountKey = account.AccountKeyPem
            });

            if (!added.IsSuccess)
            {
                throw new InvalidOperationException($"The source's account could not be imported: {added.Message}");
            }

            steps.Add(new ActionStep("Account", "Account imported", account.AccountUri));
        }

        /// <summary>
        /// Start the item with the certificate the source is using, so no new order is needed and nothing deployed
        /// changes until the first renewal here
        /// </summary>
        /// <returns>thumbprint of the adopted certificate, null if none was adopted</returns>
        private async Task<string?> AdoptSourceCertificate(ICertificateManagerMigrationSource source, string externalItemId, ManagedCertificate item, List<ActionStep> steps)
        {
            var material = await source.GetCurrentCertificate(externalItemId);

            if (material?.PfxData == null)
            {
                steps.Add(new ActionStep("Certificate", "Current certificate not adopted", "The source's current certificate could not be read, so the item has no certificate until its first request.") { HasWarning = true });
                return null;
            }

            // other tools' PFX files may repeat bag attributes, which the default loader limits refuse
            var collection = X509CertificateLoader.LoadPkcs12Collection(material.PfxData, material.PfxPassword, X509KeyStorageFlags.Exportable,
                new Pkcs12LoaderLimits { AllowDuplicateAttributes = true });
            var leaf = collection.Cast<X509Certificate2>().FirstOrDefault(c => c.HasPrivateKey)
                ?? throw new InvalidOperationException("The source's certificate file has no private key.");

            var pfxPassword = await GetPfxPassword(item);
            var identifierPath = CertificateManager.GetPrimaryIdentifierAsPath(item.RequestConfig, item.Id!);
            var assetFolder = Path.Combine(EnvironmentUtil.EnsuredAppDataPath(), "assets", identifierPath);
            Directory.CreateDirectory(assetFolder);

            var pfxPath = Path.Combine(assetFolder, $"{leaf.NotBefore:yyyyMMdd}_{leaf.Thumbprint[..8]}.pfx");
            await File.WriteAllBytesAsync(pfxPath, collection.Export(X509ContentType.Pkcs12, pfxPassword)!);

            item.CertificatePath = pfxPath;
            item.CertificateThumbprintHash = leaf.Thumbprint;
            item.CertificatePreviousThumbprintHash = null;
            item.CertificateFriendlyName = leaf.FriendlyName;
            item.DateStart = new DateTimeOffset(leaf.NotBefore);
            item.DateExpiry = new DateTimeOffset(leaf.NotAfter);
            item.DateRenewed = new DateTimeOffset(leaf.NotBefore);
            item.LastRenewalStatus = RequestState.Success;
            item.RenewalFailureCount = 0;
            item.RenewalFailureMessage = null;
            item.CertificateRevoked = false;
            item.ARICertificateId = CertUtils.GetARICertIdBase64(leaf);
            item.CertificateCurrentCA = item.CertificateAuthorityId;
            RecordCurrentCertificateDetails(item, leaf);

            steps.Add(new ActionStep("Certificate", "Current certificate adopted", $"Expires {leaf.NotAfter:yyyy-MM-dd}, thumbprint {leaf.Thumbprint}. No new order is needed."));

            if (item.RequestConfig?.ReusePrivateKey == true)
            {
                // the request reuses the key saved here, so seeding it keeps the same key as the source
                var keyPath = Path.Combine(assetFolder, "privkey.pem");

                if (File.Exists(keyPath))
                {
                    steps.Add(new ActionStep("Certificate", "Private key not replaced", $"{keyPath} already exists, so the next renewal reuses that key rather than the source's.") { HasWarning = true });
                }
                else
                {
                    // PKCS#8, as the request writes its own keys
                    using AsymmetricAlgorithm? key = (AsymmetricAlgorithm?)leaf.GetRSAPrivateKey() ?? leaf.GetECDsaPrivateKey();
                    var keyPem = key?.ExportPkcs8PrivateKeyPem();

                    if (keyPem != null)
                    {
                        await File.WriteAllTextAsync(keyPath, keyPem);
                        steps.Add(new ActionStep("Certificate", "Private key kept", "Renewals reuse the source's private key."));
                    }
                }
            }

            foreach (var c in collection)
            {
                c.Dispose();
            }

            return item.CertificateThumbprintHash;
        }
    }
}
