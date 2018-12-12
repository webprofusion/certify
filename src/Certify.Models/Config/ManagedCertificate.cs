using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;

namespace Certify.Models
{
    public enum ManagedCertificateType
    {
        SSL_LetsEncrypt_LocalIIS = 1,
        SSL_LetsEncrypt_Manual = 2
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
        }

        /// <summary>
        /// If true, the auto renewal process will include this item in attempted renewal operations
        /// if applicable
        /// </summary>
        public bool IncludeInAutoRenew { get; set; }

        /// <summary>
        /// Host or server where this item is based, usually localhost if managing the local server
        /// </summary>
        public string TargetHost { get; set; }

        /// <summary>
        /// List of configured domains this managed site will include (primary subject or SAN)
        /// </summary>
        public ObservableCollection<DomainOption> DomainOptions { get; set; }

        /// <summary>
        /// Configuration options for this request
        /// </summary>
        public CertRequestConfig RequestConfig { get; set; }

        /// <summary>
        /// Unique ID for this managed item
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// If set, this the Id of the parent managed item which controls the Certificate Request.
        /// When the parent item completes a certificate request the child item will then also be
        /// invoked in order to perform subsequent deployments/scripting etc
        /// </summary>
        public string ParentId { get; set; }

        /// <summary>
        /// Optional grouping ID, such as where mamaged sites share a common IIS site id
        /// </summary>

        public string GroupId { get; set; }

        /// <summary>
        /// Id of specific matching site on server (replaces GroupId)
        /// </summary>
        public string ServerSiteId { get => GroupId; set => GroupId = value; }

        /// <summary>
        /// If set, this is an identifier for the host to group multiple sets of managed sites across servers
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// Display name for this item, for easier reference
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Optional user notes regarding this item
        /// </summary>
        public string Comments { get; set; }

        /// <summary>
        /// Specific type of item we are managing, affects the renewal/rewuest operations required
        /// </summary>
        public ManagedCertificateType ItemType { get; set; }

        public DateTime? DateStart { get; set; }
        public DateTime? DateExpiry { get; set; }
        public DateTime? DateRenewed { get; set; }

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
        public string RenewalFailureMessage { get; set; }

        public string CertificateId { get; set; }
        public string CertificatePath { get; set; }
        public string CertificateThumbprintHash { get; set; }
        public string CertificatePreviousThumbprintHash { get; set; }
        public bool CertificateRevoked { get; set; }
        public string CurrentOrderUri { get; set; }

        public override string ToString()
        {
            return $"[{Id ?? "null"}]: \"{Name}\"";
        }

        [JsonIgnore]
        public bool Deleted { get; set; } // do not serialize to settings

        [JsonIgnore]
        public ManagedCertificateHealth Health
        {
            get
            {
                if (LastRenewalStatus == RequestState.Error)
                {
                    if (RenewalFailureCount > 5)
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
                            return ManagedCertificateHealth.OK;
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
        /// For the given challenge config and list of domains, return subset of domains which will
        /// be matched against the config (considering all other configs)
        /// </summary>
        /// <param name="config">  </param>
        /// <param name="domains">  </param>
        /// <returns>  </returns>
        public List<string> GetChallengeConfigDomainMatches(CertRequestChallengeConfig config, IEnumerable<string> domains)
        {
            List<string> matches = new List<string>();
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
                    ChallengeType = RequestConfig.ChallengeType
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
                    CertRequestChallengeConfig matchedConfig = RequestConfig.Challenges.FirstOrDefault(c => String.IsNullOrEmpty(c.DomainMatch));

                    if (!string.IsNullOrEmpty(domain))
                    {
                        // expand configs into per domain list
                        Dictionary<string, CertRequestChallengeConfig> configsPerDomain = new Dictionary<string, CertRequestChallengeConfig>();
                        foreach (var c in RequestConfig.Challenges.Where(config => !string.IsNullOrEmpty(config.DomainMatch)))
                        {
                            if (!string.IsNullOrEmpty(c.DomainMatch) && !c.DomainMatch.Contains(";"))
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
                                        configsPerDomain.Add(domainMatchKey, c);
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

                        foreach (var wildcard in allMatchingConfigKeys.Where(k => k.StartsWith("*.")))
                        {
                            if (ManagedCertificate.IsDomainOrWildcardMatch(new List<string> { wildcard }, domain))
                            {
                                return configsPerDomain[wildcard];
                            }
                        }

                        foreach (var configDomain in allMatchingConfigKeys)
                        {
                            if (configDomain.EndsWith(domain))
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
                            ChallengeType = RequestConfig.ChallengeType
                        };
                    }
                }
            }
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
                    var wildcards = dnsNames.Where(d => d.StartsWith("*."));
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
                                if (matchWildcardsToRootDomain)
                                {
                                    isMatch = true;
                                }
                            }
                            else
                            {
                                //if hostname ends with our domain and is only 1 label longer then it's a match
                                if (hostname.EndsWith("." + domain))
                                {
                                    if (hostname.Count(c => c == '.') == domain.Count(c => c == '.') + 1)
                                    {
                                        isMatch = true;
                                    }
                                }
                            }
                        }
                        if (isMatch) return isMatch;
                    }
                }
            }

            return isMatch;
        }
    }

    //TODO: may deprecate, was mainly for preview of setup wizard
    public class ManagedCertificateBinding
    {
        public string Hostname { get; set; }
        public int? Port { get; set; }

        /// <summary>
        /// IP is either * (all unassigned) or a specific IP
        /// </summary>
        public string IP { get; set; }

        public bool UseSNI { get; set; }
        public string CertName { get; set; }
        public RequiredActionType PlannedAction { get; set; }

        /// <summary>
        /// The primary domain is the main domain listed on the certificate
        /// </summary>
        public bool IsPrimaryCertificateDomain { get; set; }

        /// <summary>
        /// For SAN certificates, indicate if this name is an alternative name to be associated with
        /// a primary domain certificate
        /// </summary>
        public bool IsSubjectAlternativeName { get; set; }
    }
}
