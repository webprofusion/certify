using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Providers;

namespace Certify.Management
{
    public interface ICertifyManager
    {
        Task<bool> IsServerTypeAvailable(StandardServerTypes serverType);

        Task<Version> GetServerTypeVersion(StandardServerTypes serverType);

        Task<List<ActionStep>> RunServerDiagnostics(StandardServerTypes serverType, string siteId);

        Task<bool> LoadSettingsAsync(bool skipIfLoaded);

        Task<ManagedCertificate> GetManagedCertificate(string id);

        Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter filter = null);

        Task<ManagedCertificate> UpdateManagedCertificate(ManagedCertificate site);

        Task DeleteManagedCertificate(string id);

        Task<List<SimpleAuthorizationChallengeItem>> GetCurrentChallengeResponses(string challengeType);

        List<RegistrationItem> GetContactRegistrations();

        string GetPrimaryContactEmail();

        Task<List<StatusMessage>> TestChallenge(ILog log, ManagedCertificate managedCertificate, bool isPreviewMode, IProgress<RequestProgressState> progress = null);

        Task<List<DnsZone>> GetDnsProviderZones(string providerTypeId, string credentialsId);
        
        Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate);

        Task<CertificateRequestResult> PerformDummyCertificateRequest(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null);

        Task<bool> AddRegisteredContact(ContactRegistration reg);

        void RemoveAllContacts();

        Task<List<BindingInfo>> GetPrimaryWebSites(bool ignoreStoppedSites);

        void BeginTrackingProgress(RequestProgressState state);

        Task<CertificateRequestResult> DeployCertificate(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null, bool isPreviewOnly = false);

        Task<CertificateRequestResult> PerformCertificateRequest(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null, bool resumePaused = false);

        Task<List<DomainOption>> GetDomainOptionsFromSite(string siteId);

        Task<List<CertificateRequestResult>> PerformRenewalAllManagedCertificates(bool autoRenewalOnly = true, Dictionary<string, Progress<RequestProgressState>> progressTrackers = null);

        RequestProgressState GetRequestProgressState(string managedItemId);

        Task<bool> PerformPeriodicTasks();

        Task<bool> PerformDailyTasks();

        Task PerformCertificateCleanup();

        Task<List<ActionStep>> GeneratePreview(ManagedCertificate item);

        event Action<RequestProgressState> OnRequestProgressStateUpdated;

        event Action<ManagedCertificate> OnManagedCertificateUpdated;

        void ReportProgress(IProgress<RequestProgressState> progress, RequestProgressState state, bool logThisEvent = true);
    }
}
