using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        Task<string> GetAppVersion(AuthContext authContext = null);

        Task<UpdateCheck> CheckForUpdates(AuthContext authContext = null);

        Task<List<Models.Config.ActionResult>> PerformServiceDiagnostics(AuthContext authContext = null);
        Task<List<Models.Config.ActionResult>> PerformManagedCertMaintenance(string id = null, AuthContext authContext = null);

        Task<List<ActionStep>> SetDefaultDataStore(string dataStoreId, AuthContext authContext = null);
        Task<List<ProviderDefinition>> GetDataStoreProviders(AuthContext authContext = null);
        Task<List<DataStoreConnection>> GetDataStoreConnections(AuthContext authContext = null);
        Task<List<ActionStep>> CopyDataStore(string sourceId, string targetId, AuthContext authContext = null);
        Task<List<ActionStep>> UpdateDataStoreConnection(DataStoreConnection dataStoreConnection, AuthContext authContext = null);
        Task<List<ActionStep>> TestDataStoreConnection(DataStoreConnection dataStoreConnection, AuthContext authContext = null);

        #endregion System

        #region Server
        Task<bool> IsServerAvailable(StandardServerTypes serverType, AuthContext authContext = null);

        Task<List<SiteInfo>> GetServerSiteList(StandardServerTypes serverType, string itemId = null, AuthContext authContext = null);

        Task<Version> GetServerVersion(StandardServerTypes serverType, AuthContext authContext = null);

        Task<List<DomainOption>> GetServerSiteDomains(StandardServerTypes serverType, string serverSiteId, AuthContext authContext = null);

        Task<List<ActionStep>> RunConfigurationDiagnostics(StandardServerTypes serverType, string serverSiteId, AuthContext authContext = null);

        Task<List<SimpleAuthorizationChallengeItem>> GetCurrentChallenges(string type, string key, AuthContext authContext = null);

        #endregion Server

        #region Preferences

        Task<Preferences> GetPreferences(AuthContext authContext = null);

        Task<bool> SetPreferences(Preferences preferences, AuthContext authContext = null);

        #endregion Preferences

        #region Credentials

        Task<List<StoredCredential>> GetCredentials(AuthContext authContext = null);

        Task<StoredCredential> UpdateCredentials(StoredCredential credential, AuthContext authContext = null);

        Task<bool> DeleteCredential(string credentialKey, AuthContext authContext = null);

        Task<ActionResult> TestCredentials(string credentialKey, AuthContext authContext = null);

        #endregion Credentials

        #region Managed Certificates

        Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter filter, AuthContext authContext = null);
        Task<ManagedCertificateSearchResult> GetManagedCertificateSearchResult(ManagedCertificateFilter filter, AuthContext authContext = null);
        Task<StatusSummary> GetManagedCertificateSummary(ManagedCertificateFilter filter, AuthContext authContext = null);

        Task<ManagedCertificate> GetManagedCertificate(string managedItemId, AuthContext authContext = null);

        Task<ManagedCertificate> UpdateManagedCertificate(ManagedCertificate site, AuthContext authContext = null);

        Task<bool> DeleteManagedCertificate(string managedItemId, AuthContext authContext = null);

        Task<StatusMessage> RevokeManageSiteCertificate(string managedItemId, AuthContext authContext = null);

        Task<List<CertificateRequestResult>> BeginAutoRenewal(RenewalSettings settings, AuthContext authContext = null);

        Task<List<CertificateRequestResult>> RedeployManagedCertificates(bool isPreviewOnly, bool includeDeploymentTasks, AuthContext authContext = null);

        Task<CertificateRequestResult> ReapplyCertificateBindings(string managedItemId, bool isPreviewOnly, bool includeDeploymentTasks, AuthContext authContext = null);

        Task<CertificateRequestResult> RefetchCertificate(string managedItemId, AuthContext authContext = null);

        Task<CertificateRequestResult> BeginCertificateRequest(string managedItemId, bool resumePaused, bool isInteractive, AuthContext authContext = null);

        Task<List<StatusMessage>> TestChallengeConfiguration(ManagedCertificate site, AuthContext authContext = null);
        Task<List<StatusMessage>> PerformChallengeCleanup(ManagedCertificate site, AuthContext authContext = null);

        Task<List<Models.Providers.DnsZone>> GetDnsProviderZones(string providerTypeId, string credentialsId, AuthContext authContext = null);

        Task<List<ActionStep>> PreviewActions(ManagedCertificate site, AuthContext authContext = null);

        Task<List<ChallengeProviderDefinition>> GetChallengeAPIList(AuthContext authContext = null);

        Task<List<DeploymentProviderDefinition>> GetDeploymentProviderList(AuthContext authContext = null);

        Task<DeploymentProviderDefinition> GetDeploymentProviderDefinition(string id, Config.DeploymentTaskConfig config, AuthContext authContext = null);

        Task<List<ActionStep>> PerformDeployment(string managedCertificateId, string taskId, bool isPreviewOnly, bool forceTaskExecute, AuthContext authContext = null);

        Task<List<ActionResult>> ValidateDeploymentTask(DeploymentTaskValidationInfo info, AuthContext authContext = null);

        Task<Models.API.LogItem[]> GetItemLog(string id, int limit, AuthContext authContext = null);

        #endregion Managed Certificates

        #region Accounts
        Task<List<CertificateAuthority>> GetCertificateAuthorities(AuthContext authContext = null);
        Task<ActionResult> UpdateCertificateAuthority(CertificateAuthority ca, AuthContext authContext = null);
        Task<ActionResult> DeleteCertificateAuthority(string id, AuthContext authContext = null);
        Task<List<AccountDetails>> GetAccounts(AuthContext authContext = null);
        Task<ActionResult> AddAccount(ContactRegistration contact, AuthContext authContext = null);
        Task<ActionResult> UpdateAccountContact(ContactRegistration contact, AuthContext authContext = null);
        Task<ActionResult> RemoveAccount(string storageKey, bool deactivate, AuthContext authContext = null);
        Task<ActionResult> ChangeAccountKey(string storageKey, string newKeyPEM = null, AuthContext authContext = null);

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
