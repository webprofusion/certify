using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Config;
using Certify.Core.Management.Access;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Config.Migration;
using Certify.Models.Hub;
using Certify.Models.Providers;
using Certify.Models.Reporting;
using Certify.Providers;
using Certify.Shared;

namespace Certify.Management
{
    public interface ICertifyManager
    {
        Task Init(bool enablePlugins = true);
        void SetStatusReporting(IStatusReporting statusReporting);

        /// <summary>
        /// Returns true if the service is running in degraded mode (data store unavailable)
        /// </summary>
        bool IsInDegradedMode { get; }

        /// <summary>
        /// Gets the current data store connection status
        /// </summary>
        DataStoreStatus GetDataStoreStatus();

        /// <summary>
        /// Attempt to reconnect to the data store after a failure
        /// </summary>
        Task<ActionResult> AttemptDataStoreReconnection();

        Task<bool> IsServerTypeAvailable(StandardServerTypes serverType);
        Task<Version> GetServerTypeVersion(StandardServerTypes serverType);
        Task<List<ActionStep>> RunServerDiagnostics(StandardServerTypes serverType, string siteId);
        Task<ManagedCertificate> GetManagedCertificate(string id);
        Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter? filter = null);
        Task<ManagedCertificateSearchResult> GetManagedCertificateResults(ManagedCertificateFilter? filter = null);
        Task<Certify.Models.Reporting.StatusSummary> GetManagedCertificateSummary(ManagedCertificateFilter? filter = null);
        Task<ManagedCertificate> UpdateManagedCertificate(ManagedCertificate site);
        Task<ActionResult> DeleteManagedCertificate(string id);
        Task QueueAllManagedCertificateStatusReports();
        Task<ImportExportPackage> PerformExport(ExportRequest exportRequest);
        Task<List<ActionStep>> PerformImport(ImportRequest importRequest);
        Task<List<SimpleAuthorizationChallengeItem>> GetCurrentChallengeResponses(string challengeType, string? key = null);

        Task<List<AccountDetails>> GetAccountRegistrations();
        Task<ActionResult> AddAccount(ContactRegistration reg);
        Task<ActionResult> UpdateAccountContact(string storageKey, ContactRegistration contact);
        Task<ActionResult> RemoveAccount(string storageKey, bool includeAccountDeactivation = false);
        Task<ActionResult<AccountDetails>> ChangeAccountKey(string storageKey, string? newKeyPEM = null);

