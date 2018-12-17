using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;

namespace Certify.Client
{
    /// <summary>
    /// Client to talk to the core Certify Service 
    /// </summary>
    public interface ICertifyClient
    {
        /*
         *
        Preferences: /preferences

        Settings to be save/loaded via the client api (still tied to Core App)

        GetPreferences()
        SetPreferences()

        Primary Contact Registration /contact/

        GetPrimaryContact
        SetPrimaryContact : set the current contact registration for all subsequent requests

        Web Server Status: /server

        GetServerSummary("IIS") - summary of general info for this server, IIS Version, .Net version etc
        IsServerAvailable("IIS");
        GetServerVersion("IIS")
        GetServerSiteList("IIS")
        GetServerSiteDomains("IIS",siteId);

         Managed Certificates: /managedcertificates/

        GetManagedCertificates(filter)
        GetManagedCertificate(id)
        AddOrUpdateManagedCertificate
        DeleteManagedCertificate

        PreviewAutoRenewal - return list of managed sites which would be currently included in an auto renew run
        BeginAutoRenewal - Begin auto renewal process and returns list of managedcertificates included in this run
        BeginCertificateRequest(managedcertificate id) - Begins a single manage site certificate request
        CheckCertificateRequest(managedcertificate id) - poll until completed/failed or timeout
        */

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

        Task<List<CertificateRequestResult>> BeginAutoRenewal();

        Task<CertificateRequestResult> ReapplyCertificateBindings(string managedItemId, bool isPreviewOnly);

        Task<CertificateRequestResult> BeginCertificateRequest(string managedItemId, bool resumePaused);

        Task<RequestProgressState> CheckCertificateRequest(string managedItemId);

        Task<List<StatusMessage>> TestChallengeConfiguration(ManagedCertificate site);

        Task<List<Models.Providers.DnsZone>> GetDnsProviderZones(string providerTypeId, string credentialsId);

        Task<List<ActionStep>> PreviewActions(ManagedCertificate site);

        Task<List<ProviderDefinition>> GetChallengeAPIList();

        #endregion Managed Certificates

        #region Contacts

        Task<string> GetPrimaryContact();

        Task<bool> SetPrimaryContact(ContactRegistration contact);

        #endregion Contacts
    }
}
