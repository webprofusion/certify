using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Certify.Config;
using Newtonsoft.Json;

namespace Certify.Models
{
    public enum ManagedCertificateType
    {
        SSL_ACME = 1,
        SSL_Manual = 2,
        SSL_ExternallyManaged = 3
    }

    public enum RequiredActionType
    {
        NewCertificate,
        ReplaceCertificate,
        KeepCertificate,
        Ignore
    }

    public enum ManagedCertificateHealth
    {
        Unknown,
        OK,
        AwaitingUser,
        Warning,
        Error
    }

    public class ManagedCertificate : BindableBase
    {
        public ManagedCertificate()
        {
            Name = "New Managed Certificate";
            IncludeInAutoRenew = true;

            DomainOptions = new ObservableCollection<DomainOption>();
            RequestConfig = new CertRequestConfig();

            IncludeInAutoRenew = true;

#if DEBUG
            UseStagingMode = true;
#else
            UseStagingMode = false;
#endif
        }

        /// <summary>
        /// If set, managed item is from an external source
        /// </summary>
        public string? SourceId { get; set; }
        public string? SourceName { get; set; }

        /// <summary>
        /// Default CA to use for this request
        /// </summary>
        public string? CertificateAuthorityId { get; set; }

        /// <summary>
        /// If true, the staging (test) API and account key will be used for orders
        /// </summary>
        public bool UseStagingMode { get; set; }

        /// <summary>
        /// If true, the auto renewal process will include this item in attempted renewal operations
        /// if applicable
        /// </summary>
        public bool IncludeInAutoRenew { get; set; }

        /// <summary>
        /// List of configured domains this managed site will include (primary subject or SAN)
        /// </summary>
        public ObservableCollection<DomainOption> DomainOptions { get; set; }

        /// <summary>
        /// Configuration options for this request
        /// </summary>
        public CertRequestConfig RequestConfig { get; set; }

        /// <summary>
        /// Optional list of tasks (scripts, webhooks etc) to perform after request/renewal or on demand
        /// </summary>
        public ObservableCollection<DeploymentTaskConfig>? PreRequestTasks { get; set; }

        /// <summary>
        /// Optional list of deployment tasks to perform after request/renewal or on demand
        /// </summary>
        public ObservableCollection<DeploymentTaskConfig>? PostRequestTasks { get; set; }

        /// <summary>
        /// Unique ID for this managed item
        /// </summary>
        public string? Id { get; set; }
        public long Version { get; set; }

        /// <summary>
        /// Deprecated, use Server Site Id
        /// </summary>

        public string? GroupId { get; set; }

        /// <summary>
        /// Id of specific matching site on server (replaces GroupId)
        /// </summary>
        public string? ServerSiteId { get => GroupId; set => GroupId = value; }

        /// <summary>
        /// If set, this is an identifier for the host to group multiple sets of managed sites across servers
        /// </summary>
        public string? InstanceId { get; set; }

        /// <summary>
        /// Display name for this item, for easier reference
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Optional user notes regarding this item
        /// </summary>
        public string? Comments { get; set; }

        /// <summary>
        /// Specific type of item we are managing, affects the renewal/rewuest operations required
        /// </summary>
        public ManagedCertificateType ItemType { get; set; }

        public DateTime? DateStart { get; set; }
        public DateTime? DateExpiry { get; set; }
        public DateTime? DateRenewed { get; set; }

        /// <summary>
        /// Date we last check the OCSP status for this cert
        /// </summary>
        public DateTime? DateLastOcspCheck { get; set; }

        /// <summary>
        /// Date we last checked the CA renewal info (ARI), if available
        /// </summary>
        public DateTime? DateLastRenewalInfoCheck { get; set; }

        /// <summary>
        /// Date we last attempted renewal
        /// </summary>
        public DateTime? DateLastRenewalAttempt { get; set; }

        /// <summary>
        /// Status of most recent renewal attempt
        /// </summary>
        public RequestState? LastRenewalStatus { get; set; }

        /// <summary>
        /// Count of renewal failures since last success
        /// </summary>
        public int RenewalFailureCount { get; set; }

