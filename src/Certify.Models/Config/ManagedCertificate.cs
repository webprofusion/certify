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

    public class RenewalDueInfo
    {
        public DateTime? DateNextRenewalAttempt { get; set; }
        public bool IsRenewalDue { get; set; }
        public string Reason { get; set; }

        public RenewalDueInfo(string reason, bool isRenewalDue, DateTime? renewalAttemptDate)
        {
            Reason = reason;
            IsRenewalDue = isRenewalDue;
            DateNextRenewalAttempt = renewalAttemptDate;
        }
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
        /// If set, date we should next attempt renewal. This is normally not set but may be for items affected by ARI renewal windows etc
        /// </summary>
        public DateTime? DateNextScheduledRenewalAttempt { get; set; }

        /// <summary>
        /// Date we last attempted renewal
        /// </summary>
        public DateTime? DateLastRenewalAttempt { get; set; }

        /// <summary>
        /// Status of most recent renewal attempt
        /// </summary>
        public RequestState? LastRenewalStatus { get; set; }

        /// <summary>
        /// ID of last attempted CA, used to decide if we should attempt failover to another CA
        /// </summary>
        public string? LastAttemptedCA { get; set; }

        /// <summary>
        /// Count of renewal failures since last success
        /// </summary>
        public int RenewalFailureCount { get; set; }

        /// <summary>
        /// Message from last failed renewal attempt
        /// </summary>
        public string? RenewalFailureMessage { get; set; }

        /// <summary>
        /// The Base64 encoded Certificate Id (OCSP, ACME ARI etc) for the current certificate
        /// </summary>
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
        /// get distrinct list of certificate identifiers for this managed cert
        /// </summary>
        /// <returns></returns>
        public List<CertIdentifierItem> GetCertificateIdentifiers()
        {
            return RequestConfig.GetCertificateIdentifiers();
        }

        /// <summary>
        /// Get distinct list of certificate domains/hostnames for this managed cert
        /// </summary>
        /// <returns></returns>
        public List<string> GetCertificateDomains()
        {
            if (RequestConfig == null)
            {
                return new List<string>();
            }
            else
            {
                return RequestConfig.GetCertificateDomains();
            }
        }

        /// <summary>
        /// For the given challenge config and list of identifiers, return subset of identifiers which will
        /// be matched against the config (considering all other configs)
        /// </summary>
        /// <param name="config">  </param>
        /// <param name="identifiers">  </param>
        /// <returns>  </returns>
        public List<CertIdentifierItem> GetChallengeConfigDomainMatches(CertRequestChallengeConfig config, IEnumerable<CertIdentifierItem> domains)
        {
            var matches = new List<CertIdentifierItem>();
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
        /// For the given identifier, get the matching challenge config (DNS provider variant etc)
        /// </summary>
        /// <param name="managedCertificate">  </param>
        /// <param name="identifier">  </param>
        /// <returns>  </returns>
        public CertRequestChallengeConfig GetChallengeConfig(CertIdentifierItem identifier)
        {

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
                //identify matching challenge config based on identifier etc
                if (RequestConfig.Challenges.Count == 1)
                {
                    return RequestConfig.Challenges[0];
                }
                else
                {
                    // start by matching first config with no specific identifier
                    var matchedConfig = RequestConfig.Challenges.FirstOrDefault(c => string.IsNullOrEmpty(c.DomainMatch));

                    if (identifier != null && !string.IsNullOrEmpty(identifier?.Value))
                    {
                        // expand configs into per identifier list
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

                                        // if identifier key is test.com for example we only support one matching config
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
                                                var domainMatchKey = d.Trim().ToLowerInvariant();
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
                        var identifierKey = identifier?.Value.ToLowerInvariant() ?? "";
                        if (configsPerDomain.TryGetValue(identifierKey, out var value))
                        {
                            return value;
                        }

                        // if explicit wildcard match exists, use that
                        if (configsPerDomain.TryGetValue("*." + identifierKey, out var wildValue))
                        {
                            return wildValue;
                        }

                        //if a more specific config matches the identifier, use that, in order of longest identifier name match first
                        var allMatchingConfigKeys = configsPerDomain.Keys.OrderByDescending(l => l.Length);

                        foreach (var wildcard in allMatchingConfigKeys.Where(k => k.StartsWith("*.", StringComparison.CurrentCultureIgnoreCase)))
                        {
                            if (ManagedCertificate.IsDomainOrWildcardMatch(new List<string> { wildcard }, identifier?.Value))
                            {
                                return configsPerDomain[wildcard];
                            }
                        }

                        foreach (var configDomain in allMatchingConfigKeys)
                        {
                            if (configDomain.EndsWith(identifier?.Value.ToLowerInvariant(), StringComparison.CurrentCultureIgnoreCase))
                            {
                                // use longest matching identifier (so subdomain.test.com takes priority
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
            managedCert.LastAttemptedCA = null;
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
        public static bool IsDomainOrWildcardMatch(List<string> dnsNames, string? hostname, bool matchWildcardsToRootDomain = false)
        {
            var isMatch = false;

            if (!string.IsNullOrEmpty(hostname))
            {
                hostname = hostname?.ToLowerInvariant();

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

                            // if match wildcards to root is enabled and is a root identifier match
                            if (string.Equals(domain, hostname, StringComparison.OrdinalIgnoreCase) && matchWildcardsToRootDomain)
                            {
                                isMatch = true;
                            }
                            else
                            {
                                //if hostname ends with our identifier and is only 1 label longer then it's a match
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

        public static RenewalDueInfo? CalculateNextRenewalAttempt(ManagedCertificate s, int renewalIntervalDays, string renewalIntervalMode, bool checkFailureStatus = false)
        {

            if (s == null)
            {
                return null;
            }

            var isRenewalRequired = false;
            var renewalStatusReason = "Not yet due for renewal.";
            var nextRenewalAttemptDate = s.DateExpiry ?? DateTime.Now;

            var timeNow = DateTime.Now;
            var timeSinceLastRenewal = timeNow - (s.DateRenewed ?? timeNow.AddDays(-30));

            var expiryDate = s.DateExpiry ?? timeNow;
            var timeToExpiry = expiryDate - timeNow;

            if (s.DateNextScheduledRenewalAttempt != null && s.DateNextScheduledRenewalAttempt <= timeNow)
            {
                return new RenewalDueInfo("Scheduled renewal is now due.", true, timeNow);
            }

            if (renewalIntervalMode == RenewalIntervalModes.DaysBeforeExpiry)
            {
                var renewalDiffDays = timeToExpiry.TotalDays - renewalIntervalDays;

                // is item expiring within N days
                if (timeToExpiry.TotalDays <= renewalIntervalDays)
                {
                    isRenewalRequired = true;
                    nextRenewalAttemptDate = timeNow;
                    renewalStatusReason = "Item is due to expire soon based on default renewal interval";
                }
                else
                {
                    isRenewalRequired = false;
                    nextRenewalAttemptDate = timeNow.AddDays(renewalDiffDays);
                    renewalStatusReason = $"Item has {renewalDiffDays} remaining days before the default renewal interval occurs.";
                }
            }
            else
            {
                // was item renewed more than N days ago
                var daysSinceLastRenewal = timeSinceLastRenewal.TotalDays;
                var renewalDiffDays = timeSinceLastRenewal.TotalDays - renewalIntervalDays;

                if (daysSinceLastRenewal >= renewalIntervalDays)
                {
                    //isRenewalRequired = Math.Abs(timeSinceLastRenewal.TotalDays) > renewalIntervalDays;
                    isRenewalRequired = true;
                    nextRenewalAttemptDate = timeNow;
                    renewalStatusReason = "Last renewal date is greater than the default renewal interval";
                }
                else
                {
                    isRenewalRequired = false;
                    nextRenewalAttemptDate = timeNow.AddDays(renewalDiffDays);
                    renewalStatusReason = "Last renewal date has not yet reached the default renewal interval";
                }
            }

            // if we have never attempted renewal, renew now
            if (!isRenewalRequired && (s.DateLastRenewalAttempt == null && s.DateRenewed == null))
            {
                isRenewalRequired = true;

                renewalStatusReason = "Item has not yet been succesfully requested.";
            }

            // if renewal is required but we have previously failed, scale the frequency of renewal
            // attempts to a minimum of once per 24hrs.
            if (isRenewalRequired && checkFailureStatus)
            {
                if (s.LastRenewalStatus == RequestState.Error)
                {
                    // our last attempt failed, check how many failures we've had to decide whether
                    // we should attempt now, Scale wait time based on how many attempts we've made.
                    // Max 48hrs between attempts
                    if (s.DateLastRenewalAttempt != null && s.RenewalFailureCount > 0)
                    {
                        var hoursWait = 48;
                        if (s.RenewalFailureCount > 0 && s.RenewalFailureCount < 48)
                        {
                            hoursWait = s.RenewalFailureCount;
                        }

                        var nextAttemptByDate = s.DateLastRenewalAttempt.Value.AddHours(hoursWait);

                        if (timeNow < nextAttemptByDate)
                        {
                            isRenewalRequired = false;
                            return new RenewalDueInfo("Item has previously failed renewal but next renewal attempt is not yet due", isRenewalRequired, nextAttemptByDate);
                        }
                        else
                        {
                            return new RenewalDueInfo("Item has previously failed renewal and next renewal attempt is now due", isRenewalRequired, timeNow);
                        }
                    }
                }
            }

            if (!isRenewalRequired && s.DateNextScheduledRenewalAttempt.HasValue && s.DateNextScheduledRenewalAttempt < nextRenewalAttemptDate)
            {
                renewalStatusReason = "Item renewal is not yet required but is scheduled before normal renewal";
                nextRenewalAttemptDate = s.DateNextScheduledRenewalAttempt.Value;
            }

            return new RenewalDueInfo(renewalStatusReason, isRenewalRequired, nextRenewalAttemptDate);
        }

        /// <summary>
        /// if we know the last renewal date, check whether we should renew again, otherwise assume
        /// it's more than 30 days ago by default and attempt renewal
        /// </summary>
        /// <param name="s">  </param>
        /// <param name="renewalIntervalDays">  </param>
        /// <param name="checkFailureStatus">  </param>
        /// <returns>  </returns>
        public static bool IsRenewalRequired(ManagedCertificate s, int renewalIntervalDays, string renewalIntervalMode, bool checkFailureStatus = false)
        {
            var timeNow = DateTime.Now;

            var timeSinceLastRenewal = (s.DateRenewed ?? timeNow.AddDays(-30)) - timeNow;

            var timeToExpiry = (s.DateExpiry ?? timeNow) - timeNow;

            var isRenewalRequired = false;

            if (renewalIntervalMode == RenewalIntervalModes.DaysBeforeExpiry)
            {
                // is item expiring within N days
                isRenewalRequired = Math.Abs(timeToExpiry.TotalDays) <= renewalIntervalDays;
            }
            else
            {
                // was item renewed more than N days ago
                isRenewalRequired = Math.Abs(timeSinceLastRenewal.TotalDays) > renewalIntervalDays;
            }

            // if we have never attempted renewal, renew now
            if (!isRenewalRequired && (s.DateLastRenewalAttempt == null && s.DateRenewed == null))
            {
                isRenewalRequired = true;
            }

            // if renewal is required but we have previously failed, scale the frequency of renewal
            // attempts to a minimum of once per 24hrs.
            if (isRenewalRequired && checkFailureStatus)
            {
                if (s.LastRenewalStatus == RequestState.Error)
                {
                    // our last attempt failed, check how many failures we've had to decide whether
                    // we should attempt now, Scale wait time based on how many attempts we've made.
                    // Max 48hrs between attempts
                    if (s.DateLastRenewalAttempt != null && s.RenewalFailureCount > 0)
                    {
                        var hoursWait = 48;
                        if (s.RenewalFailureCount > 0 && s.RenewalFailureCount < 48)
                        {
                            hoursWait = s.RenewalFailureCount;
                        }

                        var nextAttemptByDate = s.DateLastRenewalAttempt.Value.AddHours(hoursWait);
                        if (DateTime.Now < nextAttemptByDate)
                        {
                            isRenewalRequired = false;
                        }
                    }
                }
            }

            return isRenewalRequired;
        }
    }
}

