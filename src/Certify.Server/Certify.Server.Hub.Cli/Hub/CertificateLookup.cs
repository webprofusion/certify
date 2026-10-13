using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Certify.Server.Hub.Cli.Cli;

namespace Certify.Server.Hub.Cli.Hub
{
    /// <summary>
    /// A managed certificate on one of the hub's instances, as listed by the hub.
    /// </summary>
    internal sealed record CertificateRef(string InstanceId, string? InstanceTitle, string Id, string? Title, JsonObject? Summary)
    {
        public string Describe() => string.IsNullOrWhiteSpace(Title) ? Id : $"'{Title}'";
    }

    /// <summary>
    /// Finds a managed certificate from what a person would naturally give: its id, its title, or one of its domains.
    /// The hub identifies a certificate by instance and certificate id, which nobody remembers.
    /// </summary>
    internal static class CertificateLookup
    {
        private const int PageSize = 500;
        private const int MaxPages = 200;

        public static async Task<CertificateRef> ResolveAsync(HubConnection connection, string certificate, string? instance, CancellationToken cancellationToken)
        {
            return Resolve(await ListAsync(connection, certificate, cancellationToken), certificate, instance);
        }

        /// <summary>
        /// Pick the one certificate matching an id, title or domain, optionally on a given instance (by id or title).
        /// An exact id match wins over a title or domain match.
        /// </summary>
        public static CertificateRef Resolve(IReadOnlyList<JsonObject> items, string certificate, string? instance)
        {
            var key = certificate.Trim();

            var candidates = items
                .Where(i => string.IsNullOrWhiteSpace(instance) || Matches(Text(i["instanceId"]), instance) || Matches(Text(i["instanceTitle"]), instance))
                .ToList();

            var matches = candidates.Where(i => Matches(Text(i["id"]), key)).ToList();

            if (matches.Count == 0)
            {
                matches = candidates.Where(i => Matches(Text(i["title"]), key) || GetIdentifiers(i).Any(d => Matches(d, key))).ToList();
            }

            var onInstance = string.IsNullOrWhiteSpace(instance) ? string.Empty : $" on instance '{instance}'";

            return matches.Count switch
            {
                1 => ToRef(matches[0]),
                0 => throw new CliException($"No certificate found matching '{key}'{onInstance}. Run 'certify-hub cert list' to see the certificates you can access."),
                _ => throw CliException.Usage(
                    $"'{key}' matches {matches.Count} certificates{onInstance}. Choose one with --instance, or give its id:{Environment.NewLine}" +
                    string.Join(Environment.NewLine, matches.Select(m => $"  {Text(m["title"])}  (instance {Text(m["instanceTitle"])} {Text(m["instanceId"])}, id {Text(m["id"])})")))
            };
        }

        private static CertificateRef ToRef(JsonObject item) => new(Text(item["instanceId"]), Text(item["instanceTitle"]), Text(item["id"]), Text(item["title"]), item);

        private static IEnumerable<string> GetIdentifiers(JsonObject item)
        {
            if (item["identifiers"] is JsonArray identifiers)
            {
                foreach (var identifier in identifiers)
                {
                    yield return Text(identifier?["value"]);
                }
            }

            yield return Text(item["primaryIdentifier"]?["value"]);
        }

        private static bool Matches(string value, string key) => !string.IsNullOrEmpty(value) && string.Equals(value, key, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Every certificate the caller can see, a page at a time.
        /// </summary>
        private static async Task<List<JsonObject>> ListAsync(HubConnection connection, string certificate, CancellationToken cancellationToken)
        {
            var items = new List<JsonObject>();

            for (var page = 0; page < MaxPages; page++)
            {
                var url = $"internal/v1/hub/items?page={page.ToString(CultureInfo.InvariantCulture)}&pageSize={PageSize.ToString(CultureInfo.InvariantCulture)}";

                using var response = await connection.SendRawAsync(HttpMethod.Get, url, null, authenticate: true, cancellationToken);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    // the hub reports a missing permission as 401 too, so this covers both
                    throw CliException.Authentication(
                        $"Could not list certificates to find '{certificate}' ({(int)response.StatusCode}). Check the credentials, or if they only allow certificate download, give the instance id with --instance and the certificate id.");
                }

                var result = JsonNode.Parse(await HubConnection.ReadSuccessAsync(response, cancellationToken));
                var results = result?["results"] as JsonArray ?? [];

                items.AddRange(results.OfType<JsonObject>());

                var total = result?["totalResults"]?.GetValueKind() == JsonValueKind.Number ? result["totalResults"]!.GetValue<long>() : items.Count;

                if (results.Count == 0 || items.Count >= total)
                {
                    break;
                }
            }

            return items;
        }

        internal static string Text(JsonNode? node)
        {
            return node?.GetValueKind() switch
            {
                JsonValueKind.String => node.GetValue<string>(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => node.ToJsonString(),
                _ => string.Empty
            };
        }
    }
}