        /// <summary>
        /// Message from last failed renewal attempt
        /// </summary>
        public string? RenewalFailureMessage { get; set; }

        public string? CertificateId { get; set; }
        public string? CertificatePath { get; set; }
        public string? CertificateFriendlyName { get; set; }
        public string? CertificateThumbprintHash { get; set; }
        public string? CertificatePreviousThumbprintHash { get; set; }
        public bool CertificateRevoked { get; set; }

        /// <summary>
        /// Optional stored credential ID for preferred PFX password (pwd is blank otherwise)
        /// </summary>
        public string? CertificatePasswordCredentialId { get; set; }

        public string? CurrentOrderUri { get; set; }

        /// <summary>
        /// If true, pre/post request tasks will run for renewal but the certificate order won't be performed (used for testing).
        /// </summary>
        public bool? SkipCertificateRequest { get; set; } = false;

        public override string ToString() => $"[{Id ?? "null"}]: \"{Name}\"";

        [JsonIgnore]
        public bool Deleted { get; set; } // do not serialize to settings

        [JsonIgnore]
        public ManagedCertificateHealth Health
        {
            get
            {
                if (LastRenewalStatus == RequestState.Error)
                {
                    if (RenewalFailureCount > 3 || DateExpiry < DateTime.Now.AddHours(12))
                    {
                        return ManagedCertificateHealth.Error;
                    }
                    else
                    {
                        return ManagedCertificateHealth.Warning;
                    }
                }
                else
                {
                    if (LastRenewalStatus != null)
                    {
                        if (LastRenewalStatus.Value == RequestState.Paused)
                        {
                            return ManagedCertificateHealth.AwaitingUser;
                        }
                        else
                        {
                            if (CertificateRevoked)
                            {
                                return ManagedCertificateHealth.Error;
                            }
                            else
                            {
                                // if cert is otherwise OK but is expiring soon, report health as warning or error (expired)
                                if (DateExpiry < DateTime.Now.AddHours(12))
                                {
                                    return ManagedCertificateHealth.Error;
                                }
                                else if (DateExpiry < DateTime.Now.AddDays(14))
                                {
                                    return ManagedCertificateHealth.Warning;
                                }
                                else
                                {
                                    return ManagedCertificateHealth.OK;
                                }
                            }
                        }
                    }
                    else
                    {
                        return ManagedCertificateHealth.Unknown;
                    }
                }
            }
        }

        /// <summary>
        /// Get distinct list of certificate domains/hostnames for this managed cert
        /// </summary>
        /// <returns></returns>
        public List<string> GetCertificateDomains()
        {
            var allDomains = new List<string>();

            if (RequestConfig != null)
            {
                if (!string.IsNullOrEmpty(RequestConfig.PrimaryDomain))
                {
#pragma warning disable CS8604 // Possible null reference argument.
                    allDomains.Add(RequestConfig.PrimaryDomain);
#pragma warning restore CS8604 // Possible null reference argument.
                }

                if (RequestConfig.SubjectAlternativeNames != null)
                {
                    allDomains.AddRange(RequestConfig.SubjectAlternativeNames);
                }
            }

            return allDomains.Distinct().ToList();

        }

        /// <summary>
        /// For the given challenge config and list of domains, return subset of domains which will
        /// be matched against the config (considering all other configs)
        /// </summary>
        /// <param name="config">  </param>
        /// <param name="domains">  </param>
        /// <returns>  </returns>
        public List<string> GetChallengeConfigDomainMatches(CertRequestChallengeConfig config, IEnumerable<string> domains)
        {
            var matches = new List<string>();
            foreach (var d in domains)
            {
                var matchedConfig = GetChallengeConfig(d);
                if (matchedConfig == config)
                {
                    matches.Add(d);
                }
            }

            return matches;
        }

