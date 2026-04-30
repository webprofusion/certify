using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.UI.ViewModel
{
    public partial class AppViewModel : BindableBase
    {
        /// <summary>
        /// If true, IIS was detected on the machine where the service is running
        /// </summary>
        public virtual bool IsIISAvailable { get; set; }

        /// <summary>
        /// Version of IIS currently detected where service is running
        /// </summary>
        public virtual Version IISVersion { get; set; }

        /// <summary>
        /// For a given server type (IIS etc) return list of sites detected where service is running
        /// </summary>
        /// <param name="serverType"></param>
        /// <returns></returns>
        internal async Task<List<SiteInfo>> GetServerSiteList(StandardServerTypes serverType)
        {
            if (!TryGetAvailableCertifyClient(out var client))
            {
                return [];
            }

            return await client.GetServerSiteList(serverType);
        }

        /// <summary>
        /// check if Server type (e.g. IIS) is available, if so also populates IISVersion 
        /// </summary>
        /// <param name="serverType"></param>
        /// <returns></returns>
        public async Task<bool> CheckServerAvailability(StandardServerTypes serverType)
        {
            if (!TryGetAvailableCertifyClient(out var client))
            {
                IsIISAvailable = false;
                IISVersion = null;

                RaisePropertyChangedEvent(nameof(IISVersion));
                RaisePropertyChangedEvent(nameof(ShowIISWarning));

                return false;
            }

            IsIISAvailable = await client.IsServerAvailable(serverType);

            if (IsIISAvailable)
            {
                IISVersion = await client.GetServerVersion(serverType);
            }

            RaisePropertyChangedEvent(nameof(IISVersion));
            RaisePropertyChangedEvent(nameof(ShowIISWarning));

            return IsIISAvailable;
        }

        /// <summary>
        /// If an IIS Version is present and it is lower than v8.0 the SNI is not supported and
        /// limitations apply
        /// </summary>
        public bool ShowIISWarning
        {
            get
            {
                if (IsIISAvailable && IISVersion?.Major < 8)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// For a given server type and service identifier, return list of domain options (identifiers) currently in use.
        /// </summary>
        /// <param name="serverType"></param>
        /// <param name="siteId"></param>
        /// <returns></returns>
        internal async Task<List<DomainOption>> GetServerSiteDomains(StandardServerTypes serverType, string siteId)
        {
            if (!TryGetAvailableCertifyClient(out var client))
            {
                return [];
            }

            return await client.GetServerSiteDomains(serverType, siteId);
        }
    }
}
