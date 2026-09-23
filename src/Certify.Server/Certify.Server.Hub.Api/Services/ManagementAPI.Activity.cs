using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Hub activity history and overview queries. These are answered by the hub from what it has recorded and cached,
    /// not by a managed instance.
    /// </summary>
    public partial class ManagementAPI
    {
        private Activity.HubActivityService ActivityService => _activityService
            ?? throw new InvalidOperationException("Hub activity history is not available.");

        /// <summary>
        /// Query the hub activity feed
        /// </summary>
        public Task<ActivityQueryResult> GetActivity(ActivityQuery query, AuthContext? authContext)
            => ActivityService.GetActivityAsync(query, authContext);

        /// <summary>
        /// Daily totals of request outcomes and deferred renewals
        /// </summary>
        public Task<ICollection<ActivityDailyTotal>> GetActivityTotals(ActivityTotalsQuery query, AuthContext? authContext)
            => ActivityService.GetActivityTotalsAsync(query, authContext);

        /// <summary>
        /// Query request run history
        /// </summary>
        public Task<RequestRunQueryResult> GetRequestRuns(RequestRunQuery query, AuthContext? authContext)
            => ActivityService.GetRequestRunsAsync(query, authContext);

        /// <summary>
        /// Get a request run, with its stages and messages
        /// </summary>
        public async Task<RequestRun> GetRequestRun(string runId, AuthContext? authContext)
            => await ActivityService.GetRequestRunAsync(runId, authContext) ?? new RequestRun { RunId = string.Empty };

        /// <summary>
        /// Everything needing a person's attention
        /// </summary>
        public Task<ICollection<AttentionItem>> GetAttentionItems(HubViewFilter filter, AuthContext? authContext)
            => ActivityService.GetAttentionItemsAsync(filter, authContext);

        /// <summary>
        /// Renewals planned over the coming days
        /// </summary>
        public Task<UpcomingRenewals> GetUpcomingRenewals(UpcomingRenewalsQuery query, AuthContext? authContext)
            => ActivityService.GetUpcomingRenewalsAsync(query, authContext);

        /// <summary>
        /// When each instance was connected, not responding or disconnected over a period
        /// </summary>
        public Task<ICollection<InstanceConnectionHistory>> GetInstanceConnectionHistory(InstanceConnectionQuery query, AuthContext? authContext)
            => ActivityService.GetInstanceConnectionHistoryAsync(query, authContext);

        /// <summary>
        /// The certificate status counts as they stood about a day ago
        /// </summary>
        public Task<StatusSummaryBaseline> GetStatusSummaryBaseline(HubViewFilter filter, AuthContext? authContext)
            => ActivityService.GetStatusSummaryBaselineAsync(filter, authContext);
    }
}
