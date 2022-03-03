using Certify.Models;
using Certify.Models.Config;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Certify.Models.Shared.Validation
{
    public class CertificateDomainsService
    {
        public static async Task<ActionResult<ManagedCertificate>> PopulateFromSiteInfo(ManagedCertificate managedCertificate, SiteInfo SelectedWebSite, IEnumerable<DomainOption> domainOptions)
        {
            // 
            if (SelectedWebSite != null)
            {
                if (managedCertificate.GroupId != SelectedWebSite.Id)
                {
                    // update website association
                    managedCertificate.GroupId = SelectedWebSite.Id;

                    // if not already set, use website name as default name
                    if (managedCertificate.Id == null || string.IsNullOrEmpty(managedCertificate.Name))
                    {
                        if (!string.IsNullOrEmpty(SelectedWebSite.Name))
                        {
                            managedCertificate.Name = SelectedWebSite.Name;
                        }
                    }

                    // remove domain options not manually added
                    foreach (var d in managedCertificate.DomainOptions.ToList())
                    {
                        if (!d.IsManualEntry)
                        {
                            managedCertificate.DomainOptions.Remove(d);
                        }
                    }

                   
                    foreach (var option in domainOptions)
                    {
                        managedCertificate.DomainOptions.Add(option);
                    }

                    if (!managedCertificate.DomainOptions.Any())
                    {
                        return new ActionResult<ManagedCertificate>("The selected site has no domain bindings setup. Configure the domains first using by editing bindings in your web server configuration (IIS etc).", false);
                    }
                }

            }

            return new ActionResult<ManagedCertificate>("OK", true, managedCertificate);

        }


    }
}
