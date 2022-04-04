#nullable disable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Certify.Models.Providers
{

	public class ServerTypeInfo {
		public StandardServerTypes ServerType { get; set; } = StandardServerTypes.Other;
		public string Title { get; set; } = String.Empty;
	}

    /// <summary>
    /// An example certified server would be an IIS server 
    /// </summary>
    public interface ITargetWebServer : IDisposable
    {
        Task<List<BindingInfo>> GetSiteBindingList(
            bool ignoreStoppedSites,
            string siteId = null
            );

        Task<List<SiteInfo>> GetPrimarySites(bool ignoreStoppedSites);

        Task<SiteInfo> GetSiteById(string siteId);

        Task RemoveHttpsBinding(string siteId, string sni);

        Task<Version> GetServerVersion();

        Task<bool> IsAvailable();

        Task<bool> IsSiteRunning(string id);

        IBindingDeploymentTarget GetDeploymentTarget();

        Task<List<ActionStep>> RunConfigurationDiagnostics(string siteId);

        void Init(ILog log, string configRoot = null);

		ServerTypeInfo GetServerTypeInfo();
    }
}
