using Certify.Models;
using Serilog;
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

        Task<ManagedSite> GetManagedSite(string id);

        Task<List<ManagedSite>> GetManagedSites(ManagedSiteFilter filter = null);

        Task<ManagedSite> UpdateManagedSite(ManagedSite site);

        Task DeleteManagedSite(string id);

        List<RegistrationItem> GetContactRegistrations();

        string GetPrimaryContactEmail();

        List<CertificateItem> GetCertificates();

        Task<StatusMessage> TestChallenge(ILogger log, ManagedSite managedSite, bool isPreviewMode);

        Task<StatusMessage> RevokeCertificate(ManagedSite managedSite);

        Task<CertificateRequestResult> PerformDummyCertificateRequest(ManagedSite managedSite, IProgress<RequestProgressState> progress = null);

        Task<bool> AddRegisteredContact(ContactRegistration reg);

        void RemoveAllContacts();

        List<SiteBindingItem> GetPrimaryWebSites(bool ignoreStoppedSites);

        void BeginTrackingProgress(RequestProgressState state);

        Task<CertificateRequestResult> ReapplyCertificateBindings(ManagedSite managedSite, IProgress<RequestProgressState> progress = null, bool isPreviewOnly = false);

        Task<CertificateRequestResult> PerformCertificateRequest(ManagedSite managedSite, IProgress<RequestProgressState> progress = null);

        List<DomainOption> GetDomainOptionsFromSite(string siteId);

        Task<List<CertificateRequestResult>> PerformRenewalAllManagedSites(bool autoRenewalOnly = true, Dictionary<string, Progress<RequestProgressState>> progressTrackers = null);

        Task<List<ManagedSite>> PreviewManagedSites(StandardServerTypes serverType);

        RequestProgressState GetRequestProgressState(string managedSiteId);

        Task<bool> PerformPeriodicTasks();

        Task<bool> PerformDailyTasks();

        event Action<RequestProgressState> OnRequestProgressStateUpdated;

        event Action<ManagedSite> OnManagedSiteUpdated;
    }
}