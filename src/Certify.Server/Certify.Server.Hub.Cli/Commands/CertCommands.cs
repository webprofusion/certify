using System.CommandLine;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Certify.Server.Hub.Cli.Cli;
using Certify.Server.Hub.Cli.Hub;

namespace Certify.Server.Hub.Cli.Commands
{
    /// <summary>
    /// Managed certificate shortcuts. A certificate can be given by id, title or domain, see <see cref="CertificateLookup"/>.
    /// </summary>
    internal static class CertCommands
    {
        /// <summary>
        /// Certificate formats the hub exports, as named by its download API.
        /// </summary>
        public static readonly string[] DownloadFormats = ["pfx", "pem_fullchain", "pem_key", "pem_fullchain_key", "pem_fullchain_root", "pem_fullchain_root_key"];

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

        public static Command Create()
        {
            var cert = new Command("cert", "Managed certificates across the hub's instances.");
            cert.Subcommands.Add(CreateList());
            cert.Subcommands.Add(CreateShow());
            cert.Subcommands.Add(CreateDownload());
            cert.Subcommands.Add(CreateRenew());
            return cert;
        }

        private static Argument<string> CreateCertificateArgument() => new("certificate") { Description = "Certificate id, title, or one of its domains." };

        private static Option<string?> CreateInstanceOption() => new("--instance") { Description = "Instance id or title, where the same certificate name or domain is on more than one instance." };

        private static Command CreateList()
        {
            var instance = new Option<string?>("--instance") { Description = "Only certificates on this instance id." };
            var keyword = new Option<string?>("--keyword", "-k") { Description = "Only certificates whose title contains this text." };
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

        private static Command CreateShow()
        {
            var certificate = CreateCertificateArgument();
            var instance = CreateInstanceOption();
            var output = GlobalOptions.CreateOutputOption();

            var command = new Command("show", "Show a certificate's status, validity and most recent renewal.") { certificate, instance, output };

            return command.WithAction(async (parseResult, cancellationToken) =>
            {
                using var connection = GlobalOptions.Connect(parseResult);

                var found = await CertificateLookup.ResolveAsync(connection, parseResult.GetValue(certificate)!, parseResult.GetValue(instance), cancellationToken);
                var json = await connection.SendAsync(HttpMethod.Get, DetailsUrl(found), null, authenticate: true, cancellationToken);

                if (GlobalOptions.IsJsonOutput(parseResult, output))
                {
                    Console.Out.WriteLine(json);
                    return ExitCodes.Success;
                }

                var details = JsonNode.Parse(json) as JsonObject ?? throw new CliException("The hub returned no details for this certificate.");
                var summary = found.Summary;

                var domains = (summary?["identifiers"] as JsonArray ?? []).Select(d => Text(d?["value"])).Where(d => d.Length > 0);
                var tags = (summary?["tags"] as JsonArray ?? []).Select(t => Text(t?["title"])).Where(t => t.Length > 0);
                var failures = details["renewalFailureCount"]?.GetValueKind() == JsonValueKind.Number ? details["renewalFailureCount"]!.GetValue<int>() : 0;
                var failureMessage = Text(details["renewalFailureMessage"]);

                var rows = new List<(string Field, string Value)>
                {
                    ("Title", Text(details["name"])),
                    ("Id", found.Id),
                    ("Instance", $"{found.InstanceTitle} ({found.InstanceId})"),
                    ("Status", Text(summary?["status"])),
                    ("Domains", string.Join(", ", domains)),
                    ("Issuer", FirstText(summary?["certificateAuthorityTitle"], summary?["issuer"])),
                    ("Key type", Text(summary?["keyType"])),
                    ("Valid from", Date(details["dateStart"])),
                    ("Expires", DescribeExpiry(details["dateExpiry"])),
                    ("Last renewed", Date(details["dateRenewed"])),
                    ("Last attempt", JoinNonEmpty(Date(details["dateLastRenewalAttempt"]), DescribeState(details["lastRenewalStatus"]))),
                    ("Failures", failures > 0 ? $"{failures}: {failureMessage}" : string.Empty),
                    ("Next attempt", Date(details["dateNextScheduledRenewalAttempt"])),
                    ("Auto renew", details["includeInAutoRenew"]?.GetValueKind() == JsonValueKind.True ? "yes" : "no"),
                    ("Thumbprint", Text(details["certificateThumbprintHash"])),
                    ("Tags", string.Join(", ", tags))
                };

                var width = rows.Max(r => r.Field.Length);

                foreach (var (field, value) in rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)))
                {
                    Console.Out.WriteLine($"{(field + ":").PadRight(width + 2)}{value}");
                }

                return ExitCodes.Success;
            });
        }

