using System.CommandLine;
using System.Text.Json;
using Certify.Server.Hub.Cli.Hub;

namespace Certify.Server.Hub.Cli.Cli
{
    /// <summary>
    /// Options which apply to every hub API command.
    /// </summary>
    internal static class GlobalOptions
    {
        public static readonly Option<string?> Url = new("--url")
        {
            Description = $"Hub URL, e.g. https://hub.example.com:9696. Defaults to {HubSettings.UrlVariable}, then the saved profile.",
            Recursive = true
        };

        public static readonly Option<string?> Profile = new("--profile")
        {
            Description = "Saved sign in profile to use (default: default).",
            Recursive = true
        };

        public static readonly Option<string?> ClientId = new("--client-id")
        {
            Description = $"API token client id. Defaults to {HubSettings.ClientIdVariable}.",
            Recursive = true
        };

        public static readonly Option<string?> ClientSecret = new("--client-secret")
        {
            Description = $"API token secret. Prefer {HubSettings.ClientSecretVariable} or --client-secret-stdin, which keep it out of the process list.",
            Recursive = true
        };

        public static readonly Option<bool> ClientSecretStdin = new("--client-secret-stdin")
        {
            Description = "Read the API token secret from stdin.",
            Recursive = true
        };

        public static readonly Option<bool> Insecure = new("--insecure")
        {
            Description = $"Do not validate the hub's TLS certificate. For testing only. Defaults to {HubSettings.InsecureVariable}.",
            Recursive = true
        };

        public static readonly Option<string?> CaCert = new("--ca-cert")
        {
            Description = $"Trust a private CA for the hub's TLS certificate (PEM or DER file). Defaults to {HubSettings.CaCertVariable}.",
            Recursive = true
        };

        public static IEnumerable<Option> All => [Url, Profile, ClientId, ClientSecret, ClientSecretStdin, Insecure, CaCert];

        public static Option<string> CreateOutputOption()
        {
            var option = new Option<string>("--output", "-o")
            {
                Description = "Output format.",
                DefaultValueFactory = _ => "table"
            };

            option.AcceptOnlyFromAmong("table", "json");
            return option;
        }

        public static bool IsJsonOutput(ParseResult parseResult, Option<string> output) => parseResult.GetValue(output) == "json";

        /// <summary>
        /// Resolve the hub and credentials for this invocation and connect.
        /// </summary>
        public static HubConnection Connect(ParseResult parseResult)
        {
            var secret = parseResult.GetValue(ClientSecret);

            if (parseResult.GetValue(ClientSecretStdin))
            {
                secret = ConsoleIO.ReadStdinLine("API token secret");
            }

            var inputs = new HubCliInputs
            {
                Url = parseResult.GetValue(Url),
                ProfileName = parseResult.GetValue(Profile),
                ClientId = parseResult.GetValue(ClientId),
                ClientSecret = secret,
                Insecure = parseResult.GetValue(Insecure),
                CaCertPath = parseResult.GetValue(CaCert)
            };

            var profiles = new ProfileStore();
            var settings = HubSettings.Resolve(inputs, Environment.GetEnvironmentVariable, profiles.Get);

            return new HubConnection(settings, profiles);
        }

        /// <summary>
        /// Read a JSON request body given as a literal, @file, or - for stdin.
        /// </summary>
        public static string? ReadBody(string? body)
        {
            if (body == null)
            {
                return null;
            }

            var json = body switch
            {
                "-" => Console.In.ReadToEnd(),
                _ when body.StartsWith('@') => File.Exists(body[1..]) ? File.ReadAllText(body[1..]) : throw CliException.Usage($"Body file not found: {body[1..]}"),
                _ => body
            };

            try
            {
                using var _ = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                throw CliException.Usage($"The request body is not valid JSON - {ex.Message}");
            }

            return json;
        }
    }
}
