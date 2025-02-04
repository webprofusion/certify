using Certify.Client;
using Certify.Config;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Config.Migration;
using Certify.Models.Hub;
using Certify.Models.Providers;
using Certify.Models.Reporting;
using Certify.Models.Utils;
using Certify.Shared;
using Microsoft.AspNetCore.DataProtection;
using ServiceControllers = Certify.Service.Controllers;

namespace Certify.Server.HubService.Services
{
    /// <summary>
    /// The HubService is a surrogate for the Certify Server Core Service, Service API and Client. The Hub hosts a Certify Server Core instead of talking to a Service instance over http, skipping a layer of abstraction and a communication layer.
    /// A further layer of abstraction can be skipped by implementing all controller logic in Certify.Core and using that directly
    /// </summary>
    public class CertifyHubService : ICertifyInternalApiClient
    {
        private ICertifyManager _certifyManager;
        private IDataProtectionProvider _dataProtectionProvider;
        public CertifyHubService(ICertifyManager certifyManager, IDataProtectionProvider dataProtectionProvider)
        {
            _certifyManager = certifyManager;
            _dataProtectionProvider = dataProtectionProvider;
        }

        private ServiceControllers.AccessController _accessController(AuthContext authContext)
        {
            var controller = new ServiceControllers.AccessController(_certifyManager, _dataProtectionProvider);
            controller.SetCurrentAuthContext(authContext);
            return controller;
        }

        private ServiceControllers.ManagedChallengeController _managedChallengeController(AuthContext authContext)
        {
            var controller = new ServiceControllers.ManagedChallengeController(_certifyManager);
            controller.SetCurrentAuthContext(authContext);
            return controller;
        }

        public Task<Preferences> GetPreferences(AuthContext authContext = null) => Task.FromResult(new ServiceControllers.PreferencesController(_certifyManager).GetPreferences());
        public Task<ActionResult> AddSecurityPrinciple(SecurityPrinciple principle, AuthContext authContext) => _accessController(authContext).AddSecurityPrinciple(principle);
        public Task<bool> CheckSecurityPrincipleHasAccess(AccessCheck check, AuthContext authContext) => _accessController(authContext).CheckSecurityPrincipleHasAccess(check);
        public Task<ICollection<AssignedRole>> GetSecurityPrincipleAssignedRoles(string id, AuthContext authContext) => _accessController(authContext).GetSecurityPrincipleAssignedRoles(id);
        public Task<RoleStatus> GetSecurityPrincipleRoleStatus(string id, AuthContext authContext) => _accessController(authContext).GetSecurityPrincipleRoleStatus(id);
        public Task<ICollection<SecurityPrinciple>> GetSecurityPrinciples(AuthContext authContext) => _accessController(authContext).GetSecurityPrinciples();
        public Task<ActionResult> AddAssignedAccessToken(AssignedAccessToken token, AuthContext authContext) => _accessController(authContext).AddAssignedccessToken(token);
        public Task<ActionResult> CheckApiTokenHasAccess(AccessToken token, AccessCheck check, AuthContext authContext = null) => _accessController(authContext).CheckApiTokenHasAccess(new AccessTokenCheck { Check = check, Token = token });
        public Task<ICollection<AssignedAccessToken>> GetAssignedAccessTokens(AuthContext authContext) => _accessController(authContext).GetAssignedAccessTokens();
        public Task<ActionResult> RemoveSecurityPrinciple(string id, AuthContext authContext) => _accessController(authContext).DeleteSecurityPrinciple(id);
        public Task<ActionResult> UpdateSecurityPrinciple(SecurityPrinciple principle, AuthContext authContext) => _accessController(authContext).UpdateSecurityPrinciple(principle);
        public Task<ActionResult> UpdateSecurityPrincipleAssignedRoles(SecurityPrincipleAssignedRoleUpdate update, AuthContext authContext) => _accessController(authContext).UpdateSecurityPrincipleAssignedRoles(update);
        public Task<ActionResult> UpdateSecurityPrinciplePassword(SecurityPrinciplePasswordUpdate passwordUpdate, AuthContext authContext) => _accessController(authContext).UpdatePassword(passwordUpdate);
        public Task<SecurityPrincipleCheckResponse> ValidateSecurityPrinciplePassword(SecurityPrinciplePasswordCheck passwordCheck, AuthContext authContext) => _accessController(authContext).Validate(passwordCheck);
        public Task<ICollection<Role>> GetAccessRoles(AuthContext authContext) => _accessController(authContext).GetRoles();
        public Task<ICollection<ManagedChallenge>> GetManagedChallenges(AuthContext authContext) => _managedChallengeController(authContext).Get();
        public Task<ActionResult> UpdateManagedChallenge(ManagedChallenge update, AuthContext authContext) => _managedChallengeController(authContext).Update(update);
        public Task<ActionResult> CleanupManagedChallenge(ManagedChallengeRequest request, AuthContext authContext) => _managedChallengeController(authContext).CleanupChallengeResponse(request);

