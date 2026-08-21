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
        SSL_ExternallyManaged = 3,
        SSL_ExternalSubscription = 4
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
        public DateTimeOffset? DateNextRenewalAttempt { get; set; }
        public TimeSpan? CertLifetime { get; set; }
        public bool IsRenewalDue { get; set; }
        public bool IsRenewalOnHold { get; set; }
        public string Reason { get; set; }

        /// <summary>
        /// If set, the current number of hrs we will wait before next attempt
        /// </summary>
        public float HoldHrs { get; set; }

        /// <summary>
        /// True when <see cref="DateNextRenewalAttempt"/> is a specific renewal time scheduled against the item
        /// (see <see cref="ManagedCertificate.DateNextScheduledRenewalAttempt"/>, e.g. a CA suggested renewal window via ACME ARI),
        /// rather than the estimate derived from the configured renewal interval.
        /// </summary>
        public bool IsRenewalScheduled { get; set; }

        /// <summary>
        /// True when the renewal attempt has been deferred to the item's maintenance window. When renewal is otherwise due
        /// this means the attempt is being held until the window next opens.
        /// </summary>
        public bool IsDeferredByMaintenanceWindow { get; set; }

        /// <summary>
        /// Parameterless constructor for serialization. This type travels between an instance, the hub and the UI,
        /// which use different serializers, so it must be constructible without arguments.
        /// </summary>
        public RenewalDueInfo()
        {
            Reason = string.Empty;
        }

        public RenewalDueInfo(string reason, bool isRenewalDue, DateTimeOffset? renewalAttemptDate, TimeSpan? certLifetime, bool isRenewalOnHold = false, float holdHrs = 0)
        {
            Reason = reason;
            IsRenewalDue = isRenewalDue;
            DateNextRenewalAttempt = renewalAttemptDate;
            CertLifetime = certLifetime;
            IsRenewalOnHold = isRenewalOnHold;
            HoldHrs = holdHrs;
        }
    }

    public static class LifetimeHealthThresholds
    {
        public const int PercentageDanger = 95;
        public const int PercentageWarning = 75;

        public const int FailureWarning = 3;
        public const int FailureDanger = 5;
        public const int FailureTerminal = 1000;
    }

    public class Lifetime
    {
        public Lifetime(DateTimeOffset dateStart, DateTimeOffset dateEnd)
        {
            DateStart = dateStart;
            DateEnd = dateEnd;
        }
        public DateTimeOffset DateStart { get; }
        public DateTimeOffset DateEnd { get; }

        public int? GetPercentageElapsed(DateTimeOffset testDateTime)
        {
            var lifetime = DateEnd - DateStart;

            if (lifetime.TotalMinutes <= 0)
            {
                return 100;
            }

            var certElapsed = testDateTime - DateStart;
            var elapsedMinutes = lifetime.TotalMinutes - (lifetime.TotalMinutes - certElapsed.TotalMinutes);

            if (elapsedMinutes > 0)
            {
                if (elapsedMinutes >= lifetime.TotalMinutes)
                {
                    return 100;
                }
                else
                {
                    return (int)(elapsedMinutes / lifetime.TotalMinutes * 100);
                }
            }
            else
            {
                return 0;
            }
        }
    }

    public class ManagedCertificateSearchResult
    {
        /// <summary>
        /// Results in this search (may be a paged subset)
        /// </summary>
        public IEnumerable<ManagedCertificate> Results { get; set; } = Enumerable.Empty<ManagedCertificate>();
        /// <summary>
        /// Total results available
        /// </summary>
        public long TotalResults { get; set; }
    }

    public class RequestStageStatus
    {
        public RequestState? Status { get; set; }
        public string? Message { get; set; }
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
        /// Id prefix used for managed certificates discovered via an external certificate manager provider
        /// </summary>
        public const string ExternalItemIdPrefix = "ext-";

        /// <summary>
        /// Determine whether the given managed certificate id refers to an item discovered via an external
        /// certificate manager provider, for use where only the id is known
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static bool IsExternalItemId(string? id) => id?.StartsWith(ExternalItemIdPrefix, StringComparison.OrdinalIgnoreCase) == true;

        /// <summary>
        /// Determine whether the given item type is one where the certificate is acquired from an external
        /// source rather than ordered by this instance
        /// </summary>
        /// <param name="itemType"></param>
        /// <returns></returns>
        public static bool IsExternalSourceItemType(ManagedCertificateType itemType) => itemType == ManagedCertificateType.SSL_ExternallyManaged || itemType == ManagedCertificateType.SSL_ExternalSubscription;

        public void NormalizeExternalSourceSettings()
        {
            if (!IsExternalSourceItem)
            {
                ExternalSource = null;
            }
        }

        /// <summary>
        /// Adopt the current item type for a certificate subscription, if this item still carries the legacy type.
        /// Subscriptions were originally stored as <see cref="ManagedCertificateType.SSL_ExternallyManaged"/> with a
        /// configured external source, which is the same type used for items discovered via a certificate manager provider
        /// </summary>
        /// <returns>true if the item type was changed and the item needs to be stored</returns>
        public bool NormalizeSubscriptionItemType()
        {
            if (ItemType == ManagedCertificateType.SSL_ExternallyManaged && IsSubscription)
            {
                ItemType = ManagedCertificateType.SSL_ExternalSubscription;
                return true;
            }

            return false;
        }

        /// <summary>
        /// If set, managed item is from an external source
        /// </summary>
        public string? SourceId { get; set; }
        public string? SourceName { get; set; }

        /// <summary>
        /// If set, this item is (or was) the temporary target of a hub Managed ACME order. Carries the order id
        /// plus the owning principal and role scope used when fulfilling the order.
        /// </summary>
        public ManagedAcmeOrderInfo? ManagedAcmeOrder { get; set; }

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
        /// Optional subscription configuration when this managed certificate is externally sourced.
        /// </summary>
        public ExternalCertificateSubscription? ExternalSource { get; set; }

        /// <summary>
        /// Display name for this item, for easier reference
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Optional user notes regarding this item
        /// </summary>
        public string? Comments { get; set; }

        /// <summary>
        /// Specific type of item we are managing, affects the renewal/request operations required
        /// </summary>
        public ManagedCertificateType ItemType { get; set; }

        public DateTimeOffset? DateStart { get; set; }
        public DateTimeOffset? DateExpiry { get; set; }
        public DateTimeOffset? DateRenewed { get; set; }

        /// <summary>
        /// Date we last check the OCSP status for this cert
        /// </summary>
        public DateTimeOffset? DateLastOcspCheck { get; set; }

        /// <summary>
        /// Date we last checked the CA renewal info (ARI), if available
        /// </summary>
        public DateTimeOffset? DateLastRenewalInfoCheck { get; set; }

        /// <summary>
        /// If set, date we should next attempt renewal. This is normally not set but may be for items affected by ARI renewal windows etc
        /// </summary>
        public DateTimeOffset? DateNextScheduledRenewalAttempt { get; set; }

        /// <summary>
        /// When this item is fetched via the management API, the calculated plan for its next renewal (when renewal will next
        /// be attempted and why), as computed by the instance which owns the item using its own renewal settings. This saves
        /// every consumer from having to fetch and interpret those settings for itself.
        /// This is derived state which is recalculated on each fetch, so it is not stored with the item.
        /// </summary>
        public RenewalDueInfo? RenewalPlan { get; set; }

        /// <summary>
        /// Date we last attempted renewal
        /// </summary>
        public DateTimeOffset? DateLastRenewalAttempt { get; set; }

        /// <summary>
        /// Timestamp of last data fetch from source instance
        /// </summary>
        public DateTimeOffset? DateRetrieved { get; set; }

        /// <summary>
        /// Overall summary status of most recent renewal and deployment attempt
        /// </summary>
        public RequestState? LastRenewalStatus { get; set; }

        /// <summary>
        /// Status of the most recent primary certificate request before deployment stages.
        /// </summary>
        public RequestStageStatus? LastPrimaryRequest { get; set; }

        /// <summary>
        /// Status of the most recent standard binding/store deployment stage.
        /// </summary>
        public RequestStageStatus? LastBindingDeployment { get; set; }

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
        /// The Base64 encoded ARI Certificate Id (ACME ARI etc) for the current certificate
        /// </summary>
        public string? ARICertificateId { get; set; }

        /// <summary>
        /// Id of the last CA this cert was successfully ordered/renewed with. 
        /// Particularly important for ARI replacement as attempting to replace a cert with the id from another CA will result in order rejection.
        /// </summary>
        public string? CertificateCurrentCA { get; set; }
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

        /// <summary>
        /// If specified this is the days or percentage target for renewal, depending on the renewal mode
        /// </summary>
        public float? CustomRenewalTarget { get; set; }

        /// <summary>
        /// If specified, custom renewal interval mode (DaysBeforeExpiry, DaysAfterRenewal, PercentageLifetime)
        /// </summary>
        public string? CustomRenewalIntervalMode { get; set; }

        /// <summary>
        /// If specified, the ID of a maintenance window that constrains when this certificate can be renewed.
        /// If null, uses the instance default maintenance window (if any).
        /// </summary>
        public string? MaintenanceWindowId { get; set; }

        /// <summary>
        /// PEM encoded version of public certificate
        /// </summary>
        public string? CertificatePEM { get; set; }

        /// <summary>
        /// True if this item's certificate is acquired from an external source rather than ordered by this instance
        /// </summary>
        [JsonIgnore]
        public bool IsExternalSourceItem => IsExternalSourceItemType(ItemType);

        /// <summary>
        /// True if this item is an external certificate subscription, which periodically fetches an updated
        /// certificate from its configured external source.
        /// An item stored before subscriptions had their own item type still carries the legacy type, and is
        /// recognised by having an external source configured (items discovered via a certificate manager
        /// provider never carry one)
        /// </summary>
        [JsonIgnore]
        public bool IsSubscription => ItemType == ManagedCertificateType.SSL_ExternalSubscription
            || (ItemType == ManagedCertificateType.SSL_ExternallyManaged && ExternalSource?.SourceType != null);

        /// <summary>
        /// True if this item is a certificate subscription which has enough configuration for a request to actually be
        /// attempted against its source. A subscription which has not been configured yet is still a subscription, but
        /// there is nothing to fetch and nothing to fetch it from.
        /// Use <see cref="IsSubscription"/> to decide how an item is processed, and this to decide whether a request
        /// can be attempted - an unconfigured subscription must never fall through to ordering its own certificate
        /// </summary>
        [JsonIgnore]
        public bool IsActionableSubscription => IsSubscription
            && !string.IsNullOrWhiteSpace(ExternalSource?.SourceType)
            && !string.IsNullOrWhiteSpace(ExternalSource?.ExternalReference);

        /// <summary>
        /// True if this item was discovered via an external certificate manager provider and is not stored by this instance
        /// </summary>
        [JsonIgnore]
        public bool IsExternallyManaged => IsExternalSourceItem && !IsSubscription;

        public override string ToString() => $"[{Id ?? "null"}]: \"{Name}\"";

        [JsonIgnore]
        public bool Deleted { get; set; } // do not serialize to settings

        [JsonIgnore]
        public Lifetime? CertificateLifetime
        {
            get
            {
                if (DateExpiry.HasValue)
                {
                    return new Lifetime(DateStart ?? DateExpiry.Value, DateExpiry.Value);
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Get the percentage of the certificate lifetime elapsed, if known
        /// </summary>
        /// <param name="testDateTime"></param>
        /// <returns></returns>
        public int? GetPercentageLifetimeElapsed(DateTimeOffset testDateTime)
        {
            return CertificateLifetime?.GetPercentageElapsed(testDateTime);
        }

        [JsonIgnore]
        public ManagedCertificateHealth Health
        {
            get
            {
                var percentageElapsed = GetPercentageLifetimeElapsed(DateTimeOffset.UtcNow);

                if (LastRenewalStatus == RequestState.Error)
                {
                    if (RenewalFailureCount > LifetimeHealthThresholds.FailureDanger || percentageElapsed > LifetimeHealthThresholds.PercentageDanger)
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
                                if (percentageElapsed > LifetimeHealthThresholds.PercentageDanger)
                                {
                                    return ManagedCertificateHealth.Error;
                                }
                                else if (percentageElapsed > LifetimeHealthThresholds.PercentageWarning)
                                {
                                    return ManagedCertificateHealth.Warning;
                                }
                                else
                                {
                                    if (LastRenewalStatus == RequestState.Warning)
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
                    }
                    else
                    {
                        return ManagedCertificateHealth.Unknown;
                    }
                }
            }
        }

        /// <summary>
        /// get distinct list of certificate identifiers for this managed cert
        /// </summary>
        /// <returns></returns>
        public List<CertIdentifierItem> GetCertificateIdentifiers()
        {
            return RequestConfig.GetCertificateIdentifiers();
        }

        /// <summary>
        /// Populates RequestConfig domain/IP identifiers from an external source identifier list
        /// (e.g. from a ManagedCertificateSummary or a parsed X.509 SAN extension).
        /// Used before binding deployment for externally-managed certificates so that
        /// BindingDeploymentManager can match server hostname bindings correctly.
        /// </summary>
        public void ApplySourceIdentifiers(IEnumerable<CertIdentifierItem> identifiers)
        {
            var list = identifiers?.ToList() ?? new List<CertIdentifierItem>();
            if (!list.Any())
            {
                return;
            }

            var dnsValues = list
                .Where(i => i.IdentifierType == CertIdentifierType.Dns && !string.IsNullOrWhiteSpace(i.Value))
                .Select(i => i.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var ipValues = list
                .Where(i => i.IdentifierType == CertIdentifierType.Ip && !string.IsNullOrWhiteSpace(i.Value))
                .Select(i => i.Value)
                .Distinct()
                .ToArray();

            if (dnsValues.Length > 0)
            {
                RequestConfig.PrimaryDomain = dnsValues[0];
                RequestConfig.SubjectAlternativeNames = dnsValues;
            }

            if (ipValues.Length > 0)
            {
                RequestConfig.SubjectIPAddresses = ipValues;
            }
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
                    // domain match rule evaluation is shared, see DomainMatchRules
                    var matchedConfig = DomainMatchRules.FindBestMatch(
                        identifier?.Value,
                        RequestConfig.Challenges,
                        c => c.DomainMatch);

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
            managedCert.ManagedAcmeOrder = null;
            managedCert.ExternalSource = null;
            managedCert.RenewalFailureCount = 0;
            managedCert.RenewalFailureMessage = null;
            managedCert.LastPrimaryRequest = null;
            managedCert.LastBindingDeployment = null;

            managedCert.LastRenewalStatus = null;
            managedCert.CurrentOrderUri = null;
            managedCert.CertificatePath = null;
            managedCert.ARICertificateId = null;
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
                hostname = hostname!.ToLowerInvariant();

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

        public static RenewalDueInfo? CalculateNextRenewalAttempt(ManagedCertificate s, float renewalInterval, string renewalIntervalMode, DateTimeOffset? testDateTime = null)
        {

            if (s == null)
            {
                return null;
            }

            var nextRenewalAttemptDate = s.DateExpiry ?? DateTimeOffset.UtcNow;

            var checkDate = DateTimeOffset.UtcNow;

            if (testDateTime != null)
            {
                checkDate = testDateTime.Value;
            }

            var isRenewalRequired = false;
            var isRenewalScheduled = false;
            var renewalStatusReason = " Item not due for renewal";
            TimeSpan? certLifetime = null;

            // use default renewal interval and mode, or prefer custom if specified
            var selectedRenewalIntervalMode = s.CustomRenewalIntervalMode ?? renewalIntervalMode;
            var selectedRenewalInterval = s.CustomRenewalTarget ?? (float)renewalInterval;

            // if cert has previously been renewed, calculate next renewal, otherwise renewal will be immediately due unless renewal has been failing
            if (s.DateRenewed.HasValue)
            {
                var timeSinceLastRenewal = checkDate - s.DateRenewed.Value;

                var expiryDate = s.DateExpiry ?? checkDate;
                var timeToExpiry = expiryDate - checkDate;
                certLifetime = s.DateExpiry - s.DateStart;

                if (s.DateNextScheduledRenewalAttempt != null && s.DateNextScheduledRenewalAttempt <= checkDate)
                {
                    isRenewalRequired = true;
                    isRenewalScheduled = true;
                    renewalStatusReason = "Certificate scheduled renewal is now due.";
                }
                else
                {

                    // strategy if cert lifetime is less than the standard renewal interval allows or the renewal mode is based on percentage lifetime
                    if (certLifetime.HasValue && (certLifetime.Value.TotalDays < renewalInterval || selectedRenewalIntervalMode == RenewalIntervalModes.PercentageLifetime))
                    {
                        // cert has a shorter lifetime than the renewal interval. Switch to a percentage based renewal 
                        float targetRenewalPercentage = 75;

                        if (selectedRenewalIntervalMode == RenewalIntervalModes.PercentageLifetime && selectedRenewalInterval > 0)
                        {
                            targetRenewalPercentage = selectedRenewalInterval;

                            if (targetRenewalPercentage > 100) { targetRenewalPercentage = 100; }
                        }

                        var targetRenewalMinutesAfterCertStart = certLifetime.Value.TotalMinutes * (targetRenewalPercentage / 100);
                        var targetRenewalDate = s.DateStart != null ? s.DateStart.Value.AddMinutes(targetRenewalMinutesAfterCertStart) : s.DateRenewed.Value;
                        nextRenewalAttemptDate = targetRenewalDate;

                        if (targetRenewalDate <= checkDate)
                        {
                            isRenewalRequired = true;
                            renewalStatusReason = $"Certificate has exceeded {targetRenewalPercentage}% of its lifetime.";
                        }
                        else
                        {
                            isRenewalRequired = false;
                            renewalStatusReason = $"Certificate has not yet exceeded {targetRenewalPercentage}% of its lifetime.";
                        }
                    }
                    else
                    {
                        // calculate renewal for non-percentage based strategies

                        if (renewalIntervalMode == RenewalIntervalModes.DaysBeforeExpiry)
                        {
                            var renewalDiffDays = timeToExpiry.TotalDays - renewalInterval;

                            // is item expiring within N days
                            if (timeToExpiry.TotalDays <= renewalInterval)
                            {

                                isRenewalRequired = true;
                                nextRenewalAttemptDate = checkDate;
                                renewalStatusReason = "Certificate is due to expire within the default renewal interval.";
                            }
                            else
                            {
                                isRenewalRequired = false;
                                nextRenewalAttemptDate = checkDate.AddDays(renewalDiffDays);
                                renewalStatusReason = $"Certificate has {renewalDiffDays} remaining days before the default renewal interval occurs.";
                            }
                        }
                        else
                        {
                            // was item renewed more than N days ago
                            var daysSinceLastRenewal = timeSinceLastRenewal.TotalDays;
                            var renewalDiffDays = timeSinceLastRenewal.TotalDays - renewalInterval;

                            if (daysSinceLastRenewal >= renewalInterval)
                            {
                                isRenewalRequired = true;
                                nextRenewalAttemptDate = checkDate;
                                renewalStatusReason = "Certificate is due for renewal, based on the default renewal settings.";
                            }
                            else
                            {
                                isRenewalRequired = false;
                                nextRenewalAttemptDate = checkDate.AddDays(-renewalDiffDays);
                                renewalStatusReason = "Certificate does not yet require renewal, based on the default renewal settings.";
                            }
                        }
                    }
                }
            }

            // if we have never achieved renewal, renew now
            if (!isRenewalRequired && s.DateRenewed == null)
            {
                isRenewalRequired = true;
                renewalStatusReason = "Certificate has not yet been successfully requested, so a renewal attempt is required.";
            }

            // if renewal is required but we have previously failed, scale the frequency of renewal
            // attempts to a minimum of once per 24hrs.
            if (isRenewalRequired && (s.LastRenewalStatus == RequestState.Error || s.LastRenewalStatus == RequestState.Warning || s.RenewalFailureCount > 0))
            {
                // our last attempt failed, check how many failures we've had to decide whether
                // we should attempt now or scale wait time based on how many attempts we've made.
                // Max 48hrs between attempts or 90% of lifetime (if known)

                if (s.RenewalFailureCount < 4)
                {
                    return new RenewalDueInfo(
                                reason: $"Renewal attempt is due, item has failed {s.RenewalFailureCount} times.",
                                isRenewalDue: true,
                                checkDate,
                                certLifetime,
                                isRenewalOnHold: false
                                );
                }
                else
                {

                    if (s.DateLastRenewalAttempt != null)
                    {
                        var maxWaitHrsLimit = 48f; // absolute max wait time if cert lifetime not known
                        var maxWaitHrs = maxWaitHrsLimit;

                        // prefer max hold wait of 10% of lifetime, particularly useful for short lifetime certs
                        if (s.RequestConfig.PreferredExpiryDays != null)
                        {
                            maxWaitHrs = ((float)s.RequestConfig.PreferredExpiryDays * 24) * 0.1f;
                        }
                        else if (s.DateExpiry != null && s.DateStart != null)
                        {
                            var lifetime = s.DateExpiry - s.DateStart;
                            maxWaitHrs = (float)lifetime.Value.TotalHours * 0.1f;
                        }
                        else
                        {
                            // cert lifetime is unknown, if not yet requested default to a short retry interval
                            maxWaitHrs = Math.Max(0.25f * s.RenewalFailureCount, 1f);
                        }

                        // set ceiling for max hold wait time
                        maxWaitHrs = Math.Min(maxWaitHrs, maxWaitHrsLimit);

                        // calculate exponential back off, increasing 10% with retries to a max wait based on lifetime
                        var factor = 1 + (maxWaitHrs / 10);
                        var minWaitHrs = 1;

                        var calcWaitHrs = (float)Math.Min(minWaitHrs * (factor * s.RenewalFailureCount), maxWaitHrs);
                        var nextAttemptByDate = s.DateLastRenewalAttempt.Value.AddHours(calcWaitHrs);

                        if (DateTimeOffset.UtcNow < nextAttemptByDate)
                        {
                            return new RenewalDueInfo(
                                    reason: $"Renewal attempt is on hold for {Math.Round(calcWaitHrs, 0, MidpointRounding.AwayFromZero)}hrs because item has failed {s.RenewalFailureCount} times and attempts are subject to periodic limits.",
                                    isRenewalDue: true,
                                    nextAttemptByDate, certLifetime,
                                    isRenewalOnHold: true,
                                    holdHrs: calcWaitHrs
                                    );
                        }
                        else
                        {
                            if (s.RenewalFailureCount > LifetimeHealthThresholds.FailureTerminal)
                            {
                                // item has failed too many times and need to be fixed manually before it can resume renewal
                                return new RenewalDueInfo(
                                   reason: $"Renewal will no longer be attempted because the item has failed {s.RenewalFailureCount} times. The limit for failed attempts is {LifetimeHealthThresholds.FailureTerminal}. Manually request this item to resolve the issue or remove if no longer required.",
                                   isRenewalDue: true,
                                   nextAttemptByDate, certLifetime,
                                   isRenewalOnHold: true,
                                   holdHrs: calcWaitHrs
                                   );
                            }
                            else
                            {
                                return new RenewalDueInfo(
                                       reason: $"Renewal attempt is due, item has failed {s.RenewalFailureCount} times and renewal will be periodically attempted.",
                                       isRenewalDue: true,
                                       nextAttemptByDate, certLifetime,
                                       isRenewalOnHold: false
                                       );
                            }
                        }
                    }
                    else
                    {
                        // never attempted, can't be put on hold
                        return new RenewalDueInfo(
                                  reason: $"Renewal attempt is due, item has not yet been attempted.",
                                  isRenewalDue: true,
                                  checkDate,
                                  certLifetime,
                                  isRenewalOnHold: false
                                  );
                    }
                }
            }

            if (!isRenewalRequired && s.DateNextScheduledRenewalAttempt.HasValue && s.DateNextScheduledRenewalAttempt < nextRenewalAttemptDate)
            {
                renewalStatusReason = "Certificate renewal is not yet required but has been scheduled ahead of normal renewal.";
                nextRenewalAttemptDate = s.DateNextScheduledRenewalAttempt.Value;
                isRenewalScheduled = true;
            }

            // a planned renewal should never be scheduled before the certificate itself became valid. This can otherwise happen due to clock skew,
            // a certificate issued with a future NotBefore, or a CA suggested renewal window which predates the current certificate.
            if (!isRenewalRequired && s.DateStart.HasValue && nextRenewalAttemptDate < s.DateStart.Value)
            {
                nextRenewalAttemptDate = s.DateStart.Value;
                renewalStatusReason = "Certificate renewal is not yet required. The planned renewal date has been adjusted because it preceded the certificate start date.";
                isRenewalScheduled = false;
            }

            return new RenewalDueInfo(renewalStatusReason, isRenewalRequired, nextRenewalAttemptDate, certLifetime)
            {
                IsRenewalScheduled = isRenewalScheduled
            };
        }

        public static bool TryParseManagementHubReference(string? reference, out string instanceId, out string managedCertificateId)
        {
            instanceId = string.Empty;
            managedCertificateId = string.Empty;

            if (string.IsNullOrWhiteSpace(reference))
            {
                return false;
            }

            var normalized = reference.Trim().Replace(':', '/');
            var parts = normalized.Split(new[] { "/" }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
            {
                return false;
            }

            instanceId = parts[0];
            managedCertificateId = parts[1];

            return !string.IsNullOrWhiteSpace(instanceId) && !string.IsNullOrWhiteSpace(managedCertificateId);
        }
    }
}

