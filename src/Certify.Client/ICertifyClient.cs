using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Utils;

namespace Certify.Client
{
    /// <summary>
    /// Client to talk to the core Certify Service 
    /// </summary>
    public interface ICertifyClient
    {

        Shared.ServiceConfig GetAppServiceConfig();

        #region Status

        event Action<string, string> OnMessageFromService;

        event Action<RequestProgressState> OnRequestProgressStateUpdated;

        event Action<ManagedCertificate> OnManagedCertificateUpdated;

        Task ConnectStatusStreamAsync();

        #endregion Status

        #region System

        Task<string> GetAppVersion();

        Task<UpdateCheck> CheckForUpdates();

        #endregion System

        #region Server

        Task<bool> IsServerAvailable(StandardServerTypes serverType);

        Task<List<BindingInfo>> GetServerSiteList(StandardServerTypes serverType);

        Task<Version> GetServerVersion(StandardServerTypes serverType);

        Task<List<DomainOption>> GetServerSiteDomains(StandardServerTypes serverType, string serverSiteId);

        Task<List<ActionStep>> RunConfigurationDiagnostics(StandardServerTypes serverType, string serverSiteId);

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

        Task<ManagedCertificate> GetManagedCertificate(string managedItemId);

        Task<ManagedCertificate> UpdateManagedCertificate(ManagedCertificate site);

        Task<bool> DeleteManagedCertificate(string managedItemId);

        Task<StatusMessage> RevokeManageSiteCertificate(string managedItemId);

        Task<List<CertificateRequestResult>> BeginAutoRenewal(RenewalSettings settings);

        Task<CertificateRequestResult> ReapplyCertificateBindings(string managedItemId, bool isPreviewOnly);

        Task<CertificateRequestResult> RefetchCertificate(string managedItemId);

        Task<CertificateRequestResult> BeginCertificateRequest(string managedItemId, bool resumePaused);

        Task<RequestProgressState> CheckCertificateRequest(string managedItemId);

        Task<List<StatusMessage>> TestChallengeConfiguration(ManagedCertificate site);

        Task<List<Models.Providers.DnsZone>> GetDnsProviderZones(string providerTypeId, string credentialsId);

        Task<List<ActionStep>> PreviewActions(ManagedCertificate site);

        Task<List<ChallengeProviderDefinition>> GetChallengeAPIList();

        Task<List<DeploymentProviderDefinition>> GetDeploymentProviderList();

        Task<DeploymentProviderDefinition> GetDeploymentProviderDefinition(string id, Config.DeploymentTaskConfig config);

        Task<List<ActionStep>> PerformDeployment(string managedCertificateId, string taskId, bool isPreviewOnly);

        Task<List<ActionResult>> ValidateDeploymentTask(DeploymentTaskValidationInfo info);

        #endregion Managed Certificates

        #region Accounts
        Task<List<CertificateAuthority>> GetCertificateAuthorities();

        Task<List<AccountDetails>> GetAccounts();
        Task<ActionResult> AddAccount(ContactRegistration contact);
        Task<ActionResult> RemoveAccount(string storageKey);

        #endregion Accounts
    }
}
