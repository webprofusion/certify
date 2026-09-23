using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Management;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Reporting;
using Certify.Server.Hub.Api.Services.Activity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// Request progress was a free-text message which overwrote the last one, and nothing recorded what happened. Each
    /// request is now a run with fixed stages and a message history, and the hub keeps an activity history from which
    /// the overview screens are built.
    /// </summary>
    [TestClass]
    public class RequestRunTrackerTests
    {
        private static ManagedCertificate Item(string id = "item-1", string name = "shop.example.org") => new ManagedCertificate { Id = id, Name = name };

        private static RequestProgressState Progress(RequestState state, string message, ManagedCertificate item) => new RequestProgressState(state, message, item);

        [TestMethod]
        public void QueuedRun_KeepsItsPassAndReason_WhenItStarts()
        {
            var tracker = new RequestRunTracker();
            var item = Item();

            tracker.Queue(item.Id, item.Name, RequestTrigger.Schedule, "Renewal window opened", "batch-1");

            var queued = Progress(RequestState.Queued, "Queued for renewal", item);
            tracker.Apply(queued);

            Assert.IsNotNull(queued.RunId);
            Assert.AreEqual(RequestStage.Queued, queued.Stage);
            Assert.AreEqual("batch-1", queued.BatchId);

            var run = tracker.Begin(item, RequestTrigger.Unknown, reason: null, isPreview: false, isRedeploy: false);

            Assert.AreEqual(queued.RunId, run.RunId, "the run started is the run which was queued");
            Assert.AreEqual(RequestTrigger.Schedule, run.Trigger);
            Assert.AreEqual("Renewal window opened", run.TriggerReason);
            Assert.AreEqual(RequestState.Success, run.Stages.Single(s => s.Stage == RequestStage.Queued).Status);
        }

        [TestMethod]
        public void StagesAreTimedAndMessagesKept_AndTheRunStopsAtTheStageWhichFailed()
        {
            var now = new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);
            var tracker = new RequestRunTracker(() => now);
            var item = Item();

            tracker.Begin(item, RequestTrigger.User, null, false, false);

            tracker.SetStage(item.Id, RequestStage.Order);
            tracker.Apply(Progress(RequestState.Running, "Beginning order", item));

            now = now.AddSeconds(2);
            tracker.SetStage(item.Id, RequestStage.Challenges);

            now = now.AddSeconds(4);
            tracker.SetStage(item.Id, RequestStage.Validation);

            var failure = Progress(RequestState.Error, "DNS TXT record not found", item);
            tracker.Apply(failure);

            Assert.AreEqual(RequestStage.Validation, failure.Stage);
            Assert.AreEqual(3, failure.Stages!.Count, "a client joining part way through sees the stages so far");

            // a failed request can still run deployment tasks configured to run on failure
            tracker.SetStage(item.Id, RequestStage.Deployment);

            now = now.AddSeconds(1);
            var run = tracker.Complete(item.Id, RequestState.Error, "DNS TXT record not found");

            Assert.IsNotNull(run);
            Assert.AreEqual(RequestStage.Validation, run.StoppedAtStage);
            Assert.AreEqual(RequestState.Success, run.Stages.Single(s => s.Stage == RequestStage.Order).Status);
            Assert.AreEqual(TimeSpan.FromSeconds(2), run.Stages.Single(s => s.Stage == RequestStage.Order).Completed - run.Stages.Single(s => s.Stage == RequestStage.Order).Started);
            Assert.AreEqual(RequestState.Error, run.Stages.Single(s => s.Stage == RequestStage.Validation).Status);
            Assert.IsTrue(run.Messages.Any(m => m.Message == "Beginning order"));
            Assert.IsNull(tracker.GetRun(item.Id), "a finished run is no longer tracked");
        }

        [TestMethod]
        public void RepeatedMessages_AreRecordedOnce()
        {
            var tracker = new RequestRunTracker();
            var item = Item();

            tracker.Begin(item, RequestTrigger.User, null, false, false);

            tracker.Apply(Progress(RequestState.Running, "Validating", item));
            tracker.Apply(Progress(RequestState.Running, "Validating", item));
            tracker.Apply(Progress(RequestState.Running, "Validated", item));

            var run = tracker.Complete(item.Id, RequestState.Success, "Done");

            CollectionAssert.AreEqual(new[] { "Validating", "Validated", "Done" }, run!.Messages.Select(m => m.Message).ToArray());
        }

        [TestMethod]
        public void LongRuns_KeepTheirFirstAndLatestMessages()
        {
            var tracker = new RequestRunTracker();
            var item = Item();

            tracker.Begin(item, RequestTrigger.User, null, false, false);

            for (var i = 0; i < RequestRunTracker.MaxMessagesPerRun + 50; i++)
            {
                tracker.Apply(Progress(RequestState.Running, $"Message {i}", item));
            }

            var run = tracker.Complete(item.Id, RequestState.Success, "Finished");

            Assert.AreEqual(RequestRunTracker.MaxMessagesPerRun, run!.Messages.Count);
            Assert.AreEqual("Message 0", run.Messages.First().Message);
            Assert.AreEqual("Finished", run.Messages.Last().Message);
        }

        [TestMethod]
        public void PausedRun_KeepsWhatThePersonMustDo()
        {
            var tracker = new RequestRunTracker();
            var item = Item();

            tracker.Begin(item, RequestTrigger.Schedule, null, false, false);
            tracker.SetStage(item.Id, RequestStage.Challenges);

            tracker.Apply(new RequestProgressState(RequestState.Paused, "Create a TXT record", item)
            {
                UserActions = [new RequestUserAction { RecordName = "_acme-challenge.shop.example.org", RecordType = "TXT", RecordValue = "abc" }]
            });

            var run = tracker.Complete(item.Id, RequestState.Paused, "Create a TXT record");

            Assert.AreEqual(RequestStage.Challenges, run!.StoppedAtStage);
            Assert.AreEqual("_acme-challenge.shop.example.org", run.UserActions!.Single().RecordName);

            var final = new RequestProgressState(RequestState.Paused, "Create a TXT record", item);
            RequestRunTracker.StampFinal(final, run);

            Assert.IsTrue(final.IsFinal);
            Assert.AreEqual(run.RunId, final.RunId);
            Assert.IsNotNull(final.UserActions);
        }

        [TestMethod]
        public void MessageForAnItemWithNoRun_IsLeftAsItIs()
        {
            var tracker = new RequestRunTracker();
            var state = Progress(RequestState.Running, "Testing configuration", Item());

            tracker.Apply(state);

            Assert.IsNull(state.RunId);
            Assert.IsNull(state.Stages);
        }

        [TestMethod]
        public void DiscardQueued_LeavesARunWhichHasStarted()
        {
            var tracker = new RequestRunTracker();
            var item = Item();

            tracker.Begin(item, RequestTrigger.User, null, false, false);
            tracker.DiscardQueued(item.Id);

            Assert.IsNotNull(tracker.GetRun(item.Id));

            var other = Item("item-2");
            tracker.Queue(other.Id, other.Name, RequestTrigger.Schedule, null, "batch");
            tracker.DiscardQueued(other.Id);

            Assert.IsNull(tracker.GetRun(other.Id));
        }

        [TestMethod]
        public void CompletedRunEvent_DescribesTheOutcome()
        {
            var item = Item();

            var failed = new RequestRun { RunId = "r1", ItemTitle = item.Name, Outcome = RequestState.Error, StoppedAtStage = RequestStage.Validation, FailureCount = 4, Started = DateTimeOffset.UtcNow };
            Assert.AreEqual("shop.example.org failed at validation (4 failures in a row)", CertifyManager.BuildRequestCompletedEvent(failed, item, hadCertificateBefore: true).Title);

            var deploymentFailed = new RequestRun { RunId = "r2", ItemTitle = item.Name, Outcome = RequestState.Error, CertificateIssued = true, StoppedAtStage = RequestStage.Deployment, Started = DateTimeOffset.UtcNow };
            Assert.AreEqual("shop.example.org was renewed, but deployment failed", CertifyManager.BuildRequestCompletedEvent(deploymentFailed, item, true).Title);

            var renewed = new RequestRun { RunId = "r3", ItemTitle = item.Name, Outcome = RequestState.Success, CertificateExpiry = new DateTimeOffset(2026, 12, 22, 0, 0, 0, TimeSpan.Zero), Started = DateTimeOffset.UtcNow };
            var renewedEvent = CertifyManager.BuildRequestCompletedEvent(renewed, item, true);
            Assert.AreEqual("Renewed shop.example.org", renewedEvent.Title);
            Assert.AreEqual("Valid until 2026-12-22", renewedEvent.Detail);

            Assert.AreEqual("Certificate issued for shop.example.org", CertifyManager.BuildRequestCompletedEvent(renewed, item, hadCertificateBefore: false).Title);
        }

        [TestMethod]
        public void RequiredUserActions_GiveTheDnsRecord_UnlessDelegated()
        {
            var item = new ManagedCertificate
            {
                Id = "item-1",
                Name = "shop.example.com",
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "shop.example.com",
                    SubjectAlternativeNames = ["shop.example.com"],
                    Challenges = [new CertRequestChallengeConfig { ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS, ChallengeProvider = "DNS01.Manual" }]
                }
            };

            var authorizations = new List<PendingAuthorization>
            {
                new PendingAuthorization
                {
                    Identifier = new CertIdentifierItem { IdentifierType = CertIdentifierType.Dns, Value = "shop.example.com" },
                    AttemptedChallenge = new AuthorizationChallengeItem
                    {
                        ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                        Key = "_acme-challenge.shop.example.com",
                        Value = "txt-value",
                        IsAwaitingUser = true,
                        ChallengeResultMsg = "Create the record"
                    }
                }
            };

            var action = CertifyManager.GetRequiredUserActions(item, authorizations).Single();

            Assert.AreEqual("_acme-challenge.shop.example.com", action.RecordName);
            Assert.AreEqual("TXT", action.RecordType);
            Assert.AreEqual("txt-value", action.RecordValue);

            item.RequestConfig.Challenges[0].ChallengeDelegationRule = "*.example.com:*.acme.example.net";

            var delegated = CertifyManager.GetRequiredUserActions(item, authorizations).Single();

            Assert.IsNull(delegated.RecordName, "the delegated record is somewhere else, which only the instructions describe");
            Assert.AreEqual("Create the record", delegated.Instructions);
        }

        [TestMethod]
        public void NotificationsWhileDisconnected_AreHeldUpToALimit()
        {
            var client = new ManagementServerClient("https://localhost:1/api/internal/managementhub", new ManagedInstanceInfo { InstanceId = "instance-1" });

            for (var i = 0; i < 600; i++)
            {
                client.QueueNotificationToManagementHub(ManagementHubCommands.NotificationActivityEvent, new ActivityEvent { Id = i.ToString() });
            }

            Assert.AreEqual(500, client.QueuedNotificationCount);
        }
    }

    [TestClass]
    public class ActivityStoreTests
    {
        private string _dbPath = default!;
        private ActivityStore _store = default!;

        [TestInitialize]
        public async Task Init()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"certify-activity-{Guid.NewGuid():N}.db");
            _store = new ActivityStore(_dbPath);
            await _store.InitAsync();
        }

        [TestCleanup]
        public void Cleanup()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                }
            }
        }

        private static ActivityEvent Event(string id, DateTimeOffset at, string? itemId = null, ActivityCategory category = ActivityCategory.Request, RequestState status = RequestState.Success, string title = "Event")
            => new ActivityEvent
            {
                Id = id,
                Timestamp = at,
                InstanceId = "instance-1",
                ManagedItemId = itemId,
                Category = category,
                EventType = ActivityEventTypes.RequestCompleted,
                Status = status,
                Title = title
            };

        [TestMethod]
        public async Task Events_AreReturnedNewestFirst_AndRecordedOnlyOnce()
        {
            var start = DateTimeOffset.UtcNow.AddHours(-1);

            Assert.IsTrue(await _store.AddEventAsync(Event("a", start, "item-1")));
            Assert.IsTrue(await _store.AddEventAsync(Event("b", start.AddMinutes(1), "item-2")));
            Assert.IsFalse(await _store.AddEventAsync(Event("a", start, "item-1")), "an event sent again after a reconnect is not duplicated");

            var (results, total) = await _store.QueryEventsAsync(new ActivityQuery());

            Assert.AreEqual(2, total);
            CollectionAssert.AreEqual(new[] { "b", "a" }, results.Select(r => r.Id).ToArray());
        }

        [TestMethod]
        public async Task QueryFilter_DecidesVisibility_AndPagingCountsOnlyVisibleEvents()
        {
            var start = DateTimeOffset.UtcNow.AddHours(-1);

            for (var i = 0; i < 10; i++)
            {
                await _store.AddEventAsync(Event($"e{i:00}", start.AddMinutes(i), itemId: i % 2 == 0 ? "visible" : "hidden"));
            }

            await _store.AddEventAsync(Event("instance", start.AddMinutes(20), itemId: null, category: ActivityCategory.Instance));

            var (page, total) = await _store.QueryEventsAsync(
                new ActivityQuery { PageSize = 2, PageIndex = 1 },
                scope => scope.ManagedItemId == "visible");

            Assert.AreEqual(5, total);
            CollectionAssert.AreEqual(new[] { "e04", "e02" }, page.Select(p => p.Id).ToArray());
        }

        [TestMethod]
        public async Task Query_FiltersByProblemsCategoryAndKeyword()
        {
            var start = DateTimeOffset.UtcNow.AddHours(-1);

            await _store.AddEventAsync(Event("ok", start, "item-1", title: "Renewed shop.example.org"));
            await _store.AddEventAsync(Event("fail", start.AddMinutes(1), "item-1", status: RequestState.Error, title: "api.contoso.net failed at validation"));
            await _store.AddEventAsync(Event("conn", start.AddMinutes(2), null, ActivityCategory.Instance, title: "web-03 disconnected"));

            Assert.AreEqual("fail", (await _store.QueryEventsAsync(new ActivityQuery { ProblemsOnly = true })).Results.Single().Id);
            Assert.AreEqual("conn", (await _store.QueryEventsAsync(new ActivityQuery { Categories = [ActivityCategory.Instance] })).Results.Single().Id);
            Assert.AreEqual("fail", (await _store.QueryEventsAsync(new ActivityQuery { Keyword = "CONTOSO" })).Results.Single().Id);
        }

        [TestMethod]
        public async Task Runs_AreStoredWithMessages_ButListedWithout()
        {
            var run = new RequestRun
            {
                RunId = "run-1",
                InstanceId = "instance-1",
                ManagedItemId = "item-1",
                ItemTitle = "shop.example.org",
                Started = DateTimeOffset.UtcNow.AddMinutes(-2),
                Completed = DateTimeOffset.UtcNow,
                Outcome = RequestState.Error,
                Messages = [new RequestRunMessage { Message = "Validation failed", Timestamp = DateTimeOffset.UtcNow }]
            };

            await _store.UpsertRunAsync(run);

            var listed = (await _store.QueryRunsAsync(new RequestRunQuery { Outcomes = [RequestState.Error] })).Results.Single();
            Assert.AreEqual(0, listed.Messages.Count);

            var fetched = await _store.GetRunAsync("run-1");
            Assert.AreEqual("Validation failed", fetched!.Messages.Single().Message);

            Assert.AreEqual(0, (await _store.QueryRunsAsync(new RequestRunQuery { Outcomes = [RequestState.Success] })).Total);
        }

        [TestMethod]
        public async Task Purge_RemovesHistoryOlderThanTheCutoff()
        {
            var now = DateTimeOffset.UtcNow;

            await _store.AddEventAsync(Event("old", now.AddDays(-100)));
            await _store.AddEventAsync(Event("new", now.AddDays(-1)));
            await _store.AddStatusSnapshotAsync("instance-1", now.AddDays(-100), new StatusSummary { Total = 1 });

            await _store.PurgeOlderThanAsync(now.AddDays(-90));

            Assert.AreEqual("new", (await _store.QueryEventsAsync(new ActivityQuery())).Results.Single().Id);
            Assert.AreEqual(0, (await _store.GetStatusSnapshotsAtAsync(now)).Count);
        }

        [TestMethod]
        public async Task StatusSnapshots_GiveTheLatestAtOrBeforeATime()
        {
            var now = DateTimeOffset.UtcNow;

            await _store.AddStatusSnapshotAsync("instance-1", now.AddHours(-30), new StatusSummary { Total = 10, Error = 1 });
            await _store.AddStatusSnapshotAsync("instance-1", now.AddHours(-25), new StatusSummary { Total = 11, Error = 2 });
            await _store.AddStatusSnapshotAsync("instance-1", now.AddHours(-1), new StatusSummary { Total = 12, Error = 3 });

            var dayAgo = await _store.GetStatusSnapshotsAtAsync(now.AddDays(-1));

            Assert.AreEqual(2, dayAgo["instance-1"].Error);
        }

        [TestMethod]
        public async Task LatestEventsByInstance_GiveEachInstancesLastConnectionChange()
        {
            var start = DateTimeOffset.UtcNow.AddHours(-2);

            await _store.AddEventAsync(new ActivityEvent { Id = "1", Timestamp = start, InstanceId = "a", EventType = ActivityEventTypes.InstanceConnected, Category = ActivityCategory.Instance });
            await _store.AddEventAsync(new ActivityEvent { Id = "2", Timestamp = start.AddMinutes(5), InstanceId = "a", EventType = ActivityEventTypes.InstanceDisconnected, Category = ActivityCategory.Instance });
            await _store.AddEventAsync(new ActivityEvent { Id = "3", Timestamp = start.AddMinutes(1), InstanceId = "b", EventType = ActivityEventTypes.InstanceConnected, Category = ActivityCategory.Instance });

            var latest = await _store.GetLatestEventsByInstanceAsync([ActivityEventTypes.InstanceConnected, ActivityEventTypes.InstanceDisconnected]);

            Assert.AreEqual(ActivityEventTypes.InstanceDisconnected, latest.Single(e => e.InstanceId == "a").EventType);
            Assert.AreEqual(ActivityEventTypes.InstanceConnected, latest.Single(e => e.InstanceId == "b").EventType);
        }
    }

    [TestClass]
    public class HubActivityServiceTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

        [TestMethod]
        public void TagFilter_KeepsActivityOfInstancesWithMatchingTags_ButNotHubActivity()
        {
            var filtered = new HubActivityService.ViewScope
            {
                CanListInstances = true,
                IsTagFiltered = true,
                MatchingInstanceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "contoso-server" }
            };

            Assert.IsTrue(filtered.Permits(new ActivityRecordScope("CONTOSO-SERVER", null, ActivityCategory.Instance)));
            Assert.IsFalse(filtered.Permits(new ActivityRecordScope("fabrikam-server", null, ActivityCategory.Instance)));
            Assert.IsFalse(filtered.Permits(new ActivityRecordScope(null, null, ActivityCategory.Hub)));

            var unfiltered = new HubActivityService.ViewScope { CanListInstances = true };

            Assert.IsTrue(unfiltered.Permits(new ActivityRecordScope("fabrikam-server", null, ActivityCategory.Instance)));
            Assert.IsTrue(unfiltered.Permits(new ActivityRecordScope(null, null, ActivityCategory.Hub)));
        }

        [TestMethod]
        public void Attention_ForARequestWaitingOnAPerson()
        {
            var item = new ManagedCertificate { Id = "1", Name = "shop.example.com", LastRenewalStatus = RequestState.Paused, RenewalFailureMessage = "Create the DNS record\r\nmore detail" };

            var attention = HubActivityService.GetItemAttention(item, Now);

            Assert.AreEqual(AttentionKinds.RequestWaiting, attention!.Kind);
            Assert.AreEqual(RequestState.Paused, attention.Severity);
            Assert.AreEqual("Create the DNS record", attention.Detail);
        }

        [TestMethod]
        public void Attention_ForRepeatedFailures_ButNotASingleFailureWithTimeToSpare()
        {
            var once = new ManagedCertificate { Id = "1", Name = "a", LastRenewalStatus = RequestState.Error, RenewalFailureCount = 1, DateExpiry = Now.AddDays(40), IncludeInAutoRenew = true, RenewalPlan = new RenewalDueInfo { DateNextRenewalAttempt = Now.AddHours(2) } };
            Assert.IsNull(HubActivityService.GetItemAttention(once, Now));

            var repeated = new ManagedCertificate { Id = "2", Name = "api.contoso.net", LastRenewalStatus = RequestState.Error, RenewalFailureCount = 4, DateExpiry = Now.AddDays(9), RenewalFailureMessage = "DNS TXT record not found" };
            var attention = HubActivityService.GetItemAttention(repeated, Now);

            Assert.AreEqual(AttentionKinds.CertificateFailing, attention!.Kind);
            Assert.AreEqual("api.contoso.net has failed its last 4 renewals", attention.Title);
            StringAssert.Contains(attention.Detail, "expires in 9 days");
        }

        [TestMethod]
        public void Attention_ForADeploymentFailureAfterTheCertificateWasIssued()
        {
            var item = new ManagedCertificate
            {
                Id = "1",
                Name = "files.example.net",
                LastRenewalStatus = RequestState.Error,
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success },
                RenewalFailureMessage = "Export to share: access denied"
            };

            Assert.AreEqual(AttentionKinds.DeploymentFailing, HubActivityService.GetItemAttention(item, Now)!.Kind);
        }

        [TestMethod]
        public void Attention_ForExpiryBeforeAnyPlannedRenewal()
        {
            var planned = new ManagedCertificate { Id = "1", Name = "ok", DateExpiry = Now.AddDays(10), IncludeInAutoRenew = true, RenewalPlan = new RenewalDueInfo { DateNextRenewalAttempt = Now.AddDays(1) } };
            Assert.IsNull(HubActivityService.GetItemAttention(planned, Now), "a renewal is planned in time");

            var onHold = new ManagedCertificate { Id = "2", Name = "legacy.example.org", DateExpiry = Now.AddDays(5), IncludeInAutoRenew = true, RenewalPlan = new RenewalDueInfo { DateNextRenewalAttempt = Now.AddDays(1), IsRenewalOnHold = true } };
            var attention = HubActivityService.GetItemAttention(onHold, Now);

            Assert.AreEqual(AttentionKinds.CertificateExpiring, attention!.Kind);
            Assert.AreEqual(RequestState.Warning, attention.Severity);

            var imminent = new ManagedCertificate { Id = "3", Name = "soon", DateExpiry = Now.AddDays(2), IncludeInAutoRenew = true, RenewalPlan = new RenewalDueInfo { DateNextRenewalAttempt = Now.AddHours(1) } };
            Assert.AreEqual(RequestState.Error, HubActivityService.GetItemAttention(imminent, Now)!.Severity, "expiring within days needs attention whatever the plan");
        }

        [TestMethod]
        public void UpcomingRenewals_AreCountedByLocalDay()
        {
            var items = new List<(string, ManagedCertificate)>
            {
                ("i1", new ManagedCertificate { Id = "due", Name = "due", IncludeInAutoRenew = true, RenewalPlan = new RenewalDueInfo { IsRenewalDue = true } }),
                ("i1", new ManagedCertificate { Id = "window", Name = "window", IncludeInAutoRenew = true, RenewalPlan = new RenewalDueInfo { IsRenewalDue = true, IsDeferredByMaintenanceWindow = true, DateNextRenewalAttempt = Now.AddDays(3) } }),
                ("i1", new ManagedCertificate { Id = "later", Name = "later", IncludeInAutoRenew = true, DateExpiry = Now.AddDays(60), RenewalPlan = new RenewalDueInfo { DateNextRenewalAttempt = Now.AddDays(30) } }),
                ("i1", new ManagedCertificate { Id = "manual", Name = "manual", IncludeInAutoRenew = false, DateExpiry = Now.AddDays(4) })
            };

            // UTC+8: noon UTC is 20:00 local, so today locally is still the 23rd
            var result = HubActivityService.BuildUpcomingRenewals(items, new Dictionary<string, string> { { "i1", "web-01" } }, new UpcomingRenewalsQuery { Days = 14, UtcOffsetMinutes = 480 }, Now);

            Assert.AreEqual(14, result.Days.Count);
            Assert.AreEqual("2026-09-23", result.Days[0].Date);
            Assert.AreEqual(1, result.Days[0].Planned, "a renewal due now is attempted today");
            Assert.AreEqual(1, result.Days[3].Planned);
            Assert.AreEqual(1, result.Days[3].InMaintenanceWindow);
            Assert.AreEqual(0, result.Days.Sum(d => d.Planned) - 2, "a renewal beyond the period is not counted");
            Assert.AreEqual("manual", result.ExpiringBeforeRenewal.Single().Title);
            Assert.AreEqual(1, result.Days[4].ExpiringBeforeRenewal);
        }

        [TestMethod]
        public void ConnectionHistory_FollowsRecordedChanges_AndEndsWithTheCurrentState()
        {
            var from = Now.AddHours(-24);
            var instance = new ManagedInstanceInfo { InstanceId = "web-03", Title = "web-03" };

            var before = new ActivityEvent { InstanceId = "web-03", EventType = ActivityEventTypes.InstanceConnected, Timestamp = from.AddHours(-5) };

            var events = new List<ActivityEvent>
            {
                new ActivityEvent { InstanceId = "web-03", EventType = ActivityEventTypes.InstanceDisconnected, Timestamp = from.AddHours(5), Detail = "Connection lost: timeout" },
                new ActivityEvent { InstanceId = "web-03", EventType = ActivityEventTypes.InstanceConnected, Timestamp = from.AddHours(6) },
                new ActivityEvent { InstanceId = "other", EventType = ActivityEventTypes.InstanceDisconnected, Timestamp = from.AddHours(7) },
                new ActivityEvent { InstanceId = "web-03", EventType = ActivityEventTypes.InstanceDisconnected, Timestamp = Now.AddMinutes(-47) }
            };

            var history = HubActivityService.BuildConnectionHistory(instance, before, hubEventBefore: null, events, currentConnection: null, from, Now);

            CollectionAssert.AreEqual(
                new[] { ConnectionStatus.Connected, ConnectionStatus.Disconnected, ConnectionStatus.Connected, ConnectionStatus.Disconnected },
                history.Segments.Select(s => s.Status).ToArray());

            Assert.AreEqual(from, history.Segments.First().Start);
            Assert.AreEqual(Now, history.Segments.Last().End);
            Assert.AreEqual("Connection lost: timeout", history.Segments[1].Detail);
            Assert.AreEqual(ConnectionStatus.Disconnected, history.CurrentStatus);
            Assert.AreEqual(Now.AddMinutes(-47), history.CurrentStatusSince);
        }

        [TestMethod]
        public void ConnectionHistory_IsUnknownWhileTheHubWasStopped()
        {
            var from = Now.AddHours(-24);
            var instance = new ManagedInstanceInfo { InstanceId = "web-01", Title = "web-01" };

            var events = new List<ActivityEvent>
            {
                new ActivityEvent { InstanceId = "web-01", EventType = ActivityEventTypes.InstanceConnected, Timestamp = from.AddHours(1) },
                new ActivityEvent { InstanceId = "hub", EventType = ActivityEventTypes.HubStopped, Timestamp = from.AddHours(2) },
                new ActivityEvent { InstanceId = "hub", EventType = ActivityEventTypes.HubStarted, Timestamp = from.AddHours(3) },
                new ActivityEvent { InstanceId = "web-01", EventType = ActivityEventTypes.InstanceConnected, Timestamp = from.AddHours(3).AddSeconds(10) }
            };

            var connected = new ManagedInstanceInfo { InstanceId = "web-01", DateLastReported = Now.AddSeconds(-20) };

            var history = HubActivityService.BuildConnectionHistory(instance, stateBefore: null, hubEventBefore: null, events, connected, from, Now);

            CollectionAssert.AreEqual(
                new string?[] { null, ConnectionStatus.Connected, null, ConnectionStatus.Disconnected, ConnectionStatus.Connected },
                history.Segments.Select(s => s.Status).ToArray());

            Assert.AreEqual(ConnectionStatus.Connected, history.CurrentStatus);
        }
    }
}