        /// <summary>
        /// For the given domain, get the matching challenge config (DNS provider variant etc)
        /// </summary>
        /// <param name="managedCertificate">  </param>
        /// <param name="domain">  </param>
        /// <returns>  </returns>
        public CertRequestChallengeConfig GetChallengeConfig(string domain)
        {
            if (domain != null)
            {
                domain = domain.Trim().ToLower();
            }

            if (RequestConfig.Challenges == null || RequestConfig.Challenges.Count == 0)
            {
                // there are no challenge configs defined return a default based on the parent
                return new CertRequestChallengeConfig
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    ChallengeType = RequestConfig.ChallengeType
#pragma warning restore CS0618 // Type or member is obsolete
                };
            }
            else
            {
                //identify matching challenge config based on domain etc
                if (RequestConfig.Challenges.Count == 1)
                {
                    return RequestConfig.Challenges[0];
                }
                else
                {
                    // start by matching first config with no specific domain
                    var matchedConfig = RequestConfig.Challenges.FirstOrDefault(c => string.IsNullOrEmpty(c.DomainMatch));

                    if (domain != null && !string.IsNullOrEmpty(domain))
                    {
                        // expand configs into per domain list
                        var configsPerDomain = new Dictionary<string, CertRequestChallengeConfig>();
                        foreach (var c in RequestConfig.Challenges.Where(config => !string.IsNullOrEmpty(config.DomainMatch)))
                        {
                            if (c != null)
                            {
                                if (c.DomainMatch != null && !string.IsNullOrEmpty(c.DomainMatch))
                                {
                                    c.DomainMatch = c.DomainMatch.Replace(",", ";"); // if user has entered comma seperators instead of semicolons, convert now.

                                    if (!c.DomainMatch.Contains(";"))
                                    {
                                        var domainMatchKey = c.DomainMatch.Trim();

                                        // if domain key is test.com for example we only support one matching config
                                        if (!configsPerDomain.ContainsKey(domainMatchKey))
                                        {
                                            configsPerDomain.Add(domainMatchKey, c);
                                        }
                                    }
                                    else
                                    {
                                        var domains = c.DomainMatch.Split(';');
                                        foreach (var d in domains)
                                        {
                                            if (!string.IsNullOrWhiteSpace(d))
                                            {
                                                var domainMatchKey = d.Trim().ToLower();
                                                if (!configsPerDomain.ContainsKey(domainMatchKey))
                                                {
                                                    configsPerDomain.Add(domainMatchKey, c);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // if exact match exists, use that
                        if (configsPerDomain.ContainsKey(domain))
                        {
                            return configsPerDomain[domain];
                        }

                        // if explicit wildcard match exists, use that
                        if (configsPerDomain.ContainsKey("*." + domain))
                        {
                            return configsPerDomain["*." + domain];
                        }

                        //if a more specific config matches the domain, use that, in order of longest domain name match first
                        var allMatchingConfigKeys = configsPerDomain.Keys.OrderByDescending(l => l.Length);

                        foreach (var wildcard in allMatchingConfigKeys.Where(k => k.StartsWith("*.", StringComparison.CurrentCultureIgnoreCase)))
                        {
                            if (ManagedCertificate.IsDomainOrWildcardMatch(new List<string> { wildcard }, domain))
                            {
                                return configsPerDomain[wildcard];
                            }
                        }

                        foreach (var configDomain in allMatchingConfigKeys)
                        {
                            if (configDomain.EndsWith(domain, StringComparison.CurrentCultureIgnoreCase))
                            {
                                // use longest matching domain (so subdomain.test.com takes priority
                                // over test.com, )
                                return configsPerDomain[configDomain];
                            }
                        }
                    }

                    // no other matches, just use first
                    if (matchedConfig != null)
                    {
                        return matchedConfig;
                    }
                    else
                    {
                        // no match, return default
                        return new CertRequestChallengeConfig
                        {
#pragma warning disable CS0618 // Type or member is obsolete
                            ChallengeType = RequestConfig.ChallengeType
#pragma warning restore CS0618 // Type or member is obsolete
                        };
                    }
                }
            }
        }

        public ManagedCertificate CopyAsTemplate(bool preserveAttributes = false)
        {

            // clone current object
            var managedCert = JsonConvert.DeserializeObject<ManagedCertificate>(JsonConvert.SerializeObject(this));

            if (managedCert == null)
            {
                return new ManagedCertificate();
            }

            // reset fields we don't want to re-use from the original
            managedCert.Id = Guid.NewGuid().ToString();

            managedCert.DateLastRenewalAttempt = null;
            managedCert.DateStart = null;
            managedCert.DateRenewed = null;
            managedCert.DateExpiry = null;
            managedCert.CertificateThumbprintHash = null;
            managedCert.CertificatePreviousThumbprintHash = null;
            managedCert.CurrentOrderUri = null;
            managedCert.SourceId = null;
            managedCert.SourceName = null;
            managedCert.RenewalFailureCount = 0;
            managedCert.RenewalFailureMessage = null;

            managedCert.LastRenewalStatus = null;
            managedCert.CurrentOrderUri = null;
            managedCert.CertificatePath = null;
            managedCert.CertificateId = null;
            managedCert.CertificateFriendlyName = null;
            managedCert.ItemType = ManagedCertificateType.SSL_ACME;

            if (!preserveAttributes)
            {
                managedCert.RequestConfig.SubjectAlternativeNames = Array.Empty<string>();
                managedCert.RequestConfig.SubjectIPAddresses = Array.Empty<string>();
                managedCert.RequestConfig.PrimaryDomain = string.Empty;
                managedCert.DomainOptions = new System.Collections.ObjectModel.ObservableCollection<DomainOption>();
                managedCert.Name = string.Empty;
            }
            else
            {
                managedCert.Name = $"{managedCert.Name.WithDefault("")} (Copy)";
            }

            if (managedCert.PreRequestTasks != null)
            {
                foreach (var t in managedCert.PreRequestTasks)
                {
                    t.Id = Guid.NewGuid().ToString();
                }
            }

            if (managedCert.PostRequestTasks != null)
            {
                foreach (var t in managedCert.PostRequestTasks)
                {
                    t.Id = Guid.NewGuid().ToString();
                }
            }

            return managedCert;
        }

        /// <summary>
        /// </summary>
        /// <param name="dnsNames">  </param>
        /// <param name="hostname">  </param>
        /// <param name="matchWildcardsToRootDomain">
        /// if true, *.test.com would match test.com (as well as www.test.com)
        /// </param>
        /// <returns>  </returns>
        public static bool IsDomainOrWildcardMatch(List<string> dnsNames, string hostname, bool matchWildcardsToRootDomain = false)
        {
            var isMatch = false;

            if (!string.IsNullOrEmpty(hostname))
            {
                // list of dns anmes has an exact match
                if (dnsNames.Contains(hostname))
                {
                    isMatch = true;
                }
                else
                {
                    //if any of our dnsHosts are a wildcard, check for a match
                    var wildcards = dnsNames.Where(d => d.StartsWith("*.", StringComparison.CurrentCultureIgnoreCase));
                    foreach (var w in wildcards)
                    {
                        if (string.Equals(w, hostname, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                        else
                        {
                            var domain = w.Replace("*.", "");

                            // if match wildcards to root is enabled and is a root domain match
                            if (string.Equals(domain, hostname, StringComparison.OrdinalIgnoreCase) && matchWildcardsToRootDomain)
                            {
                                isMatch = true;
                            }
                            else
                            {
                                //if hostname ends with our domain and is only 1 label longer then it's a match
                                if (hostname.EndsWith("." + domain, StringComparison.CurrentCultureIgnoreCase))
                                {
                                    if (hostname.Count(c => c == '.') == domain.Count(c => c == '.') + 1)
                                    {
                                        isMatch = true;
                                    }
                                }
                            }
                        }

                        if (isMatch)
                        {
                            return isMatch;
                        }
                    }
                }
            }

            return isMatch;
        }

        /// <summary>
        /// Given a CertificateRequestResult or ManagedCertificate, return the managed certiicate
        /// </summary>
        /// <param name="subject"></param>
        /// <returns></returns>
        public static ManagedCertificate? GetManagedCertificate(object subject)
        {
            if (subject == null)
            {
                return null;
            }
            else
            {

                if (subject is CertificateRequestResult)
                {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                    return (subject as CertificateRequestResult).ManagedItem;
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                }
                else if (subject is ManagedCertificate)
                {
                    return (subject as ManagedCertificate);
                }
                else
                {

                    return default;
                }
            }
        }
    }
}