        private static Command CreateDownload()
        {
            var certificate = CreateCertificateArgument();
            var instance = CreateInstanceOption();

            var format = new Option<string>("--format", "-f")
            {
                Description = "pfx (PKCS#12 with key), pem_fullchain (certificate and intermediates), pem_key (private key), pem_fullchain_key, pem_fullchain_root, or pem_fullchain_root_key.",
                DefaultValueFactory = _ => "pfx"
            };
            format.AcceptOnlyFromAmong(DownloadFormats);

            var output = OutputFile.CreateOption("File to save to. Without it PEM formats are written to stdout, and a PFX only when stdout is redirected.");

            var command = new Command("download", "Download the current certificate, e.g. to deploy it from a script.") { certificate, instance, format, output };

            return command.WithAction(async (parseResult, cancellationToken) =>
            {
                var formatName = parseResult.GetValue(format)!;
                var outPath = parseResult.GetValue(output);

                if (formatName == "pfx" && OutputFile.IsTerminal(outPath))
                {
                    throw CliException.Usage("A PFX file is binary, save it with --out <file> or redirect stdout.");
                }

                using var connection = GlobalOptions.Connect(parseResult);

                var certificateName = parseResult.GetValue(certificate)!.Trim();
                var instanceName = parseResult.GetValue(instance)?.Trim();

                // An instance and certificate id are tried as given first: a token which may download certificates may not
                // be allowed to list them, which looking up a name or domain needs.
                byte[]? content = null;
                var description = certificateName;

                if (!string.IsNullOrWhiteSpace(instanceName))
                {
                    content = await TryDownloadAsync(connection, instanceName, certificateName, formatName, cancellationToken);
                }

                if (content == null)
                {
                    var found = await CertificateLookup.ResolveAsync(connection, certificateName, instanceName, cancellationToken);
                    description = found.Describe();
                    content = await TryDownloadAsync(connection, found.InstanceId, found.Id, formatName, cancellationToken)
                        ?? throw new CliException($"The certificate {description} was not found on its instance.");
                }

                var written = OutputFile.Write(outPath, content);

                ConsoleIO.Info(written == null ? $"Wrote {formatName} for {description}." : $"Saved {formatName} for {description} to {written}");
                return ExitCodes.Success;
            });
        }

        /// <summary>
        /// Download a certificate, returning null when the hub does not know it so that the caller can look it up.
        /// </summary>
        private static async Task<byte[]?> TryDownloadAsync(HubConnection connection, string instanceId, string certificateId, string format, CancellationToken cancellationToken)
        {
            var url = $"api/v1/certificate/{Uri.EscapeDataString(instanceId)}/download/{Uri.EscapeDataString(certificateId)}/{format}";

            using var response = await connection.SendRawAsync(HttpMethod.Get, url, null, authenticate: true, cancellationToken);

            return response.StatusCode == HttpStatusCode.NotFound ? null : await HubConnection.ReadSuccessAsync(response, cancellationToken);
        }

        private static Command CreateRenew()
        {
            var certificate = CreateCertificateArgument();
            var instance = CreateInstanceOption();
            var wait = new Option<bool>("--wait", "-w") { Description = "Wait for the request to finish, and exit with 0 only if it succeeded." };
            var timeout = new Option<int>("--timeout")
            {
                Description = "Seconds to wait with --wait before giving up.",
                DefaultValueFactory = _ => 900
            };

            var command = new Command("renew", "Start a certificate request (renewal) now. It runs on the certificate's instance.") { certificate, instance, wait, timeout };

            return command.WithAction(async (parseResult, cancellationToken) =>
            {
                using var connection = GlobalOptions.Connect(parseResult);

                var found = await CertificateLookup.ResolveAsync(connection, parseResult.GetValue(certificate)!, parseResult.GetValue(instance), cancellationToken);
                var waitForResult = parseResult.GetValue(wait);

                // the hub starts the request and returns, with no handle on its outcome. The certificate records when
                // its last attempt finished, so a change to that marks this request finishing.
                var previousAttempt = waitForResult ? await GetLastAttemptAsync(connection, found, cancellationToken) : null;

                var url = OpenApiCatalog.BuildRelativeUrl("internal/v1/certificate/order", [new("instanceId", found.InstanceId), new("id", found.Id)]);
                await connection.SendAsync(HttpMethod.Post, url, null, authenticate: true, cancellationToken);

                if (!waitForResult)
                {
                    ConsoleIO.Info($"Certificate request started for {found.Describe()} on instance {found.InstanceTitle}.");
                    return ExitCodes.Success;
                }

                ConsoleIO.Info($"Certificate request started for {found.Describe()} on instance {found.InstanceTitle}, waiting for it to finish...");

                return await WaitForRequestAsync(connection, found, previousAttempt, TimeSpan.FromSeconds(Math.Max(1, parseResult.GetValue(timeout))), cancellationToken);
            });
        }

