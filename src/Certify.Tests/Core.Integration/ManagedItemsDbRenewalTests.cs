using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Certify.Datastore.SQLite;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests
{
    [TestClass]
    public class ManagedItemsDbRenewalTests
    {
        private const string ManagedItemsDbPathEnvVar = "CERTIFY_TEST_MANAGEDITEMS_DB";
        private const string RunRealRenewalsEnvVar = "CERTIFY_TEST_REAL_RENEWALS";
        private const int MaxManagedItemsToInspect = 5000;

        [TestMethod, Description("Load a specific manageditems.db and perform renewals for due items.")]
        public async Task TestManagedItemsDbDueRenewals()
        {
            var sourceDbPath = Environment.GetEnvironmentVariable(ManagedItemsDbPathEnvVar);
            if (string.IsNullOrWhiteSpace(sourceDbPath))
            {
                Assert.Inconclusive($"Set {ManagedItemsDbPathEnvVar} to a manageditems.db path to run this integration test.");
                return;
            }

            if (!File.Exists(sourceDbPath))
            {
                Assert.Inconclusive($"Managed items database file not found: {sourceDbPath}");
                return;
            }

            var originalAppDataPath = Environment.GetEnvironmentVariable("CERTIFY_APPDATA_PATH");
            var tempAppDataPath = Path.Combine(Path.GetTempPath(), $"certify-manageditems-renewals-{Guid.NewGuid():N}");
            var runRealRenewals = string.Equals(Environment.GetEnvironmentVariable(RunRealRenewalsEnvVar), "true", StringComparison.OrdinalIgnoreCase);

            CertifyManager manager = null;

            try
            {
                Directory.CreateDirectory(tempAppDataPath);

                CopySqliteDatabaseWithSidecars(sourceDbPath, Path.Combine(tempAppDataPath, $"{SQLiteStoreBase.ITEMMANAGERCONFIG}.db"));

                Environment.SetEnvironmentVariable("CERTIFY_APPDATA_PATH", tempAppDataPath);

                manager = new CertifyManager();
                await manager.Init();

                var managedItems = await manager.GetManagedCertificates(new ManagedCertificateFilter
                {
                    MaxResults = MaxManagedItemsToInspect
                });

                Assert.IsTrue(managedItems.Count > 0, "Expected managed items to be loaded from manageditems.db");

                var dueManagedItemIds = GetDueManagedItemIds(managedItems);

                if (!dueManagedItemIds.Any())
                {
                    Assert.Inconclusive("No due managed items were found in the provided manageditems.db.");
                    return;
                }

                var renewalResults = await manager.PerformRenewAll(new RenewalSettings
                {
                    Mode = RenewalMode.Auto,
                    IsPreviewMode = !runRealRenewals,
                    TargetManagedCertificates = dueManagedItemIds

                }, CancellationToken.None);

                Assert.IsTrue(renewalResults.Count > 0, "Expected at least one renewal result for due managed items.");

                var attemptedIds = renewalResults
                    .Where(r => r.ManagedItem?.Id != null)
                    .Select(r => r.ManagedItem.Id)
                    .Distinct()
                    .ToList();

                var unexpectedIds = attemptedIds.Where(id => !dueManagedItemIds.Contains(id)).ToList();
                Assert.AreEqual(0, unexpectedIds.Count, "Renewal attempted items outside of the due managed item list.");
            }
            finally
            {
                manager?.Dispose();

                Environment.SetEnvironmentVariable("CERTIFY_APPDATA_PATH", originalAppDataPath);

                if (Directory.Exists(tempAppDataPath))
                {
                    Directory.Delete(tempAppDataPath, recursive: true);
                }
            }
        }

        private static List<string> GetDueManagedItemIds(List<ManagedCertificate> managedItems)
        {
            var renewalIntervalDays = CoreAppSettings.Current.RenewalIntervalDays;
            var renewalIntervalMode = CoreAppSettings.Current.RenewalIntervalMode ?? RenewalIntervalModes.DaysAfterLastRenewal;

            return managedItems
                .Where(item => item.IncludeInAutoRenew)
                .Where(item => item.LastRenewalStatus != RequestState.Paused)
                .Select(item => new
                {
                    Item = item,
                    DueInfo = ManagedCertificate.CalculateNextRenewalAttempt(item, renewalIntervalDays, renewalIntervalMode)
                })
                .Where(x => x.DueInfo?.IsRenewalDue == true && !x.DueInfo.IsRenewalOnHold)
                .Select(x => x.Item.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
        }

        private static void CopySqliteDatabaseWithSidecars(string sourceDbPath, string destinationDbPath)
        {
            var destinationDirectory = Path.GetDirectoryName(destinationDbPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory) && !Directory.Exists(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(sourceDbPath, destinationDbPath, overwrite: true);

            var sidecars = new[] { "-wal", "-shm" };
            foreach (var suffix in sidecars)
            {
                var sourceSidecar = sourceDbPath + suffix;
                var destinationSidecar = destinationDbPath + suffix;

                if (File.Exists(sourceSidecar))
                {
                    File.Copy(sourceSidecar, destinationSidecar, overwrite: true);
                }
            }
        }
    }
}
