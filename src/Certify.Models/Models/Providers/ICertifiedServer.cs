using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Certify.Models.Providers
{
    /// <summary>
    /// An example certified server would be an IIS server 
    /// </summary>
    public interface ICertifiedServer
    {
        Task<List<BindingInfo>> GetSiteBindingList(
            bool ignoreStoppedSites,
            string siteId = null
            );

        Task<List<ActionStep>> InstallCertForRequest(
            ManagedCertificate managedCertificate,
            string pfxPath,
            bool cleanupCertStore,
            bool isPreviewOnly
            );

        Task<List<ActionStep>> InstallCertificateforBinding(
            string certStoreName,
            byte[] certificateHash,
            ManagedCertificate managedCertificate,
            string host,
            int sslPort = 443,
            bool useSNI = true,
            string ipAddress = null,
            bool alwaysRecreateBindings = false,
            bool isPreviewOnly = false
            );

        Task<List<BindingInfo>> GetPrimarySites(bool ignoreStoppedSites);

        Task<SiteInfo> GetSiteById(string siteId);

        Task RemoveHttpsBinding(ManagedCertificate managedCertificate, string sni);

        Task<Version> GetServerVersion();

        Task<bool> IsAvailable();

        Task<bool> IsSiteRunning(string id);
    }
}