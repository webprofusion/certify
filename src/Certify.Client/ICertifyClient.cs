using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config.Migration;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Reporting;
using Certify.Models.Utils;
using Certify.Shared;

namespace Certify.Client
{

    /// <summary>
    /// Base API
    /// </summary>
    public partial interface ICertifyInternalApiClient
    {

        #region System

        Task<string> GetAppVersion();

        Task<UpdateCheck> CheckForUpdates();

        Task<List<Models.Config.ActionResult>> PerformServiceDiagnostics();
        Task<List<Models.Config.ActionResult>> PerformManagedCertMaintenance(string id = null);

        Task<ImportExportPackage> PerformExport(ExportRequest exportRequest);
        Task<List<ActionStep>> PerformImport(ImportRequest importRequest);

        Task<List<ActionStep>> SetDefaultDataStore(string dataStoreId);
        Task<List<ProviderDefinition>> GetDataStoreProviders();
        Task<List<DataStoreConnection>> GetDataStoreConnections();
        Task<List<ActionStep>> CopyDataStore(string sourceId, string targetId);
        Task<List<ActionStep>> UpdateDataStoreConnection(DataStoreConnection dataStoreConnection);
        Task<List<ActionStep>> TestDataStoreConnection(DataStoreConnection dataStoreConnection);

        #endregion System

        #region Server

        Task<bool> IsServerAvailable(StandardServerTypes serverType);

        Task<List<SiteInfo>> GetServerSiteList(StandardServerTypes serverType, string itemId = null);

        Task<Version> GetServerVersion(StandardServerTypes serverType);

        Task<List<DomainOption>> GetServerSiteDomains(StandardServerTypes serverType, string serverSiteId);

        Task<List<ActionStep>> RunConfigurationDiagnostics(StandardServerTypes serverType, string serverSiteId);

        Task<List<SimpleAuthorizationChallengeItem>> GetCurrentChallenges(string type, string key);

        #endregion Server

        #region Preferences

        Task<Preferences> GetPreferences();

        Task<bool> SetPreferences(Preferences preferences);

        #endregion Preferences

        #region Credentials

        Task<List<StoredCredential>> GetCredentials();

        Task<StoredCredential> UpdateCredentials(StoredCredential credential);

        Task<bool> DeleteCredential(string credentialKey);

        Task<ActionResult> TestCredentials(string credentialKey);

        #endregion Credentials

        #region Managed Certificates

        Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter filter);
        Task<ManagedCertificateSearchResult> GetManagedCertificateSearchResult(ManagedCertificateFilter filter);
        Task<Summary> GetManagedCertificateSummary(ManagedCertificateFilter filter);

        Task<ManagedCertificate> GetManagedCertificate(string managedItemId);

        Task<ManagedCertificate> UpdateManagedCertificate(ManagedCertificate site);

        Task<bool> DeleteManagedCertificate(string managedItemId);

        Task<StatusMessage> RevokeManageSiteCertificate(string managedItemId);

        Task<List<CertificateRequestResult>> BeginAutoRenewal(RenewalSettings settings);

        Task<List<CertificateRequestResult>> RedeployManagedCertificates(bool isPreviewOnly, bool includeDeploymentTasks);

        Task<CertificateRequestResult> ReapplyCertificateBindings(string managedItemId, bool isPreviewOnly, bool includeDeploymentTasks);

        Task<CertificateRequestResult> RefetchCertificate(string managedItemId);

        Task<CertificateRequestResult> BeginCertificateRequest(string managedItemId, bool resumePaused, bool isInteractive);

        Task<List<StatusMessage>> TestChallengeConfiguration(ManagedCertificate site);

        Task<List<Models.Providers.DnsZone>> GetDnsProviderZones(string providerTypeId, string credentialsId);

        Task<List<ActionStep>> PreviewActions(ManagedCertificate site);

        Task<List<ChallengeProviderDefinition>> GetChallengeAPIList();

        Task<List<DeploymentProviderDefinition>> GetDeploymentProviderList();

        Task<DeploymentProviderDefinition> GetDeploymentProviderDefinition(string id, Config.DeploymentTaskConfig config);

        Task<List<ActionStep>> PerformDeployment(string managedCertificateId, string taskId, bool isPreviewOnly, bool forceTaskExecute);

        Task<List<ActionResult>> ValidateDeploymentTask(DeploymentTaskValidationInfo info);

        Task<Models.API.LogItem[]> GetItemLog(string id, int limit);

        #endregion Managed Certificates

        #region Accounts
        Task<List<CertificateAuthority>> GetCertificateAuthorities();
        Task<ActionResult> UpdateCertificateAuthority(CertificateAuthority ca);
        Task<ActionResult> DeleteCertificateAuthority(string id);
        Task<List<AccountDetails>> GetAccounts();
        Task<ActionResult> AddAccount(ContactRegistration contact);
        Task<ActionResult> UpdateAccountContact(ContactRegistration contact);
        Task<ActionResult> RemoveAccount(string storageKey, bool deactivate);
        Task<ActionResult> ChangeAccountKey(string storageKey, string newKeyPEM = null);

        #endregion Accounts

    }

    /// <summary>
    /// Client to talk to the core Certify Service 
    /// </summary>
    public interface ICertifyClient : ICertifyInternalApiClient
    {

        event Action<string, string> OnMessageFromService;

        event Action<RequestProgressState> OnRequestProgressStateUpdated;

        event Action<ManagedCertificate> OnManagedCertificateUpdated;

        Task ConnectStatusStreamAsync();

        Shared.ServerConnection GetConnectionInfo();

        Task<bool> EnsureServiceHubConnected();
    }
}
