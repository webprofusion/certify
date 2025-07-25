using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Providers;
using Certify.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// In-memory implementation of IManagedItemStore for testing purposes
    /// </summary>
    public class InMemoryManagedItemStore : IManagedItemStore
    {
        private readonly Dictionary<string, ManagedCertificate> _items = new Dictionary<string, ManagedCertificate>();
        private readonly object _lock = new object();
        private bool _isInitialised = false;

        public bool Init(string connectionString, ILog log)
        {
            _isInitialised = true;
            return true;
        }

        public Task<bool> IsInitialised() => Task.FromResult(_isInitialised);

        public Task DeleteAll()
        {
            lock (_lock)
            {
                _items.Clear();
            }

            return Task.CompletedTask;
        }

        public Task StoreAll(IEnumerable<ManagedCertificate> list)
        {
            lock (_lock)
            {
                foreach (var item in list)
                {
                    _items[item.Id] = CloneItem(item);
                }
            }

            return Task.CompletedTask;
        }

        public Task Delete(ManagedCertificate site)
        {
            lock (_lock)
            {
                _items.Remove(site.Id);
            }

            return Task.CompletedTask;
        }

        public Task DeleteByName(string nameStartsWith)
        {
            lock (_lock)
            {
                var toRemove = _items.Values.Where(i => i.Name.StartsWith(nameStartsWith)).ToList();
                foreach (var item in toRemove)
                {
                    _items.Remove(item.Id);
                }
            }

            return Task.CompletedTask;
        }

        public Task<ManagedCertificate> GetById(string siteId)
        {
            lock (_lock)
            {
                _items.TryGetValue(siteId, out var item);
                return Task.FromResult(item != null ? CloneItem(item) : null);
            }
        }

        public Task<List<ManagedCertificate>> Find(ManagedCertificateFilter filter)
        {
            lock (_lock)
            {
                var query = _items.Values.AsQueryable();

                // Apply basic filters
                if (!string.IsNullOrEmpty(filter.Id))
                {
                    query = query.Where(i => i.Id == filter.Id);
                }

                if (!string.IsNullOrEmpty(filter.Name))
                {
                    query = query.Where(i => i.Name.Equals(filter.Name, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrEmpty(filter.Keyword))
                {
                    query = query.Where(i => i.Name.IndexOf(filter.Keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (filter.IncludeOnlyNextAutoRenew)
                {
                    query = query.Where(i => i.IncludeInAutoRenew);
                }

                // Apply ordering
                query = query.OrderBy(i => i.DateRenewed ?? i.DateLastRenewalAttempt ?? DateTimeOffset.MinValue);

                // Apply pagination and limits
                if (filter.MaxResults > 0)
                {
                    query = query.Take(filter.MaxResults);
                }

                var results = query.Select(CloneItem).ToList();
                return Task.FromResult(results);
            }
        }

        public Task<ManagedCertificate> Update(ManagedCertificate managedCertificate)
        {
            lock (_lock)
            {
                var cloned = CloneItem(managedCertificate);
                cloned.Version += 1;
                _items[managedCertificate.Id] = cloned;
                return Task.FromResult(CloneItem(cloned));
            }
        }

        public Task PerformMaintenance()
        {
            return Task.CompletedTask;
        }

        private ManagedCertificate CloneItem(ManagedCertificate item)
        {
            // Simple clone implementation for testing
            return new ManagedCertificate
            {
                Id = item.Id,
                Name = item.Name,
                GroupId = item.GroupId,
                IncludeInAutoRenew = item.IncludeInAutoRenew,
                UseStagingMode = item.UseStagingMode,
                DateRenewed = item.DateRenewed,
                DateExpiry = item.DateExpiry,
                DateStart = item.DateStart,
                DateLastRenewalAttempt = item.DateLastRenewalAttempt,
                LastRenewalStatus = item.LastRenewalStatus,
                RenewalFailureCount = item.RenewalFailureCount,
                ServerSiteId = item.ServerSiteId,
                Version = item.Version,
                ItemType = item.ItemType,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = item.RequestConfig?.PrimaryDomain ?? "",
                    PerformAutoConfig = item.RequestConfig?.PerformAutoConfig ?? true,
                    PerformAutomatedCertBinding = item.RequestConfig?.PerformAutomatedCertBinding ?? true,
                    Challenges = item.RequestConfig?.Challenges ?? new ObservableCollection<CertRequestChallengeConfig>()
                }
            };
        }

        public Task<long> CountAll(ManagedCertificateFilter filter)
        {
            lock (_lock)
            {
                var query = _items.Values.AsQueryable();
                // Apply basic filters
                if (!string.IsNullOrEmpty(filter.Id))
                {
                    query = query.Where(i => i.Id == filter.Id);
                }

                if (!string.IsNullOrEmpty(filter.Name))
                {
                    query = query.Where(i => i.Name.Equals(filter.Name, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrEmpty(filter.Keyword))
                {
                    query = query.Where(i => i.Name.IndexOf(filter.Keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (filter.IncludeOnlyNextAutoRenew)
                {
                    query = query.Where(i => i.IncludeInAutoRenew);
                }

                return Task.FromResult((long)query.Count());
            }
        }
    }

    /// <summary>
    /// Mock implementation of ILog for testing
    /// </summary>
    public class MockLog : ILog
    {
        public List<string> LogEntries { get; } = new List<string>();

        public void Verbose(string template, params object[] propertyValues) => LogEntries.Add($"VERBOSE: {string.Format(template, propertyValues)}");
        public void Debug(string template, params object[] propertyValues) => LogEntries.Add($"DEBUG: {string.Format(template, propertyValues)}");
        public void Information(string template, params object[] propertyValues) => LogEntries.Add($"INFO: {string.Format(template, propertyValues)}");
        public void Warning(string template, params object[] propertyValues) => LogEntries.Add($"WARNING: {string.Format(template, propertyValues)}");
        public void Error(string template, params object[] propertyValues) => LogEntries.Add($"ERROR: {string.Format(template, propertyValues)}");
        public void Error(Exception ex, string template, params object[] propertyValues) => LogEntries.Add($"ERROR: {string.Format(template, propertyValues)} - {ex.Message}");
    }

    [TestClass]
    public class RenewalManagerTests
    {
        private InMemoryManagedItemStore _itemStore;
        private MockLog _mockLog;
        private RenewalSettings _defaultSettings;
        private RenewalPrefs _defaultPrefs;
        private CancellationTokenSource _cancellationTokenSource;

        [TestInitialize]
        public void Setup()
        {
            _itemStore = new InMemoryManagedItemStore();
            _mockLog = new MockLog();
            _itemStore.Init("", _mockLog);

            _defaultSettings = new RenewalSettings
            {
                Mode = RenewalMode.Auto,
                IsPreviewMode = false
            };

            _defaultPrefs = new RenewalPrefs
            {
                RenewalIntervalDays = 30,
                RenewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal,
                MaxRenewalRequests = 10,
                PerformParallelRenewals = false,
                IncludeStoppedSites = false,
                SuppressSkippedItems = false
            };

            _cancellationTokenSource = new CancellationTokenSource();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _cancellationTokenSource?.Dispose();
        }

        private ManagedCertificate CreateTestManagedCertificate(string id, string name, bool includeInAutoRenew = true, DateTimeOffset? dateRenewed = null, RequestState? lastRenewalStatus = null, int renewalFailureCount = 0, string serverSiteId = null)
        {
            return new ManagedCertificate
            {
                Id = id,
                Name = name,
                IncludeInAutoRenew = includeInAutoRenew,
                UseStagingMode = true,
                DateRenewed = dateRenewed ?? DateTimeOffset.UtcNow.AddDays(-35), // Default to needing renewal
                DateExpiry = DateTimeOffset.UtcNow.AddDays(60),
                DateStart = DateTimeOffset.UtcNow.AddDays(-90),
                LastRenewalStatus = lastRenewalStatus,
                RenewalFailureCount = renewalFailureCount,
                ServerSiteId = serverSiteId,
                ItemType = ManagedCertificateType.SSL_ACME,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = $"{name.ToLower()}.example.com",
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>
                    {
                        new CertRequestChallengeConfig { ChallengeType = "http-01" }
                    },
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true
                }
            };
        }

        [TestMethod, Description("Test PerformRenewAll with no certificates")]
        public async Task TestPerformRenewAll_NoCertificates()
        {
            // Arrange - empty store
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                _defaultSettings,
                _defaultPrefs,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Assert
            Assert.AreEqual(0, results.Count, "Should return empty list when no certificates exist");
        }

        [TestMethod, Description("Test PerformRenewAll with certificates not due for renewal")]
        public async Task TestPerformRenewAll_CertificatesNotDue()
        {
            // Arrange
            var cert1 = CreateTestManagedCertificate("cert1", "Test1", dateRenewed: DateTimeOffset.UtcNow.AddDays(-5)); // Recently renewed
            var cert2 = CreateTestManagedCertificate("cert2", "Test2", dateRenewed: DateTimeOffset.UtcNow.AddDays(-10)); // Also recent

            await _itemStore.Update(cert1);
            await _itemStore.Update(cert2);

            // Act
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                _defaultSettings,
                _defaultPrefs,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Assert
            Assert.AreEqual(0, results.Count, "Should not renew certificates that are not due");
        }

        [TestMethod, Description("Test PerformRenewAll with certificates due for renewal")]
        public async Task TestPerformRenewAll_CertificatesDue()
        {
            // Arrange
            var cert1 = CreateTestManagedCertificate("cert1", "Test1", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35)); // Due for renewal
            var cert2 = CreateTestManagedCertificate("cert2", "Test2", dateRenewed: DateTimeOffset.UtcNow.AddDays(-40)); // Also due

            await _itemStore.Update(cert1);
            await _itemStore.Update(cert2);

            // Act
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                _defaultSettings,
                _defaultPrefs,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Assert
            Assert.AreEqual(2, results.Count, "Should renew both certificates that are due");
            Assert.IsTrue(results.All(r => r.IsSuccess), "All renewal attempts should be successful");
        }

        [TestMethod, Description("Test PerformRenewAll with mixed renewal scenarios")]
        public async Task TestPerformRenewAll_MixedScenarios()
        {
            // Arrange
            var cert1 = CreateTestManagedCertificate("cert1", "DueForRenewal", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35));
            var cert2 = CreateTestManagedCertificate("cert2", "NotDue", dateRenewed: DateTimeOffset.UtcNow.AddDays(-5));
            var cert3 = CreateTestManagedCertificate("cert3", "AutoRenewDisabled", includeInAutoRenew: false, dateRenewed: DateTimeOffset.UtcNow.AddDays(-35));
            var cert4 = CreateTestManagedCertificate("cert4", "HasErrors", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35), lastRenewalStatus: RequestState.Error, renewalFailureCount: 1);

            await _itemStore.Update(cert1);
            await _itemStore.Update(cert2);
            await _itemStore.Update(cert3);
            await _itemStore.Update(cert4);

            // Act
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                _defaultSettings,
                _defaultPrefs,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Assert
            Assert.AreEqual(2, results.Count, "Should renew cert1 (due) and cert4 (has errors but due)");
            var renewedIds = results.Select(r => r.ManagedItem.Id).ToList();
            Assert.IsTrue(renewedIds.Contains("cert1"), "cert1 should be renewed");
            Assert.IsTrue(renewedIds.Contains("cert4"), "cert4 should be renewed despite errors");
        }

        [TestMethod, Description("Test PerformRenewAll with specific target certificates")]
        public async Task TestPerformRenewAll_SpecificTargets()
        {
            // Arrange
            var cert1 = CreateTestManagedCertificate("cert1", "Test1");
            var cert2 = CreateTestManagedCertificate("cert2", "Test2");
            var cert3 = CreateTestManagedCertificate("cert3", "Test3");

            await _itemStore.Update(cert1);
            await _itemStore.Update(cert2);
            await _itemStore.Update(cert3);

            var targetSettings = new RenewalSettings
            {
                Mode = RenewalMode.Auto,
                IsPreviewMode = false,
                TargetManagedCertificates = new List<string> { "cert1", "cert3" }
            };

            // Act
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                targetSettings,
                _defaultPrefs,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Assert
            Assert.AreEqual(2, results.Count, "Should only process targeted certificates");
            var renewedIds = results.Select(r => r.ManagedItem.Id).ToList();
            Assert.IsTrue(renewedIds.Contains("cert1"), "cert1 should be renewed");
            Assert.IsTrue(renewedIds.Contains("cert3"), "cert3 should be renewed");
            Assert.IsFalse(renewedIds.Contains("cert2"), "cert2 should not be renewed");
        }

        [TestMethod, Description("Test PerformRenewAll with max renewal requests limit")]
        public async Task TestPerformRenewAll_MaxRequestsLimit()
        {
            // Arrange - Create more certificates than the limit
            var certificates = new List<ManagedCertificate>();
            for (int i = 1; i <= 5; i++)
            {
                var cert = CreateTestManagedCertificate($"cert{i}", $"Test{i}", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35));
                certificates.Add(cert);
                await _itemStore.Update(cert);
            }

            var limitedPrefs = new RenewalPrefs
            {
                RenewalIntervalDays = 30,
                RenewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal,
                MaxRenewalRequests = 3, // Limit to 3 certificates
                PerformParallelRenewals = false
            };

            // Act
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                _defaultSettings,
                limitedPrefs,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Assert
            Assert.AreEqual(3, results.Count, "Should respect max renewal requests limit");
            Assert.IsTrue(results.All(r => r.IsSuccess), "All processed renewals should be successful");
        }

        [TestMethod, Description("Test PerformRenewAll with cancellation token")]
        public async Task TestPerformRenewAll_CancellationToken()
        {
            // Arrange
            var cert1 = CreateTestManagedCertificate("cert1", "Test1", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35));
            await _itemStore.Update(cert1);

            // Cancel immediately
            _cancellationTokenSource.Cancel();

            // Act
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                _defaultSettings,
                _defaultPrefs,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Assert
            Assert.AreEqual(0, results.Count, "Should return empty results when cancelled");
            Assert.IsTrue(_mockLog.LogEntries.Any(log => log.Contains("cancelled")), "Should log cancellation message");
        }

        [TestMethod, Description("Test PerformRenewAll with different renewal modes")]
        public async Task TestPerformRenewAll_RenewalModes()
        {
            // Clear the store first
            await _itemStore.DeleteAll();

            // Arrange - Create different types of certificates to test different modes
            var cert2 = CreateTestManagedCertificate("cert2", "ErrorCert", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35), lastRenewalStatus: RequestState.Error);
            var cert3 = CreateTestManagedCertificate("cert3", "NormalCert", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35));

            await _itemStore.Update(cert2);
            await _itemStore.Update(cert3);

            // Test All mode
            var allSettings = new RenewalSettings { Mode = RenewalMode.All };
            var allResults = await RenewalManager.PerformRenewAll(_mockLog, _itemStore, allSettings, _defaultPrefs, ReportProgress, IsManagedCertificateRunning, PerformCertificateRequest, _cancellationTokenSource.Token);

            Assert.AreEqual(2, allResults.Count, "All mode should process all certificates");

            // Test RenewalsWithErrors mode
            var errorsSettings = new RenewalSettings { Mode = RenewalMode.RenewalsWithErrors };
            var errorsResults = await RenewalManager.PerformRenewAll(_mockLog, _itemStore, errorsSettings, _defaultPrefs, ReportProgress, IsManagedCertificateRunning, PerformCertificateRequest, _cancellationTokenSource.Token);

            Assert.AreEqual(1, errorsResults.Count, "RenewalsWithErrors mode should only process certificates with errors");
            Assert.AreEqual("cert2", errorsResults[0].ManagedItem.Id, "Should process the certificate with errors");
        }

        #region Helper Methods

        private void BeginTrackingProgress(RequestProgressState state)
        {
            // Mock implementation - just log the progress
            _mockLog.Information($"Begin tracking progress for {state.ManagedCertificate?.Name}");
        }

        private void ReportProgress(IProgress<RequestProgressState> progress, RequestProgressState state, bool logThisEvent)
        {
            // Mock implementation - just log the progress
            if (logThisEvent)
            {
                _mockLog.Information($"Progress: {state.CurrentState} - {state.Message} for {state.ManagedCertificate?.Name}");
            }
        }

        private Task<bool> IsManagedCertificateRunning(string managedCertId)
        {
            // Mock implementation - return false for "cert2" (stopped site), true for others
            return Task.FromResult(managedCertId != "cert2");
        }

        private Task<CertificateRequestResult> PerformCertificateRequest(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress, bool isPreviewMode, string renewalReason)
        {
            // Mock implementation - simulate successful certificate request
            var result = new CertificateRequestResult(managedCertificate, true, $"Mock renewal successful: {renewalReason}");

            // Simulate progress reporting
            progress?.Report(new RequestProgressState(RequestState.Running, "Mock certificate request in progress", managedCertificate));
            progress?.Report(new RequestProgressState(RequestState.Success, "Mock certificate request completed", managedCertificate));

            return Task.FromResult(result);
        }

        #endregion

        [TestMethod, Description("Test PerformRenewAll with new certificate that should renew")]
        public async Task TestPerformRenewAll_NewCertificateRenewal()
        {
            // Arrange - Create a certificate that has never been renewed and should be due for initial certificate request
            var newCert = new ManagedCertificate
            {
                Id = "new-cert",
                Name = "NewCertificate",
                IncludeInAutoRenew = true,
                UseStagingMode = true,
                DateRenewed = null, // Never been renewed
                DateExpiry = DateTimeOffset.UtcNow.AddDays(60), // Expires in 60 days 
                DateStart = DateTimeOffset.UtcNow.AddDays(-1), // Started yesterday
                ItemType = ManagedCertificateType.SSL_ACME,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "newcert.example.com",
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>
                    {
                        new CertRequestChallengeConfig { ChallengeType = "http-01" }
                    },
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true
                }
            };

            await _itemStore.Update(newCert);

            // Test if CalculateNextRenewalAttempt thinks this cert needs renewal
            var renewalCheck = ManagedCertificate.CalculateNextRenewalAttempt(newCert, _defaultPrefs.RenewalIntervalDays, _defaultPrefs.RenewalIntervalMode);
            var logInfo = $"NewCert Renewal Check: IsRenewalDue={renewalCheck.IsRenewalDue}, Reason={renewalCheck.Reason}";
            _mockLog.Information(logInfo);

            // Test NewItems mode
            var newItemsSettings = new RenewalSettings { Mode = RenewalMode.NewItems };
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                newItemsSettings,
                _defaultPrefs,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Check what was logged
            var logMessages = string.Join("\n", _mockLog.LogEntries);
            _mockLog.Information($"Log messages: {logMessages}");

            // Assert
            Assert.IsTrue(renewalCheck.IsRenewalDue, $"Certificate with DateRenewed=null should be due for renewal. Reason: {renewalCheck.Reason}");
            Assert.AreEqual(1, results.Count, $"NewItems mode should process the new certificate. Log: {logMessages}");
        }
    }
}
