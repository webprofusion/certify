using Certify.Models;
using Certify.Models.Providers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Certify.Management
{
    public interface ICertifyManager
    {
        Task<bool> IsServerTypeAvailable(StandardServerTypes serverType);

        Task<Version> GetServerTypeVersion(StandardServerTypes serverType);

        Task<bool> LoadSettingsAsync(bool skipIfLoaded);

        Task<ManagedCertificate> GetManagedCertificate(string id);

        Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter filter = null);

        Task<ManagedCertificate> UpdateManagedCertificate(ManagedCertificate site);

        Task DeleteManagedCertificate(string id);

        List<RegistrationItem> GetContactRegistrations();

        string GetPrimaryContactEmail();

        Task<List<StatusMessage>> TestChallenge(ILog log, ManagedCertificate managedCertificate, bool isPreviewMode);

        Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate);

        Task<CertificateRequestResult> PerformDummyCertificateRequest(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null);

        Task<bool> AddRegisteredContact(ContactRegistration reg);

        void RemoveAllContacts();

        List<SiteBindingItem> GetPrimaryWebSites(bool ignoreStoppedSites);

        void BeginTrackingProgress(RequestProgressState state);

        Task<CertificateRequestResult> ReapplyCertificateBindings(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null, bool isPreviewOnly = false);

        Task<CertificateRequestResult> PerformCertificateRequest(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null);

        List<DomainOption> GetDomainOptionsFromSite(string siteId);

        Task<List<CertificateRequestResult>> PerformRenewalAllManagedCertificates(bool autoRenewalOnly = true, Dictionary<string, Progress<RequestProgressState>> progressTrackers = null);

        Task<List<ManagedCertificate>> PreviewManagedCertificates(StandardServerTypes serverType);

        RequestProgressState GetRequestProgressState(string managedItemId);

        Task<bool> PerformPeriodicTasks();

        Task<bool> PerformDailyTasks();

        event Action<RequestProgressState> OnRequestProgressStateUpdated;

        event Action<ManagedCertificate> OnManagedCertificateUpdated;

        Task<List<ActionStep>> GeneratePreview(ManagedCertificate item);
    }
}