        public Task<ActionResult> AddAccount(ContactRegistration contact, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<CertificateRequestResult>> BeginAutoRenewal(RenewalSettings settings, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<CertificateRequestResult> BeginCertificateRequest(string managedItemId, bool resumePaused, bool isInteractive, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<ActionResult> ChangeAccountKey(string storageKey, string newKeyPEM = null, AuthContext authContext = null) => throw new NotImplementedException();

        public Task<UpdateCheck> CheckForUpdates(AuthContext authContext = null) => throw new NotImplementedException();

        public Task<List<ActionStep>> CopyDataStore(string sourceId, string targetId, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<ActionResult> DeleteCertificateAuthority(string id, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<bool> DeleteCredential(string credentialKey, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<bool> DeleteManagedCertificate(string managedItemId, AuthContext authContext = null) => throw new NotImplementedException();

        public Task<List<AccountDetails>> GetAccounts(AuthContext authContext = null) => throw new NotImplementedException();
        public Task<string> GetAppVersion(AuthContext authContext = null) => Task.FromResult(new ServiceControllers.SystemController(_certifyManager).GetAppVersion());

        public Task<List<CertificateAuthority>> GetCertificateAuthorities(AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<ChallengeProviderDefinition>> GetChallengeAPIList(AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<StoredCredential>> GetCredentials(AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<SimpleAuthorizationChallengeItem>> GetCurrentChallenges(string type, string key, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<DataStoreConnection>> GetDataStoreConnections(AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<ProviderDefinition>> GetDataStoreProviders(AuthContext authContext = null) => throw new NotImplementedException();
        public Task<DeploymentProviderDefinition> GetDeploymentProviderDefinition(string id, DeploymentTaskConfig config, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<DeploymentProviderDefinition>> GetDeploymentProviderList(AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<DnsZone>> GetDnsProviderZones(string providerTypeId, string credentialId, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<LogItem[]> GetItemLog(string id, int limit, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<ManagedCertificate> GetManagedCertificate(string managedItemId, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter filter, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<ManagedCertificateSearchResult> GetManagedCertificateSearchResult(ManagedCertificateFilter filter, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<StatusSummary> GetManagedCertificateSummary(ManagedCertificateFilter filter, AuthContext authContext = null) => throw new NotImplementedException();


        public Task<List<DomainOption>> GetServerSiteDomains(StandardServerTypes serverType, string serverSiteId, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<SiteInfo>> GetServerSiteList(StandardServerTypes serverType, string itemId = null, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<Version> GetServerVersion(StandardServerTypes serverType, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<bool> IsServerAvailable(StandardServerTypes serverType, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<StatusMessage>> PerformChallengeCleanup(ManagedCertificate site, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<ActionStep>> PerformDeployment(string managedCertificateId, string taskId, bool isPreviewOnly, bool forceTaskExecute, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<ImportExportPackage> PerformExport(ExportRequest exportRequest, AuthContext authContext) => throw new NotImplementedException();
        public Task<ICollection<ActionStep>> PerformImport(ImportRequest importRequest, AuthContext authContext) => throw new NotImplementedException();
        public Task<List<ActionResult>> PerformManagedCertMaintenance(string id = null, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<ActionResult> PerformManagedChallenge(ManagedChallengeRequest request, AuthContext authContext) => throw new NotImplementedException();
        public Task<List<ActionResult>> PerformServiceDiagnostics(AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<ActionStep>> PreviewActions(ManagedCertificate site, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<CertificateRequestResult> ReapplyCertificateBindings(string managedItemId, bool isPreviewOnly, bool includeDeploymentTasks, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<CertificateRequestResult>> RedeployManagedCertificates(bool isPreviewOnly, bool includeDeploymentTasks, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<CertificateRequestResult> RefetchCertificate(string managedItemId, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<ActionResult> RemoveAccount(string storageKey, bool deactivate, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<ActionResult> RemoveManagedChallenge(string id, AuthContext authContext) => throw new NotImplementedException();

        public Task<StatusMessage> RevokeManageSiteCertificate(string managedItemId, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<ActionStep>> RunConfigurationDiagnostics(StandardServerTypes serverType, string serverSiteId, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<ActionStep>> SetDefaultDataStore(string dataStoreId, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<bool> SetPreferences(Preferences preferences, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<StatusMessage>> TestChallengeConfiguration(ManagedCertificate site, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<ActionResult> TestCredentials(string credentialKey, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<ActionStep>> TestDataStoreConnection(DataStoreConnection dataStoreConnection, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<ActionResult> UpdateAccountContact(ContactRegistration contact, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<ActionResult> UpdateCertificateAuthority(CertificateAuthority ca, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<StoredCredential> UpdateCredentials(StoredCredential credential, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<ActionStep>> UpdateDataStoreConnection(DataStoreConnection dataStoreConnection, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<ManagedCertificate> UpdateManagedCertificate(ManagedCertificate site, AuthContext authContext = null) => throw new NotImplementedException();

        public Task<ActionStep> UpdateManagementHub(string url, string joiningKey, AuthContext authContext = null) => throw new NotImplementedException();
        public Task<List<ActionResult>> ValidateDeploymentTask(DeploymentTaskValidationInfo info, AuthContext authContext = null) => throw new NotImplementedException();

    }
}
