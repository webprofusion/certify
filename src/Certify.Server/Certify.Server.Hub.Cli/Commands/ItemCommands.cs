using System.CommandLine;
using System.Text.Json.Nodes;
using Certify.Server.Hub.Cli.Cli;
using static Certify.Server.Hub.Cli.Commands.CertCommands;

namespace Certify.Server.Hub.Cli.Commands
{
    /// <summary>
    /// Shortcuts for common tasks, each of which 'certify-hub api' can also do directly.
    /// </summary>
    internal static class ItemCommands
    {
        public static IEnumerable<Command> Create() => [CertCommands.Create(), CreateInstance()];

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
    }
}