        Task<List<StatusMessage>> TestChallenge(ILog log, ManagedCertificate managedCertificate, bool isPreviewMode, IProgress<RequestProgressState>? progress = null);
        Task<List<StatusMessage>> PerformChallengeCleanup(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState>? progress = null);
        Task<List<ActionResult>> PerformServiceDiagnostics();
        Task<DnsZoneQueryResult> GetDnsProviderZones(string providerTypeId, string credentialId);
        Task<ActionResult> UpdateCertificateAuthority(CertificateAuthority certificateAuthority);
        Task<List<CertificateAuthority>> GetCertificateAuthorities();
        Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate);
        Task<ManagedCertificate> ResetManagedItemStatus(string id, bool updateStatusReports = false);
        Task<CertificateRequestResult> PerformDummyCertificateRequest(ManagedCertificate managedCertificate, IProgress<RequestProgressState>? progress = null);
        Task<ActionResult> RemoveCertificateAuthority(string id);
        Task<List<SiteInfo>> GetPrimaryWebSites(StandardServerTypes serverType, bool ignoreStoppedSites, string? itemId = null);
        Task<List<CertificateRequestResult>> RedeployManagedCertificates(ManagedCertificateFilter filter, IProgress<RequestProgressState>? progress = null, bool isPreviewOnly = false, bool includeDeploymentTasks = false);
        Task<CertificateRequestResult> DeployCertificate(ManagedCertificate managedCertificate, IProgress<RequestProgressState>? progress = null, bool isPreviewOnly = false, bool includeDeploymentTasks = false);
        Task<CertificateRequestResult> PerformCertificateRequest(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState>? progress = null, bool resumePaused = false, bool skipRequest = false, bool failOnSkip = false, bool skipTasks = false, bool isInteractive = false, string? reason = null);
        Task<List<DomainOption>> GetDomainOptionsFromSite(StandardServerTypes serverType, string siteId);
        Task<List<CertificateRequestResult>> PerformRenewAll(RenewalSettings settings, CancellationToken cancellationToken);
        Task<bool> PerformRenewalTasks(CancellationToken cancellationToken);

        Task<bool> PerformDailyMaintenanceTasks();
        Task PerformCertificateCleanup();
        Task<List<ActionResult>> PerformCertificateMaintenanceTasks(string? managedItemId = null);
        Task<List<ActionStep>> GeneratePreview(ManagedCertificate item);
        void ReportProgress(IProgress<RequestProgressState> progress, RequestProgressState state, bool logThisEvent = true);
        Task<List<ActionStep>> PerformDeploymentTask(ILog log, string managedCertificateId, string taskId, bool isPreviewOnly, bool skipDeferredTasks, bool forceTaskExecution);
        Task<List<DeploymentProviderDefinition>> GetDeploymentProviders();
        Task<List<ActionResult>> ValidateDeploymentTask(ManagedCertificate managedCertificate, DeploymentTaskConfig taskConfig);
        Task<DeploymentProviderDefinition> GetDeploymentProviderDefinition(string id, DeploymentTaskConfig config);
        Task<LogItem[]> GetItemLog(string id, int limit = 1000);

        Task<ICollection<SystemLogFileInfo>> GetServiceLogFiles(int maxFiles = 20);
        Task<string[]> GetServiceLog(string logType, int limit = 10000);
        ICredentialsManager GetCredentialsManager();
        IManagedItemStore GetManagedItemStore();
        Task ApplyPreferences();

        Task<List<ProviderDefinition>> GetDataStoreProviders();
        Task<List<DataStoreConnection>> GetDataStores();
        Task<List<ActionStep>> CopyDateStoreToTarget(string sourceId, string destId);
        Task<List<ActionStep>> SetDefaultDataStore(string dataStoreId);
        Task<List<ActionStep>> UpdateDataStoreConnection(DataStoreConnection dataStore);
        Task<List<ActionStep>> RemoveDataStoreConnection(string dataStoreId);
        Task<List<ActionStep>> TestDataStoreConnection(DataStoreConnection connection);

        Task<ActionResult> TestCredentials(string storageKey);
        Task<IAccessControl> GetCurrentAccessControl();

        Task<ICollection<ManagedChallenge>> GetManagedChallenges();
        Task<ICollection<ManagedChallenge>> GetManagedChallengesWithTagFilter(ICollection<TagScope>? tagScopes = null, bool requireAllTags = false, bool includeUntagged = false);
        Task<ICollection<ManagedChallengeSummary>> GetManagedChallengeSummaries(ICollection<TagScope>? tagScopes = null, bool requireAllTags = false, bool includeUntagged = false);
        Task<ManagedChallengeAccessScope> GetManagedChallengeAccessScope(string? securityPrincipalId, ICollection<string>? scopedAssignedRoles = null, string requiredActionId = StandardResourceActions.ManagedChallengeRequest);
        Task<ICollection<ManagedChallenge>> GetAccessibleManagedChallenges(string? securityPrincipalId, ICollection<string>? scopedAssignedRoles = null, string requiredActionId = StandardResourceActions.ManagedChallengeRequest);
        Task<ICollection<ManagedChallenge>> GetAccessibleManagedChallenges(ManagedChallengeAccessScope scope);
        Task<(bool CanSatisfy, List<string> UnsatisfiedIdentifiers)> CanPrincipalSatisfyManagedChallengeIdentifiers(string? securityPrincipalId, IEnumerable<string> identifiers, ICollection<string>? scopedAssignedRoles = null, string requiredActionId = StandardResourceActions.ManagedAcmePerformOrder);
        Task<ActionResult> UpdateManagedChallenge(ManagedChallenge update);
        Task<ActionResult> DeleteManagedChallenge(string id);
        Task<ManagedChallengeOperation> BeginManagedChallengeRequest(ManagedChallengeRequest request);
        Task<ManagedChallengeOperation> BeginManagedChallengeRequest(ManagedChallengeRequest request, ICollection<TagScope>? tagScopes, bool requireAllTags = false);
        Task<ManagedChallengeOperation?> GetManagedChallengeOperation(string operationId);
        Task<ActionResult> PerformManagedChallengeRequest(ManagedChallengeRequest request);
        Task<ActionResult> PerformManagedChallengeRequest(ManagedChallengeRequest request, ICollection<TagScope>? tagScopes, bool requireAllTags = false);
        Task<ActionResult> CleanupManagedChallengeRequest(ManagedChallengeRequest request);

        Task<HubSettings> GetHubSettings();
        Task<ActionResult> UpdateHubSettings(HubSettings settings);

        Task<ActionResult> JoinManagementHub(string url, ClientSecret clientSecret);
        Task<ActionResult<HubJoiningInfo>> CheckManagementHubCredentials(string url, ClientSecret clientSecret, bool registerInstance = false, bool reissueRequestAuthSecret = false);

        Task<InstanceCommandResult> PerformHubCommandWithResult(InstanceCommandRequest arg);
        void SetDirectManagementClient(IManagementServerClient client);
        void EnableManagementHubBackend(bool isDirectHubBackend);
        ManagedInstanceInfo GetManagedInstanceInfo();
        Task<ActionResult> CheckManagementHubConnectionStatus();

        Task<Certify.Models.Config.ActionResult<ManagedInstanceInfo>> AddHubManagedInstance(ManagedInstanceInfo item);
        Task<Certify.Models.Config.ActionResult> UpdateHubManagedInstance(string id, ManagedInstanceInfo item, bool isHeartBeatInfo);
        Task<ManagedInstanceInfo> GetHubManagedInstance(string id);
        Task<ICollection<ManagedInstanceInfo>> GetHubManagedInstances();
        Task<Certify.Models.Config.ActionResult> RemoveHubManagedInstance(string id);
        Task<Certify.Models.Config.ActionResult> RegisterManagedInstanceWithDashboard(string instanceId);
        Task<Certify.Models.Config.ActionResult> RemoveManagedInstanceFromDashboard(string instanceId);

        // Tags
        Task<ICollection<TagCategory>> GetTagCategories();
        Task<TagCategory?> GetTagCategory(string categoryKey);
        Task<Certify.Models.Config.ActionResult> AddOrUpdateTagCategory(TagCategory category);
        Task<Certify.Models.Config.ActionResult> DeleteTagCategory(string categoryKey);
        Task<ICollection<TagValue>> GetTagValues(string? categoryKey = null);
        Task<TagValue?> GetOrCreateTagValue(string categoryKey, string value);
        Task<Certify.Models.Config.ActionResult> UpdateTagValue(string valueId, string newValue, string? description = null);
        Task<Certify.Models.Config.ActionResult> DeleteTagValue(string valueId);
        Task<Certify.Models.Config.ActionResult> MergeTagValues(ICollection<string> sourceValueIds, string targetValueId);
        Task<ICollection<ItemTag>> GetAllHubItemTags(string? categoryKey = null, string? value = null, string? itemTypeId = null, string? instanceId = null);
        Task<ICollection<TagSummary>> GetHubItemTags(string itemId, string itemTypeId);
        Task<Certify.Models.Config.ActionResult> AddHubItemTags(ICollection<ItemTag> tags);
        Task<Certify.Models.Config.ActionResult> RemoveHubItemTags(ICollection<string> tagsIds);
        Task<Certify.Models.Config.ActionResult> RemoveHubItemTagByKey(string itemId, string itemType, string categoryKey, string value, string? instanceId = null);
        Task<ICollection<ItemTag>> GetItemsByTagScopes(ICollection<TagScope> scopes, string? itemType = null, bool requireAll = false, string? instanceId = null);
        Task<Certify.Models.Config.ActionResult> BulkTagOperation(ICollection<string> itemIds, string itemType, string? instanceId, ICollection<TagScope>? addTags, ICollection<TagScope>? removeTags);
        Task<ScopePreviewResult> PreviewTagScope(ICollection<TagScope> scopes, ICollection<string>? resourceTypes = null, bool requireAll = false, string? instanceId = null);

        Task<ICollection<ManagedLicense>> GetManagedLicenses();
        Task<ActionResult> AddManagedLicense(ManagedLicense item);
        Task<ActionResult> UpdateManagedLicense(ManagedLicense item);
        Task<ActionResult> RemoveManagedLicenses(string id);

        Task<ICollection<OidcProviderConfig>> GetOidcProviders(bool includeSecret = false);
        Task<ActionResult> AddOidcProvider(OidcProviderConfig item);
        Task<ActionResult> UpdateOidcProvider(OidcProviderConfig item);
        Task<ActionResult> RemovOidcProvider(string id);

        Task<HubInfo> GetHubInfo();

        Task<List<ManagedCertificateSummary>> GetHubSubscribableManagedCertificates();
    }
}
