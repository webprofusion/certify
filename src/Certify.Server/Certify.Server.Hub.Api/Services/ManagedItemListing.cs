using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Reporting;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// The managed item summaries and status counts the hub reports from its cached instance items, limited to the
    /// items a caller may see. Shared by the endpoints which list items and those which count them, so a summary never
    /// counts an item its listing would not show.
    /// </summary>
    public static class ManagedItemListing
    {
        /// <summary>
        /// Build the set of managed certificate summaries matching the given criteria, limited to the items visible under
        /// the caller's resolved scope for listing managed items.
        /// </summary>
        public static async Task<List<ManagedCertificateSummary>> GetItems(
            ICertifyInternalApiClient client,
            ManagementAPI mgmtAPI,
            ResourceScope visibility,
            string? instanceId,
            string? keyword,
            string? health,
            IEnumerable<string>? tagScopes,
            bool requireAllTags,
            bool includeUntagged,
            string? certificateAuthority = null,
            string? keyType = null)
        {
            var scopes = TagScopeFilter.ParseAll(tagScopes);

            var managedItems = mgmtAPI.GetManagedInstanceItems();
            var instances = mgmtAPI.GetConnectedInstances();

            // TODO: would fetching cached hub status summaries be faster
            var knownInstances = await client.GetHubManagedInstances(PrincipalAccess.SystemAuthContext) ?? [];

            var tagsByItemId = await GetItemTagsByItemId(client, TaggedItemTypes.ManagedCertificate);
            var caTitles = await GetCertificateAuthorityTitles(client);

            ManagedCertificateHealth? healthFilter = null;

            if (!string.IsNullOrEmpty(health) && Enum.TryParse(health, true, out ManagedCertificateHealth healthValue))
            {
                healthFilter = healthValue;
            }

            var list = new List<ManagedCertificateSummary>();

            foreach (var remote in managedItems.Values)
            {
                if (!string.IsNullOrEmpty(instanceId) && instanceId != remote.InstanceId)
                {
                    continue;
                }

                var instance = knownInstances.FirstOrDefault(k => k.InstanceId == remote.InstanceId)
                               ?? instances.FirstOrDefault(c => c.InstanceId == remote.InstanceId);

                // a custom CA is defined on the instance itself, so its own titles come first
                var instanceCATitles = mgmtAPI.GetInstanceCertificateAuthorityTitles(remote.InstanceId);

                foreach (var i in remote.Items)
                {
                    if (!string.IsNullOrWhiteSpace(keyword) && i.Name?.Contains(keyword, StringComparison.InvariantCultureIgnoreCase) != true)
                    {
                        continue;
                    }

                    if (healthFilter != null && i.Health != healthFilter)
                    {
                        continue;
                    }

                    var itemCA = GetCertificateAuthorityId(i);
                    var itemKeyType = GetKeyType(i);

                    if (!string.IsNullOrEmpty(certificateAuthority) && !string.Equals(itemCA, certificateAuthority, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(keyType) && !string.Equals(itemKeyType, keyType, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var tags = tagsByItemId.TryGetValue(i.Id ?? "", out var itemTags)
                        ? itemTags.Where(t => HubItemTags.AppliesToInstance(t.InstanceId, remote.InstanceId)).ToList()
                        : new List<TagSummary>();

                    if (!TagScopeFilter.Matches(tags, scopes, requireAllTags, includeUntagged))
                    {
                        continue;
                    }

                    var identifiers = i.GetCertificateIdentifiers();

                    if (!visibility.Permits(tags, identifiers.Select(id => id.Value)))
                    {
                        continue;
                    }

                    list.Add(new ManagedCertificateSummary
                    {
                        InstanceId = remote.InstanceId,
                        InstanceTitle = instance?.DisplayTitle,
                        Id = i.Id ?? "",
                        Title = i.Name ?? "",
                        OS = instance?.OS,
                        ClientDetails = i.SourceId != null ? i.SourceName : instance?.ClientName,
                        PrimaryIdentifier = identifiers.FirstOrDefault(p => p.Value == i.RequestConfig.PrimaryDomain) ?? identifiers.FirstOrDefault(),
                        Identifiers = identifiers,
                        DateRenewed = i.DateRenewed,
                        DateExpiry = i.DateExpiry,
                        Comments = i.Comments ?? "",
                        CertificateAuthorityId = itemCA,
                        CertificateAuthorityTitle = itemCA == null ? null : instanceCATitles?.GetValueOrDefault(itemCA) ?? caTitles.GetValueOrDefault(itemCA, itemCA),
                        KeyType = itemKeyType,
                        Issuer = GetIssuerName(i.CertificateIssuer),
                        Status = i.Health.ToString(),
                        DateRetrieved = i.DateRetrieved,
                        HasCertificate = !string.IsNullOrEmpty(i.CertificatePath),
                        IsExternallyManaged = i.IsExternallyManaged,
                        IsSubscription = i.IsSubscription,
                        Tags = tags
                    });
                }
            }

            return list;
        }

        /// <summary>
        /// The CA last used for an item: the CA which issued its current certificate, otherwise the CA of its most recent
        /// attempt, which covers certificates recorded before the issuing CA was.
        /// </summary>
        public static string? GetCertificateAuthorityId(ManagedCertificate item)
        {
            return !string.IsNullOrEmpty(item.CertificateCurrentCA) ? item.CertificateCurrentCA
                : !string.IsNullOrEmpty(item.LastAttemptedCA) ? item.LastAttemptedCA
                : null;
        }

        /// <summary>
        /// The key type of an item's current certificate, otherwise its configured key type. An instance which predates
        /// recording the key type of the certificate itself reports only the configured key type, and so none where the
        /// instance default key type applies.
        /// </summary>
        public static string? GetKeyType(ManagedCertificate item)
        {
            return !string.IsNullOrEmpty(item.CertificateKeyType) ? item.CertificateKeyType
                : !string.IsNullOrEmpty(item.RequestConfig?.CSRKeyAlg) ? item.RequestConfig.CSRKeyAlg
                : null;
        }

        /// <summary>
        /// A readable name for a certificate issuer distinguished name: its common name, followed by its organization
        /// where the common name does not already include it, e.g. "R11 (Let's Encrypt)"
        /// </summary>
        public static string? GetIssuerName(string? issuerDistinguishedName)
        {
            if (string.IsNullOrWhiteSpace(issuerDistinguishedName))
            {
                return null;
            }

            try
            {
                var rdns = new System.Security.Cryptography.X509Certificates.X500DistinguishedName(issuerDistinguishedName).EnumerateRelativeDistinguishedNames().ToList();

                string? Attribute(string oid) => rdns.FirstOrDefault(r => !r.HasMultipleElements && r.GetSingleElementType().Value == oid)?.GetSingleElementValue();

                var commonName = Attribute("2.5.4.3");
                var organization = Attribute("2.5.4.10");

                if (string.IsNullOrWhiteSpace(commonName))
                {
                    return organization ?? issuerDistinguishedName;
                }

                return string.IsNullOrWhiteSpace(organization) || commonName.Contains(organization, StringComparison.OrdinalIgnoreCase)
                    ? commonName
                    : $"{commonName} ({organization})";
            }
            catch
            {
                return issuerDistinguishedName;
            }
        }

        /// <summary>
        /// Count the given items by CA and by key type, largest groups first
        /// </summary>
        public static ManagedCertificateBreakdown Breakdown(IEnumerable<ManagedCertificateSummary> items)
        {
            var list = items.ToList();

            return new ManagedCertificateBreakdown
            {
                CertificateAuthorities = list
                    .GroupBy(i => i.CertificateAuthorityId, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new ManagedCertificateGroupCount { Key = g.Key, Title = g.Key == null ? "None" : g.First().CertificateAuthorityTitle ?? g.Key, Count = g.Count() })
                    .OrderByDescending(g => g.Count).ThenBy(g => g.Title)
                    .ToList(),
                KeyTypes = list
                    .GroupBy(i => i.KeyType, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new ManagedCertificateGroupCount { Key = g.Key, Title = StandardKeyTypes.GetDisplayName(g.Key), Count = g.Count() })
                    .OrderByDescending(g => g.Count).ThenBy(g => g.Title)
                    .ToList()
            };
        }

        /// <summary>
        /// Display titles of the CAs known to the hub itself, keyed by CA id. Used where an item's instance has not
        /// reported its own CAs, and so for every CA on an instance which has not reported them yet.
        /// </summary>
        private static async Task<Dictionary<string, string>> GetCertificateAuthorityTitles(ICertifyInternalApiClient client)
        {
            var titles = CertificateAuthority.CoreCertificateAuthorities
                .Where(ca => ca.Id != null)
                .ToDictionary(ca => ca.Id!, ca => ca.Title, StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var ca in await client.GetCertificateAuthorities(PrincipalAccess.SystemAuthContext) ?? [])
                {
                    if (!string.IsNullOrEmpty(ca.Id) && !string.IsNullOrEmpty(ca.Title))
                    {
                        titles[ca.Id] = ca.Title;
                    }
                }
            }
            catch
            {
                // the built in CAs are still named
            }

            return titles;
        }

        /// <summary>
        /// A status summary for a caller: the pre-aggregated instance summaries when they may see every item, otherwise
        /// counts over the items they may see.
        /// </summary>
        public static async Task<StatusSummary> GetSummary(ICertifyInternalApiClient client, ManagementAPI mgmtAPI, ResourceScope visibility, string? instanceId, AuthContext? authContext)
        {
            if (visibility.IsUnrestricted)
            {
                var aggregate = string.IsNullOrEmpty(instanceId)
                    ? await mgmtAPI.GetManagedCertificateSummary(authContext)
                    : await mgmtAPI.GetManagedCertificateSummary(instanceId, authContext);

                return aggregate ?? new StatusSummary { InstanceId = instanceId ?? string.Empty };
            }

            var items = await GetItems(client, mgmtAPI, visibility, instanceId, keyword: null, health: null, tagScopes: null, requireAllTags: false, includeUntagged: false);

            return Summarise(items, instanceId);
        }

        /// <summary>
        /// Load the display tags for all items of the given type, keyed by item id. Each tag keeps the instance it was
        /// recorded for, so it is only applied to the item on that instance.
        /// </summary>
        /// <remarks>
        /// TODO: we need to optimize this by only loading tags for items we know are in the result set, which
        /// requires a backend API to fetch tags for a given set of item ids.
        /// </remarks>
        public static async Task<Dictionary<string, List<TagSummary>>> GetItemTagsByItemId(ICertifyInternalApiClient client, string itemTypeId)
        {
            var tagsByItemId = new Dictionary<string, List<TagSummary>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // load tag categories to get display names and colors
                var categoriesByKey = new Dictionary<string, TagCategory>();
                var categories = await client.GetTagCategories(PrincipalAccess.SystemAuthContext);

                if (categories != null)
                {
                    foreach (var cat in categories)
                    {
                        categoriesByKey[cat.CategoryKey] = cat;
                    }
                }

                var allItemTags = await client.GetAllHubItemTags(null, null, itemTypeId, null, PrincipalAccess.SystemAuthContext);

                if (allItemTags != null)
                {
                    foreach (var tag in allItemTags)
                    {
                        if (!tagsByItemId.TryGetValue(tag.TaggedItemId, out var itemTags))
                        {
                            itemTags = new List<TagSummary>();
                            tagsByItemId[tag.TaggedItemId] = itemTags;
                        }

                        categoriesByKey.TryGetValue(tag.CategoryKey, out var category);

                        itemTags.Add(new TagSummary
                        {
                            CategoryKey = tag.CategoryKey,
                            CategoryDisplayName = category?.DisplayName ?? tag.CategoryKey,
                            Value = tag.Value,
                            ColorHint = category?.ColorHint,
                            InstanceId = tag.InstanceId
                        });
                    }
                }
            }
            catch
            {
                // if tag loading fails, continue without tags
            }

            return tagsByItemId;
        }

        /// <summary>
        /// Summarise a set of managed certificate summaries into overall status counts.
        /// </summary>
        /// <remarks>
        /// The counts must match those an instance reports for itself, because the summary falls back to the
        /// pre-aggregated instance summaries when no filtering applies. In particular ExternallyManaged counts only
        /// items discovered via an external certificate manager provider, not certificate subscriptions.
        /// </remarks>
        public static StatusSummary Summarise(IEnumerable<ManagedCertificateSummary> items, string? instanceId)
        {
            var summary = new StatusSummary { InstanceId = instanceId ?? string.Empty };

            foreach (var item in items)
            {
                summary.Total++;
                summary.TotalDomains += item.Identifiers?.Count() ?? 0;

                if (!item.HasCertificate)
                {
                    summary.NoCertificate++;
                }

                if (item.IsExternallyManaged)
                {
                    summary.ExternallyManaged++;
                }

                if (Enum.TryParse(item.Status, true, out ManagedCertificateHealth health))
                {
                    switch (health)
                    {
                        case ManagedCertificateHealth.OK:
                            summary.Healthy++;
                            break;
                        case ManagedCertificateHealth.Warning:
                            summary.Warning++;
                            break;
                        case ManagedCertificateHealth.Error:
                            summary.Error++;
                            break;
                        case ManagedCertificateHealth.AwaitingUser:
                            summary.AwaitingUser++;
                            break;
                    }
                }
            }

            return summary;
        }
    }
}
