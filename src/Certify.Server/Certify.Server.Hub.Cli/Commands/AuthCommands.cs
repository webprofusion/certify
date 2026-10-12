using System.CommandLine;
using System.Net;
using System.Text.Json;
using Certify.Server.Hub.Cli.Cli;
using Certify.Server.Hub.Cli.Hub;

namespace Certify.Server.Hub.Cli.Commands
{
    internal static class AuthCommands
    {
        public static IEnumerable<Command> Create() => [CreateLogin(), CreateLogout(), CreateStatus()];

        private static Command CreateLogin()
        {
            var username = new Option<string?>("--username", "-u") { Description = "Username to sign in as." };
            var passwordStdin = new Option<bool>("--password-stdin") { Description = "Read the password from stdin." };

            var command = new Command("login", "Sign in to a hub with a username and password, saving the session in a profile. Hubs which only allow single sign on need an API token instead.")
            {
                username,
                passwordStdin
            };

            return command.WithAction(async (parseResult, cancellationToken) =>
            {
                var profiles = new ProfileStore();
                var profileName = parseResult.GetValue(GlobalOptions.Profile) ?? ProfileStore.DefaultProfileName;
                var existing = profiles.Get(profileName);

                var url = new[] { parseResult.GetValue(GlobalOptions.Url), Environment.GetEnvironmentVariable(HubSettings.UrlVariable), existing?.Url }
                    .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                    ?? throw CliException.Usage("No hub URL given, use --url <hub url>.");

                var baseUri = HubSettings.ParseHubUrl(url);

                var user = parseResult.GetValue(username);

                if (string.IsNullOrWhiteSpace(user))
                {
                    user = ConsoleIO.Prompt("Username");
                }

                var password = parseResult.GetValue(passwordStdin) ? ConsoleIO.ReadStdinLine("password") : ConsoleIO.PromptSecret("Password");

                var settings = new HubSettings
                {
                    BaseUri = baseUri,
                    AuthMode = HubAuthMode.None,
                    ProfileName = profileName,
                    Insecure = parseResult.GetValue(GlobalOptions.Insecure) || string.Equals(Environment.GetEnvironmentVariable(HubSettings.InsecureVariable), "true", StringComparison.OrdinalIgnoreCase),
                    CaCertPath = parseResult.GetValue(GlobalOptions.CaCert) ?? Environment.GetEnvironmentVariable(HubSettings.CaCertVariable)
                };

                using var connection = new HubConnection(settings, profiles);

                var request = JsonSerializer.Serialize(new LoginRequest { Username = user, Password = password }, CliJsonContext.Default.LoginRequest);

                using var response = await connection.SendRawAsync(HttpMethod.Post, "api/v1/auth/login", request, authenticate: false, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw CliException.Authentication($"Sign in failed: {HubConnection.DescribeBody(body) ?? "invalid username or password"}");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw HubConnection.ErrorFromResponse(response, body);
                }

                var session = HubConnection.ParseSession(body) ?? throw new CliException("The hub accepted the sign in but returned no session tokens.");

                HubConnection.SaveSession(profiles, profileName, baseUri, user, session);

                ConsoleIO.Info($"Signed in to {baseUri} as {session.Username ?? user} (profile '{profileName}').");
                return ExitCodes.Success;
            });
        }

        private static Command CreateLogout()
        {
            var command = new Command("logout", "Forget the saved session for a profile.");

            return command.WithAction((parseResult, _) =>
            {
                var profileName = parseResult.GetValue(GlobalOptions.Profile) ?? ProfileStore.DefaultProfileName;

                ConsoleIO.Info(new ProfileStore().Delete(profileName)
                    ? $"Signed out (profile '{profileName}' removed)."
                    : $"No saved profile '{profileName}'.");

                return Task.FromResult(ExitCodes.Success);
            });
        }

        private static Command CreateStatus()
        {
            var command = new Command("status", "Show which hub and credentials are in use, and check them against the hub.");

            return command.WithAction(async (parseResult, cancellationToken) =>
            {
                using var connection = GlobalOptions.Connect(parseResult);
                var settings = connection.Settings;

                Console.Out.WriteLine($"Hub:            {settings.BaseUri}");

                var version = await connection.SendAsync(HttpMethod.Get, "api/v1/system/version", null, authenticate: false, cancellationToken);
                Console.Out.WriteLine($"Hub version:    {DescribeVersion(version)}");

                switch (settings.AuthMode)
                {
                    case HubAuthMode.ApiToken:
                        Console.Out.WriteLine($"Authentication: API token (client id {settings.ClientId})");
                        break;

                    case HubAuthMode.Login:
                        Console.Out.WriteLine($"Authentication: signed in as {settings.Profile?.Username} (profile '{settings.ProfileName}')");
                        break;

                    default:
                        Console.Out.WriteLine("Authentication: none");
                        return ExitCodes.AuthenticationRequired;
                }

                await connection.SendAsync(HttpMethod.Get, "api/v1/auth/status", null, authenticate: true, cancellationToken);

                if (settings.AuthMode == HubAuthMode.Login && connection.AccessTokenExpiry is DateTimeOffset expiry)
                {
                    Console.Out.WriteLine($"Session:        valid, access token renews at {expiry.ToLocalTime():g}");
                }
                else
                {
                    Console.Out.WriteLine("Credentials:    valid");
                }

                return ExitCodes.Success;
            });
        }

        private static string DescribeVersion(string body)
        {
            var node = HubConnection.TryParseObject(body);

            if (node?.GetValueKind() == JsonValueKind.Object)
            {
                var version = node["version"] ?? node["productVersion"];

                if (version?.GetValueKind() == JsonValueKind.String)
                {
                    return version.GetValue<string>();
                }
            }

            return body.Trim().Trim('"');
        }
    }
}
