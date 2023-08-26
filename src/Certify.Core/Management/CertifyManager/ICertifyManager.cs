using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Config.Migration;
using Certify.Models;
using Certify.Models.API;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers;
using Certify.Shared;

namespace Certify.Management
{
    public interface ICertifyManager
    {
        Task Init();

        void SetStatusReporting(IStatusReporting statusReporting);

        Task<bool> IsServerTypeAvailable(StandardServerTypes serverType);

        Task<Version> GetServerTypeVersion(StandardServerTypes serverType);

        Task<List<ActionStep>> RunServerDiagnostics(StandardServerTypes serverType, string siteId);

        Task<ManagedCertificate> GetManagedCertificate(string id);

        Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter filter = null);
        Task<ManagedCertificateSearchResult> GetManagedCertificateResults(ManagedCertificateFilter filter = null);
        Task<Certify.Reporting.Summary> GetManagedCertificateSummary(ManagedCertificateFilter filter = null);

        Task<ManagedCertificate> UpdateManagedCertificate(ManagedCertificate site);

        Task DeleteManagedCertificate(string id);

        Task<ImportExportPackage> PerformExport(ExportRequest exportRequest);
        Task<List<ActionStep>> PerformImport(ImportRequest importRequest);

        Task<List<SimpleAuthorizationChallengeItem>> GetCurrentChallengeResponses(string challengeType, string key = null);

        Task<List<AccountDetails>> GetAccountRegistrations();

        Task<ActionResult> AddAccount(ContactRegistration reg);

        Task<ActionResult> UpdateAccountContact(string storageKey, ContactRegistration contact);

        Task<ActionResult> RemoveAccount(string storageKey, bool includeAccountDeactivation = false);
        Task<ActionResult<AccountDetails>> ChangeAccountKey(string storageKey, string newKeyPEM = null);

        Task<List<StatusMessage>> TestChallenge(ILog log, ManagedCertificate managedCertificate, bool isPreviewMode, IProgress<RequestProgressState> progress = null);
        Task<List<ActionResult>> PerformServiceDiagnostics();
        Task<List<DnsZone>> GetDnsProviderZones(string providerTypeId, string credentialsId);
        Task<ActionResult> UpdateCertificateAuthority(CertificateAuthority certificateAuthority);
        Task<List<CertificateAuthority>> GetCertificateAuthorities();

        Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate);

        Task<CertificateRequestResult> PerformDummyCertificateRequest(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null);
        Task<ActionResult> RemoveCertificateAuthority(string id);
        Task<List<SiteInfo>> GetPrimaryWebSites(StandardServerTypes serverType, bool ignoreStoppedSites, string itemId = null);

        Task<List<CertificateRequestResult>> RedeployManagedCertificates(ManagedCertificateFilter filter, IProgress<RequestProgressState> progress = null, bool isPreviewOnly = false, bool includeDeploymentTasks = false);

        Task<CertificateRequestResult> DeployCertificate(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null, bool isPreviewOnly = false, bool includeDeploymentTasks = false);

        Task<CertificateRequestResult> FetchCertificate(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null, bool isPreviewOnly = false);

        Task<CertificateRequestResult> PerformCertificateRequest(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null, bool resumePaused = false, bool skipRequest = false, bool failOnSkip = false, bool skipTasks = false, bool isInteractive = false, string reason = null);

        Task<List<DomainOption>> GetDomainOptionsFromSite(StandardServerTypes serverType, string siteId);

        Task<List<CertificateRequestResult>> PerformRenewAll(RenewalSettings settings, ConcurrentDictionary<string, Progress<RequestProgressState>> progressTrackers = null);

        Task<bool> PerformRenewalTasks();

        Task<bool> PerformDailyMaintenanceTasks();

        Task PerformCertificateCleanup();

        Task<List<ActionResult>> PerformCertificateMaintenanceTasks(string managedItemId = null);

        Task<List<ActionStep>> GeneratePreview(ManagedCertificate item);

        void ReportProgress(IProgress<RequestProgressState> progress, RequestProgressState state, bool logThisEvent = true);

        Task<List<ActionStep>> PerformDeploymentTask(ILog log, string managedCertificateId, string taskId, bool isPreviewOnly, bool skipDeferredTasks, bool forceTaskExecution);

        Task<List<DeploymentProviderDefinition>> GetDeploymentProviders();

        Task<List<ActionResult>> ValidateDeploymentTask(ManagedCertificate managedCertificate, DeploymentTaskConfig taskConfig);

        Task<DeploymentProviderDefinition> GetDeploymentProviderDefinition(string id, DeploymentTaskConfig config);

        Task<LogItem[]> GetItemLog(string id, int limit = 1000);

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
    }
}
