using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Certify.Server.Hub.Cli.Cli;
using Certify.Server.Hub.Cli.Hub;

namespace Certify.Server.Hub.Cli.Commands
{
    /// <summary>
    /// Shortcuts for common tasks. Each is a single hub API call, which 'certify-hub api' can also make directly.
    /// </summary>
    internal static class ItemCommands
    {
        public static IEnumerable<Command> Create() => [CreateCert(), CreateInstance()];

        private static Command CreateCert()
        {
            var cert = new Command("cert", "Managed certificates across the hub's instances.");
            cert.Subcommands.Add(CreateCertList());
            cert.Subcommands.Add(CreateCertRenew());
            return cert;
        }

        private static Command CreateCertList()
        {
            var instance = new Option<string?>("--instance") { Description = "Only certificates on this instance id." };
            var keyword = new Option<string?>("--keyword", "-k") { Description = "Only certificates matching this text." };
            var page = new Option<int?>("--page") { Description = "Page number, starting at 0." };
            var pageSize = new Option<int?>("--page-size") { Description = "Results per page." };
            var output = GlobalOptions.CreateOutputOption();

            var command = new Command("list", "List managed certificates.") { instance, keyword, page, pageSize, output };

            return command.WithAction(async (parseResult, cancellationToken) =>
            {
                using var connection = GlobalOptions.Connect(parseResult);

                var query = new List<KeyValuePair<string, string>>();
                AddIfSet(query, "instanceId", parseResult.GetValue(instance));
                AddIfSet(query, "keyword", parseResult.GetValue(keyword));
                AddIfSet(query, "page", parseResult.GetValue(page)?.ToString(CultureInfo.InvariantCulture));
                AddIfSet(query, "pageSize", parseResult.GetValue(pageSize)?.ToString(CultureInfo.InvariantCulture));

                var json = await connection.SendAsync(HttpMethod.Get, OpenApiCatalog.BuildRelativeUrl("internal/v1/hub/items", query), null, authenticate: true, cancellationToken);

                if (GlobalOptions.IsJsonOutput(parseResult, output))
                {
                    Console.Out.WriteLine(json);
                    return ExitCodes.Success;
                }

                var result = JsonNode.Parse(json);
                var items = result?["results"]?.AsArray() ?? [];

                ConsoleIO.WriteTable(
                    ["Title", "Status", "Expires", "Instance", "Instance Id", "Id"],
                    items.Select(i => new[]
                    {
                        Text(i?["title"]),
                        Text(i?["status"]),
                        Date(i?["dateExpiry"]),
                        Text(i?["instanceTitle"]),
                        Text(i?["instanceId"]),
                        Text(i?["id"])
                    }));

                ConsoleIO.Info($"{items.Count} of {Text(result?["totalResults"])} certificates.");
                return ExitCodes.Success;
            });
        }

        private static Command CreateCertRenew()
        {
            var instanceId = new Argument<string>("instance-id") { Description = "Instance the certificate is managed on (see 'cert list')." };
            var certificateId = new Argument<string>("certificate-id") { Description = "Managed certificate id." };

            var command = new Command("renew", "Start a certificate request (renewal) now. The request runs on its instance, follow its progress in the hub.") { instanceId, certificateId };

            return command.WithAction(async (parseResult, cancellationToken) =>
            {
                using var connection = GlobalOptions.Connect(parseResult);

                var url = OpenApiCatalog.BuildRelativeUrl("internal/v1/certificate/order", [
                    new("instanceId", parseResult.GetValue(instanceId)!),
                    new("id", parseResult.GetValue(certificateId)!)
                ]);

                await connection.SendAsync(HttpMethod.Post, url, null, authenticate: true, cancellationToken);

                ConsoleIO.Info($"Certificate request started for {parseResult.GetValue(certificateId)} on instance {parseResult.GetValue(instanceId)}.");
                return ExitCodes.Success;
            });
        }

        private static Command CreateInstance()
        {
            var output = GlobalOptions.CreateOutputOption();
            var list = new Command("list", "List the managed instances connected to the hub.") { output };

            list.WithAction(async (parseResult, cancellationToken) =>
            {
                using var connection = GlobalOptions.Connect(parseResult);
                var json = await connection.SendAsync(HttpMethod.Get, "internal/v1/hub/instances", null, authenticate: true, cancellationToken);

                if (GlobalOptions.IsJsonOutput(parseResult, output))
                {
                    Console.Out.WriteLine(json);
                    return ExitCodes.Success;
                }

                var instances = JsonNode.Parse(json)?.AsArray() ?? [];

                ConsoleIO.WriteTable(
                    ["Instance Id", "Title", "OS", "Version", "Status", "Last Reported"],
                    instances.Select(i => new[]
                    {
                        Text(i?["instanceId"]),
                        string.IsNullOrWhiteSpace(Text(i?["customTitle"])) ? Text(i?["title"]) : Text(i?["customTitle"]),
                        Text(i?["os"]),
                        Text(i?["clientVersion"]),
                        Text(i?["connectionStatus"]),
                        Date(i?["dateLastReported"])
                    }));

                return ExitCodes.Success;
            });

            var instance = new Command("instance", "Managed instances connected to the hub.");
            instance.Subcommands.Add(list);
            return instance;
        }

        private static void AddIfSet(List<KeyValuePair<string, string>> query, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                query.Add(new(name, value));
            }
        }

        private static string Text(JsonNode? node)
        {
            return node?.GetValueKind() switch
            {
                JsonValueKind.String => node.GetValue<string>(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => node.ToJsonString(),
                _ => string.Empty
            };
        }

        private static string Date(JsonNode? node)
        {
            return DateTimeOffset.TryParse(Text(node), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year > 1
                ? date.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : string.Empty;
        }
    }
}
