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
            bool includeUntagged)
        {
            var scopes = TagScopeFilter.ParseAll(tagScopes);

            var managedItems = mgmtAPI.GetManagedInstanceItems();
            var instances = mgmtAPI.GetConnectedInstances();

            // TODO: would fetching cached hub status summaries be faster
            var knownInstances = await client.GetHubManagedInstances(PrincipalAccess.SystemAuthContext) ?? [];

            var tagsByItemId = await GetItemTagsByItemId(client, TaggedItemTypes.ManagedCertificate);

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
