using System;
using System.Collections.Generic;
using System.Linq;
using Certify.Locales;
using Certify.Models.Config;

namespace Certify.Models.Shared.Validation
{
    public enum ValidationErrorCodes
    {
        NONE,
        PRIMARY_IDENTIFIER_REQUIRED,
        PRIMARY_IDENTIFIER_TOOMANY,
        CHALLENGE_TYPE_INVALID,
        REQUIRED_NAME,
        INVALID_HOSTNAME,
        REQUIRED_CHALLENGE_CONFIG_PARAM,
        SAN_LIMIT
    }

    public class CertificateEditorService
    {

        /// <summary>
        /// For a given website and list of domains, populate the Domain Options of the managed certificate
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <param name="selectedTargetSite"></param>
        /// <param name="domainOptions"></param>
        /// <returns></returns>
        public static ActionResult<ManagedCertificate> PopulateFromSiteInfo(ManagedCertificate managedCertificate, SiteInfo selectedTargetSite, IEnumerable<DomainOption> domainOptions)
        {
            // 
            if (selectedTargetSite != null && managedCertificate != null)
            {
                if (managedCertificate.GroupId != selectedTargetSite.Id)
                {
                    // update website association
                    managedCertificate.GroupId = selectedTargetSite.Id;

                    // if not already set, use website name as default name
                    if (managedCertificate.Id == null || string.IsNullOrEmpty(managedCertificate.Name))
                    {
                        if (!string.IsNullOrEmpty(selectedTargetSite.Name))
                        {
                            managedCertificate.Name = selectedTargetSite.Name;
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

            return new ActionResult<ManagedCertificate>("OK", true, result: managedCertificate);

        }

        /// <summary>
        /// Check the currently selected options and auto set where we can, transpose selected identifier options to update the final request configuration
        /// </summary>
        /// <returns></returns>
        public static void ApplyAutoConfiguration(ManagedCertificate item, SiteInfo selectedTargetSite)
        {

            var config = item.RequestConfig;

            // if no primary identifier is selected then we need to attempt to default to one
            if (!item.DomainOptions.Any(d => d.IsPrimaryDomain) && item.DomainOptions.Any(d => d.IsSelected))
            {
                var o = item.DomainOptions.FirstOrDefault(d => d.IsSelected == true);
                if (o != null)
                {
                    o.IsPrimaryDomain = true;
                }
            }

            // requests with a primary domain need to set the primary domain in the request config
            var primaryDomain = item.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain == true && d.Type == "dns");

            if (primaryDomain != null)
            {
                // update request config primary identifier
                if (config.PrimaryDomain != primaryDomain.Domain)
                {
                    config.PrimaryDomain = primaryDomain?.Domain?.Trim();
                }
            }

            //apply remaining selected domains as subject alternative names
            var sanList =
                item.DomainOptions.Where(dm => dm.IsSelected && dm.Type == "dns" && dm.Domain != null)
                .Select(i => i.Domain ?? string.Empty)
                .ToArray();

            if (config.SubjectAlternativeNames == null ||
                !sanList.SequenceEqual(config.SubjectAlternativeNames))
            {
                config.SubjectAlternativeNames = sanList;
            }

            // update our list of selected subject ip addresses, if any
            if (!config.SubjectIPAddresses.SequenceEqual(item.DomainOptions.Where(i => i.IsSelected && i.Type == "ip").Select(s => s.Domain).ToArray()))
            {

                config.SubjectIPAddresses = item.DomainOptions.Where(i => i.IsSelected && i.Type == "ip" && i.Domain != null)
                                                              .Select(s => s.Domain ?? string.Empty)
                                                              .ToArray();
            }

            //determine if this site has an existing entry in Managed Certificates, if so use that, otherwise start a new one
            if (item.Id == null)
            {
                item.Id = Guid.NewGuid().ToString();

                item.ItemType = ManagedCertificateType.SSL_ACME;

                // optionally set webserver site ID (if used)
                if (selectedTargetSite != null && !string.IsNullOrEmpty(selectedTargetSite.Id))
                {
                    item.GroupId = selectedTargetSite.Id;
                }
            }

            if (item.RequestConfig.Challenges == null)
            {
                item.RequestConfig.Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>();
            }

            if (item.RequestConfig.PerformAutomatedCertBinding)
            {
                item.RequestConfig.BindingIPAddress = null;
                item.RequestConfig.BindingPort = null;
                item.RequestConfig.BindingUseSNI = null;
            }
            else
            {
                //always select Use SNI unless it's specifically set to false
                if (item.RequestConfig.BindingUseSNI == null)
                {
                    item.RequestConfig.BindingUseSNI = true;
                }
            }
        }

        public static DomainOption? GetPrimarySubjectDomain(ManagedCertificate item)
        {
            return item?.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain);
        }
        public static void SetPrimarySubjectDomain(ManagedCertificate item, DomainOption opt)
        {
            // mark the matching domain option as the primary domain (common name or primary subject), mark anything else as not primary.
            foreach (var o in item.DomainOptions)
            {
                if (!o.IsPrimaryDomain && o.Domain == opt.Domain && o.Type == opt.Type)
                {
                    o.IsPrimaryDomain = true;
                    o.IsSelected = true;
                }
                else if (o.IsPrimaryDomain && !(o.Domain == opt.Domain && o.Type == opt.Type))
                {
                    o.IsPrimaryDomain = false;
                }
            }
        }
        /// <summary>
        /// Add set of domains as domain options froma given string
        /// </summary>
        /// <param name="domains"></param>
        /// <returns>return true if one or more items was a wildcard</returns>
        public static (string[] domainList, bool wildcardAdded) AddDomainOptionsFromString(ManagedCertificate item, string domains)
        {
            var wildcardAdded = false;

            // parse text input to add as manual domain options

            var domainList = Array.Empty<string>();

            if (!string.IsNullOrEmpty(domains))
            {
                domainList = domains.Split(",; ".ToCharArray());
                var invalidDomains = "";

                foreach (var d in domainList)
                {
                    if (!string.IsNullOrEmpty(d.Trim()))
                    {
                        var domain = d.ToLower().Trim();
                        if (domain != null && !item.DomainOptions.Any(o => o.Domain == domain))
                        {
                            var option = new DomainOption
                            {
                                Domain = domain,
                                IsManualEntry = true,
                                IsSelected = true
                            };

                            if (Uri.CheckHostName(domain) == UriHostNameType.Dns || (domain.StartsWith("*.", StringComparison.InvariantCultureIgnoreCase) && Uri.CheckHostName(domain.Replace("*.", "")) == UriHostNameType.Dns))
                            {
                                // preselect first item as primary domain
                                if (item.DomainOptions.Count == 0)
                                {
                                    option.IsPrimaryDomain = true;
                                }

                                item.DomainOptions.Add(option);

                                if (option.Domain.StartsWith("*.", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    wildcardAdded = true;
                                }
                            }
                            else if (Uri.CheckHostName(domain) == UriHostNameType.IPv4 || Uri.CheckHostName(domain) == UriHostNameType.IPv6)
                            {
                                option.Type = "ip";
                                // add an IP address instead of a domain
                                if (item.DomainOptions.Count == 0)
                                {
                                    option.IsPrimaryDomain = true;
                                }

                                item.DomainOptions.Add(option);
                            }
                            else
                            {
                                invalidDomains += domain + "\n";
                            }
                        }
                    }
                }
            }

            return (domainList, wildcardAdded);
        }

        public static ValidationResult Validate(ManagedCertificate item, SiteInfo selectedTargetSite, CertificateAuthority preferredCA, bool applyAutoConfiguration)
        {
            try
            {
                // too many domains options selected as primary (UI bug)
                if (item.DomainOptions?.Count(d => d.IsPrimaryDomain) > 1)
                {
                    return new ValidationResult(false, "There can only be one domain selected as the primary domain.", ValidationErrorCodes.PRIMARY_IDENTIFIER_TOOMANY.ToString());
                }

                if (applyAutoConfiguration)
                {
                    CertificateEditorService.ApplyAutoConfiguration(item, selectedTargetSite);
                }

                if (string.IsNullOrEmpty(item.Name))
                {

                    return new ValidationResult(false, SR.ManagedCertificateSettings_NameRequired, ValidationErrorCodes.REQUIRED_NAME.ToString());
                }

                // a primary subject domain must be set
                if (GetPrimarySubjectDomain(item) == null)
                {
                    // if we still can't decide on the primary domain ask user to define it
                    return new ValidationResult(
                        false,
                        SR.ManagedCertificateSettings_NeedPrimaryDomain,
                        ValidationErrorCodes.PRIMARY_IDENTIFIER_REQUIRED.ToString()
                    );
                }

                if (!(preferredCA != null && preferredCA.AllowInternalHostnames))
                {
                    // validate hostnames
                    if (item.DomainOptions?.Any(d => d.IsSelected && d.Type == "dns" && d.Domain != null && (!d.Domain.Contains(".") || d.Domain.ToLower().EndsWith(".local", StringComparison.InvariantCultureIgnoreCase))) == true)
                    {
                        // one or more selected domains does not include a label separator (is an internal host name) or end in .local

                        return new ValidationResult(
                            false,
                            "One or more domains specified are internal hostnames. Certificates for internal host names are not supported by the Certificate Authority.",
                            ValidationErrorCodes.INVALID_HOSTNAME.ToString()
                        );
                    }
                }

                // if title still set to the default, automatically use the primary domain instead
                if (item.Name == SR.ManagedCertificateSettings_DefaultTitle)
                {
                    item.Name = GetPrimarySubjectDomain(item)?.Domain;
                }

                // certificates cannot request wildcards unless they also use DNS validation
                if (
                    item.DomainOptions?.Any(d => d.IsSelected && d.Domain != null && d.Domain.StartsWith("*.", StringComparison.InvariantCultureIgnoreCase)) == true
                    &&
                    !item.RequestConfig.Challenges.Any(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
                    )
                {
                    return new ValidationResult(
                        false,
                        "Wildcard domains cannot use http-01 validation for domain authorization. Use dns-01 instead.",
                        ValidationErrorCodes.CHALLENGE_TYPE_INVALID.ToString()
                    );
                }

                // TLS-SNI-01 (is now not supported)
                if (item.RequestConfig.Challenges.Any(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI))
                {
                    return new ValidationResult(
                        false,
                        "The tls-sni-01 challenge type is no longer available. You need to switch to either http-01 or dns-01.",
                        ValidationErrorCodes.CHALLENGE_TYPE_INVALID.ToString()
                    );
                }

                if (item.RequestConfig.Challenges.Any(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS && string.IsNullOrEmpty(c.ChallengeProvider)))
                {
                    return new ValidationResult(
                        false,
                        "The dns-01 challenge type requires a DNS Update Method selection.",
                        ValidationErrorCodes.CHALLENGE_TYPE_INVALID.ToString()
                    );
                }

                if (item.RequestConfig.Challenges.Count(c => string.IsNullOrEmpty(c.DomainMatch)) > 1)
                {
                    return new ValidationResult(
                       false,
                       "Only one authorization configuration can be used which matches any domain (domain match blank). Specify domain(s) to match or remove additional configuration. ",
                       ValidationErrorCodes.CHALLENGE_TYPE_INVALID.ToString()
                    );

                    // TODO: error if any selected domains will not be matched or if an config will not be used
                }

                // validate settings for authorization config non-optional parameters

                if (item.RequestConfig.Challenges?.Any() != true)
                {
                    return new ValidationResult(
                                   false,
                                   $"One or more challenge configurations required",
                                   ValidationErrorCodes.REQUIRED_CHALLENGE_CONFIG_PARAM.ToString()
                                );
                }

                foreach (var c in item.RequestConfig.Challenges)
                {
                    if (c.Parameters != null && c.Parameters.Any())
                    {
                        //validate parameters
                        foreach (var p in c.Parameters)
                        {
                            if (p.IsRequired && string.IsNullOrEmpty(p.Value))
                            {
                                return new ValidationResult(
                                   false,
                                   $"Challenge configuration parameter required: {p.Name}",
                                   ValidationErrorCodes.REQUIRED_CHALLENGE_CONFIG_PARAM.ToString()
                                );
                            }
                        }
                    }
                }

                // check certificate will not exceed 100 name limit. TODO: make this dynamic per selected CA
                var numSelectedDomains = item.DomainOptions.Count(d => d.IsSelected);

                if (numSelectedDomains > 100)
                {
                    return new ValidationResult(
                                 false,
                                 $"Certificates cannot include more than 100 names. You will need to remove names or split your certificate into 2 or more managed certificates.",
                                 ValidationErrorCodes.SAN_LIMIT.ToString()
                              );
                }

                // no problems found
                return new ValidationResult(true, "OK", ValidationErrorCodes.NONE.ToString());
            }
            catch (Exception exp)
            {

                // unexpected error while checking
                return new ValidationResult(false, exp.ToString());
            }
        }
    }
}
