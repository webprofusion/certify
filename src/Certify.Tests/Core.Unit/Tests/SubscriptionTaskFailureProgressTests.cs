using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for the request progress reported when the post-request deployment tasks of a subscription request fail.
    /// A subscription request reports the outcome of its fetch and deployment before its deployment tasks are run, so
    /// unless the task run reports the failure itself the request progress the UI shows keeps saying the request
    /// succeeded, and the failure is only visible by going back to the item afterwards
    /// </summary>
    [TestClass]
    public class SubscriptionTaskFailureProgressTests
    {
        /// <summary>
        /// Records progress reported to the caller which started the request. Reports synchronously, unlike
        /// <see cref="Progress{T}"/>, so a test can assert on what was reported as soon as the call returns
        /// </summary>
        private class RecordingProgress : IProgress<RequestProgressState>
        {
            public List<RequestProgressState> Reports { get; } = new();

            public void Report(RequestProgressState value) => Reports.Add(value);
        }

        private class StubItemStore : IManagedItemStore
        {
            private readonly List<ManagedCertificate> _items;

            public StubItemStore(params ManagedCertificate[] items) => _items = items.ToList();

            public Task<ManagedCertificate> Update(ManagedCertificate managedCertificate) => Task.FromResult(managedCertificate);
            public Task<ManagedCertificate> GetById(string siteId) => Task.FromResult(_items.FirstOrDefault(i => i.Id == siteId));
            public Task<List<ManagedCertificate>> Find(ManagedCertificateFilter filter) => Task.FromResult(_items.ToList());
            public bool Init(string connectionString, ILog log, string instanceId = null) => true;
            public Task<bool> IsInitialised() => Task.FromResult(true);
            public Task DeleteAll() => Task.CompletedTask;
            public Task StoreAll(IEnumerable<ManagedCertificate> list) => Task.CompletedTask;
            public Task Delete(ManagedCertificate site) => Task.CompletedTask;
            public Task DeleteByName(string nameStartsWith) => Task.CompletedTask;
            public Task<long> CountAll(ManagedCertificateFilter filter) => Task.FromResult((long)_items.Count);
            public Task PerformMaintenance() => Task.CompletedTask;
        }

        private ILog _log;

        [TestInitialize]
        public void Setup() => _log = new Loggy(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<SubscriptionTaskFailureProgressTests>());

        private static void SetPrivateField(CertifyManager manager, string fieldName, object value)
        {
            var field = typeof(CertifyManager).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{fieldName} should be available for testing");
            field.SetValue(manager, value);
        }

        /// <summary>
        /// A manager with just enough of its dependencies to run a deployment task list and store the outcome.
        /// MockTask is resolved from the Certify.Core assembly itself, so no provider plugins are needed
        /// </summary>
        private static CertifyManager CreateManager(IManagedItemStore itemStore)
        {
            var manager = new CertifyManager();

            SetPrivateField(manager, "_itemManager", itemStore);
            SetPrivateField(manager, "_pluginManager", new PluginManager());
            SetPrivateField(manager, "_serverConfig", new Certify.Shared.ServiceConfig());

            return manager;
        }

        /// <summary>
        /// A mock post-request task which fails when it is run
        /// </summary>
        private static DeploymentTaskConfig GetFailingTask()
        {
            return new DeploymentTaskConfig
            {
                Id = "task-1",
                TaskName = "Deploy to server",
                TaskTypeId = Providers.DeploymentTasks.Core.MockTask.Definition.Id,
                TaskTrigger = TaskTriggerType.ANY_STATUS,
                IsFatalOnError = true,
                Parameters = new List<ProviderParameterSetting>
                {
                    new ProviderParameterSetting("message", "Deployment failed"),
                    new ProviderParameterSetting("throw", "true")
                }
            };
        }

        private static ManagedCertificate CreateSubscriptionWithFailingTask()
        {
            var now = DateTimeOffset.UtcNow;

            return new ManagedCertificate
            {
                Id = "subscriber-item",
                Name = "Subscriber Item",
                IncludeInAutoRenew = true,
                ItemType = ManagedCertificateType.SSL_ExternalSubscription,
                DateStart = now.AddMinutes(-1),
                DateRenewed = now.AddMinutes(-1),
                DateExpiry = now.AddDays(6),
                CertificateThumbprintHash = "ABC123",
                LastRenewalStatus = RequestState.Success,
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "External certificate pulled from Management Hub." },
                LastBindingDeployment = new RequestStageStatus { Status = RequestState.Success, Message = "External certificate deployment completed successfully." },
                PostRequestTasks = new ObservableCollection<DeploymentTaskConfig>(new[] { GetFailingTask() }),
                // failure notifications would send the recorded failure to the reporting dashboard, which is not
                // part of what is under test here
                RequestConfig = new CertRequestConfig { PrimaryDomain = "sub.example.com", EnableFailureNotifications = false },
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = ExternalCertificateRetrievalModes.Pull,
                    ExternalReference = "instance-a/cert-1"
                }
            };
        }

        /// <summary>
        /// The result a subscription request hands to its post-request task run once the certificate was fetched and
        /// deployed. Its outcome has already been reported as request progress by this point
        /// </summary>
        private static CertificateRequestResult GetSuccessfulPrimaryRequestResult(ManagedCertificate item)
        {
            return new CertificateRequestResult(item, isSuccess: true, "External certificate deployment completed successfully.")
            {
                PrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "External certificate pulled from Management Hub." }
            };
        }

        private async Task<bool> PerformPostRequestTasks(CertifyManager manager, ManagedCertificate item, CertificateRequestResult result, bool isFinalRequestStage, IProgress<RequestProgressState> progress)
        {
            var method = typeof(CertifyManager).GetMethod("PerformPostRequestTasksIfApplicable", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "PerformPostRequestTasksIfApplicable should be available for testing");

            return await (Task<bool>)method.Invoke(manager, new object[] { _log, item, result, false, item.RenewalFailureCount, isFinalRequestStage, progress });
        }

        [TestMethod, Description("A failed deployment task of a subscription request is reported as failed request progress")]
        public async Task FailedTaskOfSubscriptionRequestIsReportedAsProgress()
        {
            var item = CreateSubscriptionWithFailingTask();
            var manager = CreateManager(new StubItemStore(item));
            var progress = new RecordingProgress();

            var result = GetSuccessfulPrimaryRequestResult(item);

            var tasksRan = await PerformPostRequestTasks(manager, item, result, isFinalRequestStage: true, progress);

            Assert.IsTrue(tasksRan, "The task list should have been evaluated");
            Assert.IsFalse(result.IsSuccess, "A failed deployment task makes the request a failed one");

            Assert.HasCount(1, progress.Reports, "The task failure should be reported as request progress, the request progress is otherwise left saying the request succeeded");

            var report = progress.Reports[0];

            Assert.AreEqual(RequestState.Error, report.CurrentState, "The reported state should match the failure recorded against the item");
            Assert.AreEqual(result.Message, report.Message, "The reported message should describe the deployment task which failed");
            Assert.AreEqual(item.Id, report.ManagedCertificate?.Id);
        }

        [TestMethod, Description("The failure reported for a subscription request is the one recorded against the item")]
        public async Task ReportedFailureMatchesTheRecordedItemStatus()
        {
            var item = CreateSubscriptionWithFailingTask();
            var manager = CreateManager(new StubItemStore(item));
            var progress = new RecordingProgress();

            await PerformPostRequestTasks(manager, item, GetSuccessfulPrimaryRequestResult(item), isFinalRequestStage: true, progress);

            Assert.AreEqual(RequestState.Error, item.LastRenewalStatus, "The item itself should record the failed request");
            Assert.AreEqual(item.RenewalFailureMessage, progress.Reports.Single().Message, "The request progress and the item should describe the same failure");
        }

        [TestMethod, Description("A successful task run of a subscription request reports no further progress")]
        public async Task SuccessfulTasksOfSubscriptionRequestReportNoFurtherProgress()
        {
            var item = CreateSubscriptionWithFailingTask();
            item.PostRequestTasks[0].Parameters.Single(p => p.Key == "throw").Value = "false";

            var manager = CreateManager(new StubItemStore(item));
            var progress = new RecordingProgress();

            var result = GetSuccessfulPrimaryRequestResult(item);

            var tasksRan = await PerformPostRequestTasks(manager, item, result, isFinalRequestStage: true, progress);

            Assert.IsTrue(tasksRan, "The task list should have been evaluated");
            Assert.IsTrue(result.IsSuccess, "The request succeeded and no task failed");

            Assert.IsEmpty(progress.Reports, "The successful outcome was already reported by the subscription request itself");
        }

        [TestMethod, Description("A failed deployment task of a standard request is left for the caller to report")]
        public async Task FailedTaskOfStandardRequestIsNotReportedHere()
        {
            var item = CreateSubscriptionWithFailingTask();
            item.ItemType = ManagedCertificateType.SSL_ACME;
            item.ExternalSource = null;

            var manager = CreateManager(new StubItemStore(item));
            var progress = new RecordingProgress();

            var result = GetSuccessfulPrimaryRequestResult(item);

            var tasksRan = await PerformPostRequestTasks(manager, item, result, isFinalRequestStage: false, progress);

            Assert.IsTrue(tasksRan, "The task list should have been evaluated");
            Assert.IsFalse(result.IsSuccess, "A failed deployment task makes the request a failed one");

            Assert.IsEmpty(progress.Reports, "A standard request resolves its final status after the tasks have run and reports it there, reporting here would report the request outcome twice");
        }
    }
}
