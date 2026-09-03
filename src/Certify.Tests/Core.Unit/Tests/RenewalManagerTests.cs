using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// In-memory implementation of IManagedItemStore for testing purposes
    /// </summary>
    public class InMemoryManagedItemStore : IManagedItemStore
    {
        private readonly Dictionary<string, ManagedCertificate> _items = new Dictionary<string, ManagedCertificate>();
        private readonly object _lock = new object();
        private bool _isInitialised = false;

        public bool Init(string connectionString, ILog log, string instanceId = null)
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

                // Apply pagination and limits. Paging is applied the way the real data stores apply it (LIMIT/OFFSET),
                // so a caller which pages through candidates is exercised here rather than always seeing every item
                if (filter.PageIndex != null && filter.PageSize != null)
                {
                    query = query.Skip(filter.PageIndex.Value * filter.PageSize.Value).Take(filter.PageSize.Value);
                }
                else if (filter.MaxResults > 0)
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
                DateNextScheduledRenewalAttempt = item.DateNextScheduledRenewalAttempt,
                LastRenewalStatus = item.LastRenewalStatus,
                LastPrimaryRequest = item.LastPrimaryRequest == null
                    ? null
                    : new RequestStageStatus
                    {
                        Status = item.LastPrimaryRequest.Status,
                        Message = item.LastPrimaryRequest.Message
                    },
                LastBindingDeployment = item.LastBindingDeployment == null
                    ? null
                    : new RequestStageStatus
                    {
                        Status = item.LastBindingDeployment.Status,
                        Message = item.LastBindingDeployment.Message
                    },
                RenewalFailureCount = item.RenewalFailureCount,
                RenewalFailureMessage = item.RenewalFailureMessage,
                CertificateThumbprintHash = item.CertificateThumbprintHash,
                CertificatePath = item.CertificatePath,
                PostRequestTasks = item.PostRequestTasks == null
                    ? null
                    : new ObservableCollection<DeploymentTaskConfig>(item.PostRequestTasks.Select(t => new DeploymentTaskConfig
                    {
                        Id = t.Id,
                        TaskName = t.TaskName,
                        TaskTypeId = t.TaskTypeId,
                        TaskTrigger = t.TaskTrigger,
                        LastRunStatus = t.LastRunStatus,
                        LastResult = t.LastResult
                    })),
                ServerSiteId = item.ServerSiteId,
                Version = item.Version,
                ItemType = item.ItemType,
                MaintenanceWindowId = item.MaintenanceWindowId,
                ExternalSource = item.ExternalSource == null
                    ? null
                    : new ExternalCertificateSubscription
                    {
                        SourceType = item.ExternalSource.SourceType,
                        RetrievalMode = item.ExternalSource.RetrievalMode,
                        SourceConnection = item.ExternalSource.SourceConnection,
                        ExternalReference = item.ExternalSource.ExternalReference,
                        CredentialKey = item.ExternalSource.CredentialKey,
                        SourceItemName = item.ExternalSource.SourceItemName,
                        PollIntervalMinutes = item.ExternalSource.PollIntervalMinutes,
                        DateLastPoll = item.ExternalSource.DateLastPoll,
                        LastSourceVersion = item.ExternalSource.LastSourceVersion,
                        PendingSourceVersion = item.ExternalSource.PendingSourceVersion,
                        LastError = item.ExternalSource.LastError
                    },
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = item.RequestConfig?.PrimaryDomain ?? "",
                    PerformAutoConfig = item.RequestConfig?.PerformAutoConfig ?? true,
                    PerformAutomatedCertBinding = item.RequestConfig?.PerformAutomatedCertBinding ?? true,
                    Challenges = item.RequestConfig?.Challenges ?? new ObservableCollection<CertRequestChallengeConfig>()
                }
            };
        }

        private ManagedCertificate CreateExternalManagedCertificate(string id, string name, DateTimeOffset? dateRenewed = null, string? pendingSourceVersion = null)
        {
            return new ManagedCertificate
            {
                Id = id,
                Name = name,
                IncludeInAutoRenew = true,
                UseStagingMode = true,
                DateRenewed = dateRenewed ?? DateTimeOffset.UtcNow.AddDays(-35),
                DateExpiry = DateTimeOffset.UtcNow.AddDays(60),
                DateStart = DateTimeOffset.UtcNow.AddDays(-90),
                ItemType = ManagedCertificateType.SSL_ExternallyManaged,
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                    ExternalReference = $"instance/{id}",
                    PollIntervalMinutes = 30,
                    PendingSourceVersion = pendingSourceVersion
                },
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = $"{name.ToLower()}.example.com",
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>()
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

        private ManagedCertificate CreateManagedCertificateSubscription(string id, string name, DateTimeOffset? dateRenewed = null, string pendingSourceVersion = null)
        {
            return new ManagedCertificate
            {
                Id = id,
                Name = name,
                IncludeInAutoRenew = true,
                UseStagingMode = true,
                DateRenewed = dateRenewed ?? DateTimeOffset.UtcNow.AddDays(-35),
                DateExpiry = DateTimeOffset.UtcNow.AddDays(60),
                DateStart = DateTimeOffset.UtcNow.AddDays(-90),
                ItemType = ManagedCertificateType.SSL_ExternallyManaged,
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                    ExternalReference = $"instance/{id}",
                    PollIntervalMinutes = 30,
                    PendingSourceVersion = pendingSourceVersion
                },
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = $"{name.ToLower()}.example.com",
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>()
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
            Assert.IsEmpty(results, "Should return empty list when no certificates exist");
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
            Assert.IsEmpty(results, "Should not renew certificates that are not due");
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
            Assert.HasCount(2, results, "Should renew both certificates that are due");
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
            Assert.HasCount(2, results, "Should renew cert1 (due) and cert4 (has errors but due)");
            var renewedIds = results.Select(r => r.ManagedItem.Id).ToList();
            Assert.Contains("cert1", renewedIds, "cert1 should be renewed");
            Assert.Contains("cert4", renewedIds, "cert4 should be renewed despite errors");
        }

        [TestMethod, Description("Test PerformRenewAll with RenewalsWithErrors mode includes failed primary request state")]
        public async Task TestPerformRenewAll_RenewalsWithErrors_UsesLastPrimaryRequestError()
        {
            // Arrange
            var certWithPrimaryError = CreateTestManagedCertificate("cert-primary-error", "PrimaryErrorOnly", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35), lastRenewalStatus: RequestState.Success);
            certWithPrimaryError.LastPrimaryRequest = new RequestStageStatus
            {
                Status = RequestState.Error,
                Message = "Validation failed"
            };

            var healthyCert = CreateTestManagedCertificate("cert-healthy", "Healthy", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35), lastRenewalStatus: RequestState.Success);

            await _itemStore.Update(certWithPrimaryError);
            await _itemStore.Update(healthyCert);

            var settings = new RenewalSettings { Mode = RenewalMode.RenewalsWithErrors, IsPreviewMode = false };

            // Act
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                settings,
                _defaultPrefs,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Assert
            Assert.HasCount(1, results, "Only certificates with error status or failed primary request should be renewed in RenewalsWithErrors mode.");
            Assert.AreEqual("cert-primary-error", results[0].ManagedItem.Id, "Certificate with failed LastPrimaryRequest should be included.");
        }

        [TestMethod, Description("Test PerformRenewAll never attempts paused items awaiting user input, regardless of renewal mode")]
        [DataRow(RenewalMode.Auto)]
        [DataRow(RenewalMode.RenewalsDue)]
        [DataRow(RenewalMode.All)]
        [DataRow(RenewalMode.RenewalsWithErrors)]
        public async Task TestPerformRenewAll_SkipsPausedItems(RenewalMode mode)
        {
            // Arrange - paused item is otherwise due for renewal and has a failed primary request
            await _itemStore.DeleteAll();

            var pausedCert = CreateTestManagedCertificate("cert-paused", "PausedCert", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35), lastRenewalStatus: RequestState.Paused);
            pausedCert.LastPrimaryRequest = new RequestStageStatus
            {
                Status = RequestState.Error,
                Message = "Awaiting manual DNS challenge completion"
            };

            var dueCert = CreateTestManagedCertificate("cert-due", "DueCert", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35), lastRenewalStatus: RequestState.Error);

            await _itemStore.Update(pausedCert);
            await _itemStore.Update(dueCert);

            var settings = new RenewalSettings { Mode = mode, IsPreviewMode = false };

            // Act
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                settings,
                _defaultPrefs,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Assert
            Assert.IsFalse(results.Any(r => r.ManagedItem?.Id == "cert-paused"), $"Paused certificate must not be attempted in {mode} mode.");
            Assert.IsTrue(results.Any(r => r.ManagedItem?.Id == "cert-due"), $"Non-paused due certificate should still be attempted in {mode} mode.");
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
            Assert.HasCount(2, results, "Should only process targeted certificates");
            var renewedIds = results.Select(r => r.ManagedItem.Id).ToList();
            Assert.Contains("cert1", renewedIds, "cert1 should be renewed");
            Assert.Contains("cert3", renewedIds, "cert3 should be renewed");
            Assert.DoesNotContain("cert2", renewedIds, "cert2 should not be renewed");
        }

        [TestMethod, Description("Test PerformRenewAll with a specific target redeploys an item whose certificate was obtained but not fully deployed")]
        public async Task TestPerformRenewAll_SpecificTargets_RedeploysUndeployedCertificate()
        {
            // Arrange - the certificate is a day old so renewal is not due, but its deployment failed
            await _itemStore.DeleteAll();
            await _itemStore.Update(CreateItemWithUndeployedCertificate("undeployed", "Undeployed"));

            var targetSettings = new RenewalSettings
            {
                Mode = RenewalMode.Auto,
                IsPreviewMode = false,
                TargetManagedCertificates = new List<string> { "undeployed" }
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
            Assert.HasCount(1, results);
            Assert.IsTrue(_requestsPerformed.Single().RedeployOnly, "The certificate already held is deployed again rather than a new one requested");
        }

        [TestMethod, Description("Test PerformRenewAll with a specific target redeploys a subscription whose certificate was fetched but not deployed, even when its source is not due to be checked")]
        public async Task TestPerformRenewAll_SpecificTargets_RedeploysUndeployedSubscription()
        {
            // Arrange - a push only subscription is never due to poll its source, so without the redeploy it would be
            // skipped as not due and there would be no way to ask for the certificate it holds to be deployed
            await _itemStore.DeleteAll();

            var now = DateTimeOffset.UtcNow;
            var subscription = CreateManagedCertificateSubscription("sub-undeployed", "SubUndeployed", dateRenewed: now.AddDays(-1));
            subscription.ExternalSource.RetrievalMode = ExternalCertificateRetrievalModes.Push;
            subscription.DateStart = now.AddDays(-1);
            subscription.DateLastRenewalAttempt = now.AddMinutes(-10);
            subscription.CertificateThumbprintHash = "ABC123";
            subscription.LastRenewalStatus = RequestState.Error;
            subscription.RenewalFailureCount = 1;
            subscription.LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "External certificate pulled from Management Hub." };
            subscription.LastBindingDeployment = new RequestStageStatus { Status = RequestState.Error, Message = "Certificate install failed." };

            await _itemStore.Update(subscription);

            var targetSettings = new RenewalSettings
            {
                Mode = RenewalMode.Auto,
                IsPreviewMode = false,
                TargetManagedCertificates = new List<string> { "sub-undeployed" }
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
            Assert.HasCount(1, results, "The subscription is selected for its redeployment even though its source is not due to be checked");
            Assert.AreEqual("sub-undeployed", results[0].ManagedItem.Id);
            Assert.IsTrue(_requestsPerformed.Single().RedeployOnly, "The certificate already held is deployed again, the source is not involved");
        }

        [TestMethod, Description("Test PerformRenewAll excludes targeted external subscriptions that are not due and have no pending update")]
        public async Task TestPerformRenewAll_SpecificTargets_ExcludesSubscriptionsNotDue()
        {
            var externalNotDue = CreateManagedCertificateSubscription("sub-not-due", "ExternalNotDue", dateRenewed: DateTimeOffset.UtcNow.AddDays(-5));
            var normalDue = CreateTestManagedCertificate("cert1", "Test1", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35));

            await _itemStore.Update(externalNotDue);
            await _itemStore.Update(normalDue);

            var targetSettings = new RenewalSettings
            {
                Mode = RenewalMode.Auto,
                IsPreviewMode = false,
                TargetManagedCertificates = new List<string> { "sub-not-due", "cert1" }
            };

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

            Assert.HasCount(1, results, "Non-due external subscriptions should not be queued as targeted renewals.");
            Assert.AreEqual("cert1", results[0].ManagedItem.Id, "Only the due standard managed certificate should be processed.");
        }

        [TestMethod, Description("Test PerformRenewAll includes external subscriptions when a pending update exists")]
        public async Task TestPerformRenewAll_IncludesSubscriptionsWithPendingUpdate()
        {
            var externalPending = CreateManagedCertificateSubscription("sub-pending", "ExternalPending", dateRenewed: DateTimeOffset.UtcNow.AddDays(-5), pendingSourceVersion: "source-version-1");
            var externalNotDue = CreateManagedCertificateSubscription("sub-not-due", "ExternalNotDue", dateRenewed: DateTimeOffset.UtcNow.AddDays(-5));

            await _itemStore.Update(externalPending);
            await _itemStore.Update(externalNotDue);

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

            Assert.HasCount(1, results, "Only the external subscription with a pending update should be queued.");
            Assert.AreEqual("sub-pending", results[0].ManagedItem.Id, "Pending external updates should still be processed.");
            StringAssert.Contains(results[0].Message, "Pending external certificate update", "The renewal reason should indicate the pending external update.");
        }

        [TestMethod, Description("Test PerformRenewAll excludes a renewal due external subscription which is not yet due to be polled")]
        public async Task TestPerformRenewAll_ExcludesSubscriptionNotDueForPolling()
        {
            // renewal is due for this item, but its source was polled moments ago and has no update waiting, so a
            // request would have nothing to do and must not be queued (which would report a no-op status to the UI)
            var externalRecentlyPolled = CreateManagedCertificateSubscription("sub-recently-polled", "ExternalRecentlyPolled");
            externalRecentlyPolled.ExternalSource.DateLastPoll = DateTimeOffset.UtcNow;

            var normalDue = CreateTestManagedCertificate("cert1", "Test1", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35));

            await _itemStore.Update(externalRecentlyPolled);
            await _itemStore.Update(normalDue);

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

            Assert.HasCount(1, results, "An external subscription which is not due to be polled should not be queued for renewal.");
            Assert.AreEqual("cert1", results[0].ManagedItem.Id, "Only the due standard managed certificate should be processed.");
        }

        [TestMethod, Description("Test PerformRenewAll in All mode excludes an external subscription which is not yet due to be polled")]
        public async Task TestPerformRenewAll_AllMode_ExcludesSubscriptionNotDueForPolling()
        {
            var externalRecentlyPolled = CreateManagedCertificateSubscription("sub-recently-polled", "ExternalRecentlyPolled");
            externalRecentlyPolled.ExternalSource.DateLastPoll = DateTimeOffset.UtcNow;

            await _itemStore.Update(externalRecentlyPolled);

            var allModeSettings = new RenewalSettings { Mode = RenewalMode.All };

            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                allModeSettings,
                _defaultPrefs,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            Assert.IsEmpty(results, "An external subscription with nothing to fetch should not be queued, even in All mode.");
        }

        [TestMethod, Description("Test PerformRenewAll with max renewal requests limit")]
        public async Task TestPerformRenewAll_MaxRequestsLimit()
        {
            // Arrange - Create more certificates than the limit
            var certificates = new List<ManagedCertificate>();
            for (var i = 1; i <= 5; i++)
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
            Assert.HasCount(3, results, "Should respect max renewal requests limit");
            Assert.IsTrue(results.All(r => r.IsSuccess), "All processed renewals should be successful");
        }

        [TestMethod, Description("Test PerformRenewAll examines candidates beyond the first page of results")]
        public async Task TestPerformRenewAll_ScansBeyondFirstPageOfCandidates()
        {
            // Arrange - a long lifetime certificate which is nowhere near due sorts ahead of a short lifetime
            // certificate which is due, because it was renewed longer ago. With enough of them the due item falls
            // beyond the first page of candidates and is only found if the scan pages through them
            var now = DateTimeOffset.UtcNow;

            for (var i = 0; i < 100; i++)
            {
                var longLifetimeCert = CreateTestManagedCertificate($"longlife{i}", $"LongLife{i}");

                // one year certificate obtained 100 days ago, 27% of its lifetime elapsed
                longLifetimeCert.DateStart = now.AddDays(-100).AddMinutes(i);
                longLifetimeCert.DateRenewed = now.AddDays(-100).AddMinutes(i);
                longLifetimeCert.DateExpiry = now.AddDays(265).AddMinutes(i);

                await _itemStore.Update(longLifetimeCert);
            }

            // six day certificate obtained 5 days ago, 83% of its lifetime elapsed, so renewal is due
            var shortLifetimeCert = CreateTestManagedCertificate("shortlife", "ShortLife");
            shortLifetimeCert.DateStart = now.AddDays(-5);
            shortLifetimeCert.DateRenewed = now.AddDays(-5);
            shortLifetimeCert.DateExpiry = now.AddDays(1);

            await _itemStore.Update(shortLifetimeCert);

            var prefs = new RenewalPrefs
            {
                RenewalIntervalDays = 75, // percentage of lifetime
                RenewalIntervalMode = RenewalIntervalModes.PercentageLifetime,
                MaxRenewalRequests = 10,
                PerformParallelRenewals = false,
                IncludeStoppedSites = true
            };

            // Act
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                _defaultSettings,
                prefs,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Assert
            Assert.HasCount(1, results, "The due certificate should be renewed even though it is not on the first page of candidates");
            Assert.AreEqual("shortlife", results[0].ManagedItem.Id, "The short lifetime certificate is the one which was due");
        }

        /// <summary>
        /// An item which obtained a certificate a day ago, so is nowhere near due for renewal, but whose deployment of
        /// that certificate failed
        /// </summary>
        private ManagedCertificate CreateItemWithUndeployedCertificate(string id, string name, DateTimeOffset? lastAttempt = null, int renewalFailureCount = 1)
        {
            var now = DateTimeOffset.UtcNow;
            var item = CreateTestManagedCertificate(id, name, dateRenewed: now.AddDays(-1), lastRenewalStatus: RequestState.Error, renewalFailureCount: renewalFailureCount);

            item.DateStart = now.AddDays(-1);
            item.DateExpiry = now.AddDays(89);
            item.DateLastRenewalAttempt = lastAttempt ?? now.AddMinutes(-10);
            item.CertificateThumbprintHash = "ABC123";
            item.LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "New certificate received OK." };
            item.LastBindingDeployment = new RequestStageStatus { Status = RequestState.Error, Message = "Certificate install failed." };

            return item;
        }

        [TestMethod, Description("Test PerformRenewAll redeploys an item whose certificate was obtained but not fully deployed")]
        public async Task TestPerformRenewAll_RedeploysUndeployedCertificate()
        {
            // Arrange - the certificate is a day old so renewal is not due, but its deployment failed
            await _itemStore.DeleteAll();
            await _itemStore.Update(CreateItemWithUndeployedCertificate("undeployed", "Undeployed"));

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
            Assert.HasCount(1, results, "The item should be selected even though renewal is not due");
            Assert.AreEqual("undeployed", results[0].ManagedItem.Id);
            Assert.IsTrue(_requestsPerformed.Single().RedeployOnly, "The certificate already held is deployed again rather than a new one requested");
        }

        [TestMethod, Description("Test PerformRenewAll holds a redeploy which is within the failure back off")]
        public async Task TestPerformRenewAll_RedeployIsHeldByBackOff()
        {
            // Arrange - enough deployment attempts have failed for the back off to apply, and the last was recent
            await _itemStore.DeleteAll();
            await _itemStore.Update(CreateItemWithUndeployedCertificate("held", "Held", lastAttempt: DateTimeOffset.UtcNow.AddMinutes(-30), renewalFailureCount: LifetimeHealthThresholds.FailuresBeforeBackoff));

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
            Assert.HasCount(0, results, "Once enough deployment attempts have failed the next is spaced out");
        }

        [TestMethod, Description("Test PerformRenewAll attempts an item within the failure back off when a renewal is requested rather than scheduled")]
        public async Task TestPerformRenewAll_RequestedRenewalOverridesBackOff()
        {
            // Arrange - the same held item, but a person has asked for failing items to be renewed
            await _itemStore.DeleteAll();
            await _itemStore.Update(CreateItemWithUndeployedCertificate("held", "Held", lastAttempt: DateTimeOffset.UtcNow.AddMinutes(-30), renewalFailureCount: LifetimeHealthThresholds.FailuresBeforeBackoff));

            var settings = new RenewalSettings { Mode = RenewalMode.RenewalsWithErrors, IsPreviewMode = false };

            // Act
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                settings,
                _defaultPrefs,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Assert
            Assert.HasCount(1, results, "The wait after repeated failures paces the scheduled pass, not a renewal a person asked for");
            Assert.IsTrue(_requestsPerformed.Single().RedeployOnly, "The certificate already held is still what is deployed again");
        }

        [TestMethod, Description("Test PerformRenewAll redeploys on the next pass while the item is within its first attempts")]
        public async Task TestPerformRenewAll_RedeployIsDueOnTheNextPass()
        {
            // Arrange - the deployment failed a minute ago, on the item's first attempt
            await _itemStore.DeleteAll();
            await _itemStore.Update(CreateItemWithUndeployedCertificate("recent", "Recent", lastAttempt: DateTimeOffset.UtcNow.AddMinutes(-1)));

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
            Assert.HasCount(1, results, "The first few attempts are made without delay, so a brief problem recovers quickly");
            Assert.IsTrue(_requestsPerformed.Single().RedeployOnly);
        }

        [TestMethod, Description("Test PerformRenewAll holds a redeploy for the item's maintenance window")]
        public async Task TestPerformRenewAll_RedeployRespectsMaintenanceWindow()
        {
            // Arrange
            await _itemStore.DeleteAll();

            var item = CreateItemWithUndeployedCertificate("windowed", "Windowed");
            item.MaintenanceWindowId = "never-window";
            await _itemStore.Update(item);

            var prefsWithWindow = new RenewalPrefs
            {
                RenewalIntervalDays = 30,
                RenewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal,
                MaxRenewalRequests = 10,
                PerformParallelRenewals = false,
                SuppressSkippedItems = false,
                MaintenanceWindows = new List<MaintenanceWindow>
                {
                    new MaintenanceWindow
                    {
                        Id = "never-window",
                        Name = "Never Window",
                        IsEnabled = true,
                        Days = MaintenanceDays.None,
                        StartTime = TimeSpan.FromHours(0),
                        EndTime = TimeSpan.FromHours(0)
                    }
                }
            };

            // Act
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                _defaultSettings,
                prefsWithWindow,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Assert
            Assert.HasCount(0, results, "A deployment outside the maintenance window is what the window exists to prevent");

            var skipLog = _mockLog.LogEntries.FirstOrDefault(l => l.Contains("Windowed") && l.Contains("Limited to Maintenance Window"));
            Assert.IsNotNull(skipLog, "The skip should report the window the redeploy is waiting for");
        }

        [TestMethod, Description("Test PerformRenewAll renews rather than redeploys an item which is also due for renewal")]
        public async Task TestPerformRenewAll_DueItemWithFailedDeploymentIsRenewed()
        {
            // Arrange - renewed 35 days ago so renewal is due under the 30 day default, and its last deployment failed
            await _itemStore.DeleteAll();

            var item = CreateItemWithUndeployedCertificate("due", "Due");
            item.DateRenewed = DateTimeOffset.UtcNow.AddDays(-35);
            item.DateStart = DateTimeOffset.UtcNow.AddDays(-35);
            await _itemStore.Update(item);

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
            Assert.HasCount(1, results);
            Assert.IsFalse(_requestsPerformed.Single().RedeployOnly, "A certificate which is due is replaced, not redeployed");
        }

        [TestMethod, Description("Test PerformRenewAll previews a redeployment like any other request")]
        public async Task TestPerformRenewAll_PreviewsRedeployment()
        {
            // Arrange
            await _itemStore.DeleteAll();
            await _itemStore.Update(CreateItemWithUndeployedCertificate("preview", "Preview"));

            var previewSettings = new RenewalSettings { Mode = RenewalMode.Auto, IsPreviewMode = true };

            // Act
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                previewSettings,
                _defaultPrefs,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Assert
            Assert.HasCount(1, results, "A preview reports the redeployment which is due, so an operator can see it before it happens");

            var request = _requestsPerformed.Single();
            Assert.IsTrue(request.RedeployOnly);
            Assert.IsTrue(request.IsPreview, "The redeployment is previewed, not performed");
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
            Assert.IsEmpty(results, "Should return empty results when cancelled");
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

            Assert.HasCount(2, allResults, "All mode should process all certificates");

            // Test RenewalsWithErrors mode
            var errorsSettings = new RenewalSettings { Mode = RenewalMode.RenewalsWithErrors };
            var errorsResults = await RenewalManager.PerformRenewAll(_mockLog, _itemStore, errorsSettings, _defaultPrefs, ReportProgress, IsManagedCertificateRunning, PerformCertificateRequest, _cancellationTokenSource.Token);

            Assert.HasCount(1, errorsResults, "RenewalsWithErrors mode should only process certificates with errors");
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

        /// <summary>
        /// The requests the mock request handler received, with whether each was a redeploy of the certificate already
        /// held rather than a request for a new one, and whether it was a preview
        /// </summary>
        private readonly ConcurrentQueue<(string Id, bool RedeployOnly, bool IsPreview)> _requestsPerformed = new();

        private Task<CertificateRequestResult> PerformCertificateRequest(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress, bool isPreviewMode, string renewalReason, bool redeployOnly)
        {
            _requestsPerformed.Enqueue((managedCertificate.Id, redeployOnly, isPreviewMode));

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
            Assert.HasCount(1, results, $"NewItems mode should process the new certificate. Log: {logMessages}");
        }

        #region Maintenance Window Tests

        [TestMethod, Description("Test IsWithinMaintenanceWindow - no windows configured")]
        public void TestMaintenanceWindow_NoWindowsConfigured()
        {
            // Arrange
            var cert = CreateTestManagedCertificate("cert1", "Test1");
            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = null,
                DefaultMaintenanceWindowId = null
            };

            // Act
            var result = RenewalManager.IsWithinMaintenanceWindow(cert, prefs);

            // Assert
            Assert.IsTrue(result.IsWithinWindow, "Should allow renewal when no maintenance windows are configured");
            Assert.Contains("No maintenance windows configured", result.Reason, $"Reason should indicate no windows configured. Got: {result.Reason}");
        }

        [TestMethod, Description("Test IsWithinMaintenanceWindow - empty windows list")]
        public void TestMaintenanceWindow_EmptyWindowsList()
        {
            // Arrange
            var cert = CreateTestManagedCertificate("cert1", "Test1");
            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = new List<MaintenanceWindow>(),
                DefaultMaintenanceWindowId = null
            };

            // Act
            var result = RenewalManager.IsWithinMaintenanceWindow(cert, prefs);

            // Assert
            Assert.IsTrue(result.IsWithinWindow, "Should allow renewal when maintenance windows list is empty");
        }

        [TestMethod, Description("Test IsWithinMaintenanceWindow - no window assigned to item and no default")]
        public void TestMaintenanceWindow_NoWindowAssigned()
        {
            // Arrange
            var cert = CreateTestManagedCertificate("cert1", "Test1");
            cert.MaintenanceWindowId = null;

            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = new List<MaintenanceWindow>
                            {
                                new MaintenanceWindow { Id = "window1", Name = "Test Window", IsEnabled = true }
                            },
                DefaultMaintenanceWindowId = null
            };

            // Act
            var result = RenewalManager.IsWithinMaintenanceWindow(cert, prefs);

            // Assert
            Assert.IsTrue(result.IsWithinWindow, "Should allow renewal when no window is assigned and no default is set");
            Assert.Contains("No maintenance window assigned", result.Reason, $"Reason should indicate no window assigned. Got: {result.Reason}");
        }

        [TestMethod, Description("Test IsWithinMaintenanceWindow - window not found")]
        public void TestMaintenanceWindow_WindowNotFound()
        {
            // Arrange
            var cert = CreateTestManagedCertificate("cert1", "Test1");
            cert.MaintenanceWindowId = "deleted-window-id";

            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = new List<MaintenanceWindow>
                            {
                                new MaintenanceWindow { Id = "window1", Name = "Test Window", IsEnabled = true }
                            },
                DefaultMaintenanceWindowId = null
            };

            // Act
            var result = RenewalManager.IsWithinMaintenanceWindow(cert, prefs);

            // Assert
            Assert.IsTrue(result.IsWithinWindow, "Should allow renewal when configured window is not found (may have been deleted)");
            Assert.Contains("not found", result.Reason, $"Reason should indicate window not found. Got: {result.Reason}");
        }

        [TestMethod, Description("Test IsWithinMaintenanceWindow - window is disabled")]
        public void TestMaintenanceWindow_WindowDisabled()
        {
            // Arrange
            var cert = CreateTestManagedCertificate("cert1", "Test1");
            cert.MaintenanceWindowId = "window1";

            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = new List<MaintenanceWindow>
                            {
                                new MaintenanceWindow
                                {
                                    Id = "window1",
                                    Name = "Disabled Window",
                                    IsEnabled = false,
                                    Days = MaintenanceDays.Weekdays,
                                    StartTime = TimeSpan.FromHours(18),
                                    EndTime = TimeSpan.FromHours(21)
                                }
                            },
                DefaultMaintenanceWindowId = null
            };

            // Act
            var result = RenewalManager.IsWithinMaintenanceWindow(cert, prefs);

            // Assert
            Assert.IsTrue(result.IsWithinWindow, "Should allow renewal when maintenance window is disabled");
            Assert.Contains("disabled", result.Reason, $"Reason should indicate window is disabled. Got: {result.Reason}");
        }

        [TestMethod, Description("Test IsWithinMaintenanceWindow - uses default window when item has none")]
        public void TestMaintenanceWindow_UsesDefaultWindow()
        {
            // Arrange
            var cert = CreateTestManagedCertificate("cert1", "Test1");
            cert.MaintenanceWindowId = null;

            // Create a window that covers all times (24/7)
            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = new List<MaintenanceWindow>
                            {
                                new MaintenanceWindow
                                {
                                    Id = "default-window",
                                    Name = "Default Window",
                                    IsEnabled = true,
                                    Days = MaintenanceDays.All,
                                    StartTime = TimeSpan.FromHours(0),
                                    EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59))
                                }
                            },
                DefaultMaintenanceWindowId = "default-window"
            };

            // Act
            var result = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, DateTimeOffset.Now);

            // Assert
            Assert.IsTrue(result.IsWithinWindow, "Should use default window when item has no specific window");
            Assert.Contains("Default Window", result.Reason, $"Reason should reference the default window. Got: {result.Reason}");
        }

        [TestMethod, Description("Test IsWithinMaintenanceWindow - item window overrides default")]
        public void TestMaintenanceWindow_ItemWindowOverridesDefault()
        {
            // Arrange
            var cert = CreateTestManagedCertificate("cert1", "Test1");
            cert.MaintenanceWindowId = "item-window";

            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = new List<MaintenanceWindow>
                            {
                                new MaintenanceWindow
                                {
                                    Id = "default-window",
                                    Name = "Default Window",
                                    IsEnabled = true,
                                    Days = MaintenanceDays.None, // Never allows renewal
                                    StartTime = TimeSpan.FromHours(0),
                                    EndTime = TimeSpan.FromHours(0)
                                },
                                new MaintenanceWindow
                                {
                                    Id = "item-window",
                                    Name = "Item Specific Window",
                                    IsEnabled = true,
                                    Days = MaintenanceDays.All, // Always allows renewal
                                    StartTime = TimeSpan.FromHours(0),
                                    EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59))
                                }
                            },
                DefaultMaintenanceWindowId = "default-window"
            };

            // Act
            var result = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, DateTimeOffset.Now);

            // Assert
            Assert.IsTrue(result.IsWithinWindow, "Item-specific window should override default window");
            Assert.Contains("Item Specific Window", result.Reason, $"Reason should reference item window, not default. Got: {result.Reason}");
        }

        [TestMethod, Description("Test IsWithinMaintenanceWindow - weekday window on a weekday")]
        public void TestMaintenanceWindow_WeekdayWindow_OnWeekday()
        {
            // Arrange
            var cert = CreateTestManagedCertificate("cert1", "Test1");
            cert.MaintenanceWindowId = "weekday-window";

            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = new List<MaintenanceWindow>
                            {
                                new MaintenanceWindow
                                {
                                    Id = "weekday-window",
                                    Name = "Weekday Evening",
                                    IsEnabled = true,
                                    Days = MaintenanceDays.Weekdays,
                                    StartTime = TimeSpan.FromHours(18),
                                    EndTime = TimeSpan.FromHours(21)
                                }
                            }
            };

            // Test on a Wednesday at 19:00
            var testTime = new DateTimeOffset(2024, 1, 10, 19, 0, 0, TimeSpan.Zero); // Wednesday

            // Act
            var result = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, testTime);

            // Assert
            Assert.IsTrue(result.IsWithinWindow, "Should allow renewal on weekday within time window");
        }

        [TestMethod, Description("Test IsWithinMaintenanceWindow - weekday window on a weekend")]
        public void TestMaintenanceWindow_WeekdayWindow_OnWeekend()
        {
            // Arrange
            var cert = CreateTestManagedCertificate("cert1", "Test1");
            cert.MaintenanceWindowId = "weekday-window";

            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = new List<MaintenanceWindow>
                            {
                                new MaintenanceWindow
                                {
                                    Id = "weekday-window",
                                    Name = "Weekday Evening",
                                    IsEnabled = true,
                                    Days = MaintenanceDays.Weekdays,
                                    StartTime = TimeSpan.FromHours(18),
                                    EndTime = TimeSpan.FromHours(21)
                                }
                            }
            };

            // Test on a Saturday at 19:00
            var testTime = new DateTimeOffset(2024, 1, 13, 19, 0, 0, TimeSpan.Zero); // Saturday

            // Act
            var result = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, testTime);

            // Assert
            Assert.IsFalse(result.IsWithinWindow, "Should not allow renewal on weekend when window is weekdays only");
            Assert.Contains("Limited to Maintenance Window", result.Reason, $"Reason should indicate the renewal is limited to the maintenance window. Got: {result.Reason}");
        }

        [TestMethod, Description("Test IsWithinMaintenanceWindow - time outside window hours")]
        public void TestMaintenanceWindow_OutsideWindowHours()
        {
            // Arrange
            var cert = CreateTestManagedCertificate("cert1", "Test1");
            cert.MaintenanceWindowId = "evening-window";

            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = new List<MaintenanceWindow>
                            {
                                new MaintenanceWindow
                                {
                                    Id = "evening-window",
                                    Name = "Evening Window",
                                    IsEnabled = true,
                                    Days = MaintenanceDays.All,
                                    StartTime = TimeSpan.FromHours(18),
                                    EndTime = TimeSpan.FromHours(21)
                                }
                            }
            };

            // Test at 10:00 AM (outside 18:00-21:00 window)
            var testTime = new DateTimeOffset(2024, 1, 10, 10, 0, 0, TimeSpan.Zero);

            // Act
            var result = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, testTime);

            // Assert
            Assert.IsFalse(result.IsWithinWindow, "Should not allow renewal outside window hours");
            Assert.Contains("Next window:", result.Reason, $"Reason should include next occurrence. Got: {result.Reason}");
        }

        [TestMethod, Description("Test IsWithinMaintenanceWindow - overnight window")]
        public void TestMaintenanceWindow_OvernightWindow()
        {
            // Arrange
            var cert = CreateTestManagedCertificate("cert1", "Test1");
            cert.MaintenanceWindowId = "overnight-window";

            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = new List<MaintenanceWindow>
                            {
                                new MaintenanceWindow
                                {
                                    Id = "overnight-window",
                                    Name = "Overnight Window",
                                    IsEnabled = true,
                                    Days = MaintenanceDays.All,
                                    StartTime = TimeSpan.FromHours(22), // 10 PM
                                    EndTime = TimeSpan.FromHours(6)     // 6 AM (next day)
                                }
                            }
            };

            // Test at 11 PM (within overnight window)
            var testTime1 = new DateTimeOffset(2024, 1, 10, 23, 0, 0, TimeSpan.Zero);
            var result1 = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, testTime1);
            Assert.IsTrue(result1.IsWithinWindow, "Should allow renewal at 11 PM in overnight window");

            // Test at 3 AM (within overnight window)
            var testTime2 = new DateTimeOffset(2024, 1, 11, 3, 0, 0, TimeSpan.Zero);
            var result2 = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, testTime2);
            Assert.IsTrue(result2.IsWithinWindow, "Should allow renewal at 3 AM in overnight window");

            // Test at 10 AM (outside overnight window)
            var testTime3 = new DateTimeOffset(2024, 1, 11, 10, 0, 0, TimeSpan.Zero);
            var result3 = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, testTime3);
            Assert.IsFalse(result3.IsWithinWindow, "Should not allow renewal at 10 AM outside overnight window");
        }

        [TestMethod, Description("Test IsWithinMaintenanceWindow - weekend only window")]
        public void TestMaintenanceWindow_WeekendOnly()
        {
            // Arrange
            var cert = CreateTestManagedCertificate("cert1", "Test1");
            cert.MaintenanceWindowId = "weekend-window";

            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = new List<MaintenanceWindow>
                            {
                                new MaintenanceWindow
                                {
                                    Id = "weekend-window",
                                    Name = "Weekend Window",
                                    IsEnabled = true,
                                    Days = MaintenanceDays.Weekends,
                                    StartTime = TimeSpan.FromHours(0),
                                    EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59))
                                }
                            }
            };

            // Test on Saturday
            var saturday = new DateTimeOffset(2024, 1, 13, 12, 0, 0, TimeSpan.Zero);
            var resultSaturday = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, saturday);
            Assert.IsTrue(resultSaturday.IsWithinWindow, "Should allow renewal on Saturday");

            // Test on Sunday
            var sunday = new DateTimeOffset(2024, 1, 14, 12, 0, 0, TimeSpan.Zero);
            var resultSunday = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, sunday);
            Assert.IsTrue(resultSunday.IsWithinWindow, "Should allow renewal on Sunday");

            // Test on Monday
            var monday = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);
            var resultMonday = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, monday);
            Assert.IsFalse(resultMonday.IsWithinWindow, "Should not allow renewal on Monday");
        }

        [TestMethod, Description("Test IsWithinMaintenanceWindow - specific days (Mon, Wed, Fri)")]
        public void TestMaintenanceWindow_SpecificDays()
        {
            // Arrange
            var cert = CreateTestManagedCertificate("cert1", "Test1");
            cert.MaintenanceWindowId = "mwf-window";

            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = new List<MaintenanceWindow>
                            {
                                new MaintenanceWindow
                                {
                                    Id = "mwf-window",
                                    Name = "Mon/Wed/Fri Window",
                                    IsEnabled = true,
                                    Days = MaintenanceDays.Monday | MaintenanceDays.Wednesday | MaintenanceDays.Friday,
                                    StartTime = TimeSpan.FromHours(9),
                                    EndTime = TimeSpan.FromHours(17)
                                }
                            }
            };

            // Monday at noon - should be allowed
            var monday = new DateTimeOffset(2024, 1, 8, 12, 0, 0, TimeSpan.Zero);
            var resultMonday = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, monday);
            Assert.IsTrue(resultMonday.IsWithinWindow, "Should allow renewal on Monday");

            // Tuesday at noon - should not be allowed
            var tuesday = new DateTimeOffset(2024, 1, 9, 12, 0, 0, TimeSpan.Zero);
            var resultTuesday = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, tuesday);
            Assert.IsFalse(resultTuesday.IsWithinWindow, "Should not allow renewal on Tuesday");

            // Wednesday at noon - should be allowed
            var wednesday = new DateTimeOffset(2024, 1, 10, 12, 0, 0, TimeSpan.Zero);
            var resultWednesday = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, wednesday);
            Assert.IsTrue(resultWednesday.IsWithinWindow, "Should allow renewal on Wednesday");
        }

        [TestMethod, Description("Test IsWithinMaintenanceWindow - boundary times")]
        public void TestMaintenanceWindow_BoundaryTimes()
        {
            // Arrange
            var cert = CreateTestManagedCertificate("cert1", "Test1");
            cert.MaintenanceWindowId = "precise-window";

            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = new List<MaintenanceWindow>
                            {
                                new MaintenanceWindow
                                {
                                    Id = "precise-window",
                                    Name = "Precise Window",
                                    IsEnabled = true,
                                    Days = MaintenanceDays.All,
                                    StartTime = TimeSpan.FromHours(14).Add(TimeSpan.FromMinutes(30)), // 14:30
                                    EndTime = TimeSpan.FromHours(16).Add(TimeSpan.FromMinutes(45))   // 16:45
                                }
                            }
            };

            // At exact start time
            var atStart = new DateTimeOffset(2024, 1, 10, 14, 30, 0, TimeSpan.Zero);
            var resultStart = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, atStart);
            Assert.IsTrue(resultStart.IsWithinWindow, "Should allow renewal at exact start time");

            // At exact end time
            var atEnd = new DateTimeOffset(2024, 1, 10, 16, 45, 0, TimeSpan.Zero);
            var resultEnd = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, atEnd);
            Assert.IsTrue(resultEnd.IsWithinWindow, "Should allow renewal at exact end time");

            // One minute before start
            var beforeStart = new DateTimeOffset(2024, 1, 10, 14, 29, 0, TimeSpan.Zero);
            var resultBeforeStart = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, beforeStart);
            Assert.IsFalse(resultBeforeStart.IsWithinWindow, "Should not allow renewal one minute before start");

            // One minute after end
            var afterEnd = new DateTimeOffset(2024, 1, 10, 16, 46, 0, TimeSpan.Zero);
            var resultAfterEnd = RenewalManager.IsWithinMaintenanceWindow(cert, prefs, afterEnd);
            Assert.IsFalse(resultAfterEnd.IsWithinWindow, "Should not allow renewal one minute after end");
        }

        [TestMethod, Description("Test PerformRenewAll respects maintenance windows in Auto mode")]
        public async Task TestPerformRenewAll_RespectsMaintenanceWindow()
        {
            // Arrange
            await _itemStore.DeleteAll();

            var cert1 = CreateTestManagedCertificate("cert1", "InWindow", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35));
            cert1.MaintenanceWindowId = "all-day-window";

            var cert2 = CreateTestManagedCertificate("cert2", "OutsideWindow", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35));
            cert2.MaintenanceWindowId = "never-window";

            await _itemStore.Update(cert1);
            await _itemStore.Update(cert2);

            var prefsWithWindows = new RenewalPrefs
            {
                RenewalIntervalDays = 30,
                RenewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal,
                MaxRenewalRequests = 10,
                PerformParallelRenewals = false,
                SuppressSkippedItems = false,
                MaintenanceWindows = new List<MaintenanceWindow>
                            {
                                new MaintenanceWindow
                                {
                                    Id = "all-day-window",
                                    Name = "All Day Window",
                                    IsEnabled = true,
                                    Days = MaintenanceDays.All,
                                    StartTime = TimeSpan.FromHours(0),
                                    EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59))
                                },
                                new MaintenanceWindow
                                {
                                    Id = "never-window",
                                    Name = "Never Window",
                                    IsEnabled = true,
                                    Days = MaintenanceDays.None, // No days enabled
                                    StartTime = TimeSpan.FromHours(0),
                                    EndTime = TimeSpan.FromHours(0)
                                }
                            }
            };

            var autoSettings = new RenewalSettings { Mode = RenewalMode.Auto };

            // Act
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                autoSettings,
                prefsWithWindows,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Assert
            Assert.HasCount(1, results, "Should only renew certificate within maintenance window");
            Assert.AreEqual("cert1", results[0].ManagedItem.Id, "cert1 (in window) should be renewed");

            // Check logs for skipped item
            var skipLog = _mockLog.LogEntries.FirstOrDefault(l => l.Contains("OutsideWindow") && l.Contains("Limited to Maintenance Window"));
            Assert.IsNotNull(skipLog, "Should log that cert2 was skipped due to maintenance window");
        }

        [TestMethod, Description("Test PerformRenewAll ignores maintenance windows in All mode")]
        public async Task TestPerformRenewAll_IgnoresMaintenanceWindowInAllMode()
        {
            // Arrange
            await _itemStore.DeleteAll();

            var cert1 = CreateTestManagedCertificate("cert1", "Test1", dateRenewed: DateTimeOffset.UtcNow.AddDays(-35));
            cert1.MaintenanceWindowId = "never-window";

            await _itemStore.Update(cert1);

            var prefsWithWindows = new RenewalPrefs
            {
                RenewalIntervalDays = 30,
                RenewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal,
                MaxRenewalRequests = 10,
                PerformParallelRenewals = false,
                MaintenanceWindows = new List<MaintenanceWindow>
                            {
                                new MaintenanceWindow
                                {
                                    Id = "never-window",
                                    Name = "Never Window",
                                    IsEnabled = true,
                                    Days = MaintenanceDays.None, // No days enabled
                                    StartTime = TimeSpan.FromHours(0),
                                    EndTime = TimeSpan.FromHours(0)
                                }
                            }
            };

            var allModeSettings = new RenewalSettings { Mode = RenewalMode.All };

            // Act
            var results = await RenewalManager.PerformRenewAll(
                _mockLog,
                _itemStore,
                allModeSettings,
                prefsWithWindows,
                ReportProgress,
                IsManagedCertificateRunning,
                PerformCertificateRequest,
                _cancellationTokenSource.Token
            );

            // Assert - In "All" mode, maintenance windows should be ignored
            Assert.HasCount(1, results, "All mode should ignore maintenance windows and process certificate");
        }

        #endregion
    }
}
