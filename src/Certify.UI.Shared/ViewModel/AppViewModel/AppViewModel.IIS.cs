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
        internal async Task<List<BindingInfo>> GetServerSiteList(StandardServerTypes serverType)
        {
            return await _certifyClient.GetServerSiteList(serverType);
        }

        /// <summary>
        /// check if Server type (e.g. IIS) is available, if so also populates IISVersion 
        /// </summary>
        /// <param name="serverType"></param>
        /// <returns></returns>
        public async Task<bool> CheckServerAvailability(StandardServerTypes serverType)
        {
            IsIISAvailable = await _certifyClient.IsServerAvailable(serverType);

            if (IsIISAvailable)
            {
                IISVersion = await _certifyClient.GetServerVersion(serverType);
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
            return await _certifyClient.GetServerSiteDomains(serverType, siteId);
        }

    }
}
