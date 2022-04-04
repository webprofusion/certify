using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Service
{
	[ApiController]
	[Route("api/server")]
	public class ServerController : Controllers.ControllerBase
	{
		private ICertifyManager _certifyManager;

		public ServerController(Management.ICertifyManager manager)
		{
			_certifyManager = manager;
		}

		[HttpGet, Route("isavailable/{serverType}")]
		public async Task<bool> IsServerAvailable(StandardServerTypes serverType)
		{
			DebugLog();

			if (serverType == StandardServerTypes.IIS)
			{
				return await _certifyManager.IsServerTypeAvailable(serverType);
			}
			else
			{
				return false;
			}
		}

		[HttpGet, Route("sitelist/{serverType}")]
		public async Task<List<SiteInfo>> GetServerSiteList(StandardServerTypes serverType)
		{

			return await _certifyManager.GetPrimaryWebSites(serverType, Management.CoreAppSettings.Current.IgnoreStoppedSites);

		}


		[HttpGet, Route("sitelist/{serverType}/{itemId?}")]
		public async Task<List<SiteInfo>> GetServerSiteList(StandardServerTypes serverType, string itemId = null)
		{
			return await _certifyManager.GetPrimaryWebSites(serverType, Management.CoreAppSettings.Current.IgnoreStoppedSites, itemId);
		}

		[HttpGet, Route("sitedomains/{serverType}/{serverSiteId}")]
		public async Task<List<DomainOption>> GetServerSiteDomainOptions(StandardServerTypes serverType, string serverSiteId)
		{
			return await _certifyManager.GetDomainOptionsFromSite(serverType, serverSiteId);

		}

		[HttpGet, Route("version/{serverType}")]
		public async Task<string> GetServerVersion(StandardServerTypes serverType)
		{
			if (serverType == StandardServerTypes.IIS)
			{
				var version = await _certifyManager.GetServerTypeVersion(serverType);
				return version.ToString();
			}
			else
			{
				return null;
			}
		}

		[HttpGet, Route("diagnostics/{serverType}/{siteId?}")]
		public async Task<List<ActionStep>> RunServerDiagnostics(StandardServerTypes serverType, string siteId)
		{
			if (serverType == StandardServerTypes.IIS)
			{
				return await _certifyManager.RunServerDiagnostics(serverType, siteId);
			}
			else
			{
				return null;
			}
		}
	}
}