        private static async Task<string?> GetLastAttemptAsync(HubConnection connection, CertificateRef found, CancellationToken cancellationToken)
        {
            var details = JsonNode.Parse(await connection.SendAsync(HttpMethod.Get, DetailsUrl(found), null, authenticate: true, cancellationToken));
            return Text(details?["dateLastRenewalAttempt"]);
        }

        private static async Task<int> WaitForRequestAsync(HubConnection connection, CertificateRef found, string? previousAttempt, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;

            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(PollInterval, cancellationToken);

                JsonObject? details;

                try
                {
                    details = JsonNode.Parse(await connection.SendAsync(HttpMethod.Get, DetailsUrl(found), null, authenticate: true, cancellationToken)) as JsonObject;
                }
                catch (CliException ex) when (ex.ExitCode == ExitCodes.Failed)
                {
                    // e.g. the instance briefly not answering the hub while it works on the request, keep waiting
                    continue;
                }

                var lastAttempt = Text(details?["dateLastRenewalAttempt"]);

                if (details == null || string.IsNullOrEmpty(lastAttempt) || lastAttempt == previousAttempt)
                {
                    continue;
                }

                return ReportOutcome(found, details);
            }

            throw new CliException($"Timed out after {timeout.TotalSeconds:0} seconds waiting for the request to finish. It may still be running, check with 'certify-hub cert show {found.Id} --instance {found.InstanceId}'.");
        }

        private static int ReportOutcome(CertificateRef found, JsonObject details)
        {
            var state = details["lastRenewalStatus"]?.GetValueKind() == JsonValueKind.Number ? details["lastRenewalStatus"]!.GetValue<int>() : -1;
            var message = Text(details["renewalFailureMessage"]);

            switch (state)
            {
                case RequestStateSuccess:
                    ConsoleIO.Info($"Certificate request succeeded for {found.Describe()}. Expires {DescribeExpiry(details["dateExpiry"])}.");
                    return ExitCodes.Success;

                case RequestStatePaused:
                    throw new CliException($"The request for {found.Describe()} is waiting for someone to complete a step, e.g. creating a manual DNS record. {message}".Trim());

                default:
                    throw new CliException($"The request for {found.Describe()} finished as '{DescribeState(details["lastRenewalStatus"])}': {(string.IsNullOrWhiteSpace(message) ? "see the certificate's log in the hub" : message)}");
            }
        }

        private const int RequestStateSuccess = 3;
        private const int RequestStatePaused = 4;

        /// <summary>
        /// The hub's request states, which it serializes as numbers.
        /// </summary>
        private static string DescribeState(JsonNode? node)
        {
            if (node?.GetValueKind() != JsonValueKind.Number)
            {
                return string.Empty;
            }

            return node.GetValue<int>() switch
            {
                0 => "Not run",
                1 => "Running",
                2 => "Failed",
                3 => "Succeeded",
                4 => "Waiting for user",
                5 => "Warning",
                6 => "Queued",
                7 => "Skipped",
                var other => other.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static string DetailsUrl(CertificateRef found) => $"internal/v1/certificate/{Uri.EscapeDataString(found.InstanceId)}/settings/{Uri.EscapeDataString(found.Id)}";

        private static string DescribeExpiry(JsonNode? node)
        {
            if (!TryParseDate(node, out var expiry))
            {
                return string.Empty;
            }

            var days = (int)Math.Floor((expiry - DateTimeOffset.UtcNow).TotalDays);
            var remaining = days < 0 ? "expired" : $"{days} days left";
            return $"{expiry.ToLocalTime():yyyy-MM-dd HH:mm} ({remaining})";
        }

        private static void AddIfSet(List<KeyValuePair<string, string>> query, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                query.Add(new(name, value));
            }
        }

        private static string FirstText(params JsonNode?[] nodes) => nodes.Select(Text).FirstOrDefault(t => t.Length > 0) ?? string.Empty;

        private static string JoinNonEmpty(params string[] values) => string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v)));

        internal static string Text(JsonNode? node) => CertificateLookup.Text(node);

        private static bool TryParseDate(JsonNode? node, out DateTimeOffset date)
        {
            return DateTimeOffset.TryParse(Text(node), CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && date.Year > 1;
        }

        internal static string Date(JsonNode? node) => TryParseDate(node, out var date) ? date.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : string.Empty;
    }
}
