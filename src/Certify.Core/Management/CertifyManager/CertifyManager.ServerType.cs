using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        public async Task<List<BindingInfo>> GetPrimaryWebSites(bool ignoreStoppedSites) => await _serverProvider.GetPrimarySites(ignoreStoppedSites);

        public async Task<List<DomainOption>> GetDomainOptionsFromSite(string siteId)
        {
            var defaultNoDomainHost = "";
            var domainOptions = new List<DomainOption>();

            var matchingSites =
                await _serverProvider.GetSiteBindingList(CoreAppSettings.Current.IgnoreStoppedSites, siteId);
            var siteBindingList = matchingSites.Where(s => s.SiteId == siteId);

            var includeEmptyHostnameBindings = false;

            foreach (var siteDetails in siteBindingList)
            {
                //if domain not currently in the list of options, add it
                if (!domainOptions.Any(item => item.Domain == siteDetails.Host))
                {
                    var opt = new DomainOption
                    {
                        Domain = siteDetails.Host,
                        IsPrimaryDomain = false,
                        IsSelected = true,
                        Title = ""
                    };

                    if (string.IsNullOrWhiteSpace(opt.Domain))
                    {
                        //binding has no hostname/domain set - user will need to specify
                        opt.Title = defaultNoDomainHost;
                        opt.Domain = defaultNoDomainHost;
                        opt.IsManualEntry = true;
                    }
                    else
                    {
                        opt.Title = siteDetails.Protocol + "://" + opt.Domain;
                    }

                    if (siteDetails.IP != null && siteDetails.IP != "0.0.0.0")
                    {
                        opt.Title += " : " + siteDetails.IP;
                    }

                    if (!opt.IsManualEntry || (opt.IsManualEntry && includeEmptyHostnameBindings))
                    {
                        domainOptions.Add(opt);
                    }
                }
            }

            //TODO: if one or more binding is to a specific IP, how to manage in UI?

            if (domainOptions.Any(d => !string.IsNullOrEmpty(d.Domain)))
            {
                // mark first domain as primary, if we have no other settings
                if (!domainOptions.Any(d => d.IsPrimaryDomain == true))
                {
                    var electableDomains = domainOptions.Where(d =>
                        !string.IsNullOrEmpty(d.Domain) && d.Domain != defaultNoDomainHost);
                    if (electableDomains.Any())
                    {
                        // promote first domain in list to primary by default
                        electableDomains.First().IsPrimaryDomain = true;
                    }
                }
            }

            return domainOptions.OrderByDescending(d => d.IsPrimaryDomain).ThenBy(d => d.Domain).ToList();
        }

        public async Task<bool> IsServerTypeAvailable(StandardServerTypes serverType)
        {
            if (serverType == StandardServerTypes.IIS)
            {
                if (_useWindowsNativeFeatures)
                {
                    return await _serverProvider.IsAvailable();
                }
                else
                {
                    // non-windows platform
                    return false;
                }
            }

            return false;
        }

        public async Task<Version> GetServerTypeVersion(StandardServerTypes serverType) => await _serverProvider.GetServerVersion();

        public async Task<List<ActionStep>> RunServerDiagnostics(StandardServerTypes serverType, string siteId) => await _serverProvider.RunConfigurationDiagnostics(siteId);
    }
}
