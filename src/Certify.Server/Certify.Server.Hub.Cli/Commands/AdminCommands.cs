using System.CommandLine;
using System.Text.Json;
using Certify.Models.Hub;
using Certify.Server.Hub.Cli.Cli;
using Certify.Server.Hub.Cli.LocalAdmin;

namespace Certify.Server.Hub.Cli.Commands
{
    /// <summary>
    /// Break-glass administration over the hub's local admin channel. These run on the hub machine itself, as an
    /// administrator (Windows) or as root / the hub service user, and need no hub sign in: they exist to recover a hub
    /// nobody can sign in to.
    /// </summary>
    internal static class AdminCommands
    {
        private static readonly Option<string?> PipeName = new("--pipe-name")
        {
            Description = $"Windows: the hub's local admin pipe name. Defaults to {LocalAdminChannel.PipeNameVariable}, then {LocalAdminChannel.DefaultPipeName}.",
            Recursive = true
        };

        private static readonly Option<string?> SocketPath = new("--socket")
        {
            Description = $"Linux/macOS: the hub's local admin socket. Defaults to {LocalAdminChannel.SocketPathVariable}, then {LocalAdminChannel.SystemdSocketPath} or {LocalAdminChannel.FallbackSocketPath}.",
            Recursive = true
        };

        public static Command Create()
        {
            var admin = new Command("admin", "Break-glass hub administration, on the hub machine only: run elevated on Windows, or as root / the hub service user elsewhere.")
            {
                PipeName,
                SocketPath
            };

            var principals = new Command("principals", "Security principals (users and applications).");
            principals.Subcommands.Add(CreatePrincipalList());

            admin.Subcommands.Add(principals);
            admin.Subcommands.Add(CreatePasswordReset());
            admin.Subcommands.Add(CreateGrantAdmin());
            admin.Subcommands.Add(CreateCreateAdmin());
            admin.Subcommands.Add(CreateEnablePasswordLogin());

            return admin;
        }

        private static LocalAdminClient Connect(ParseResult parseResult) => LocalAdminClient.Create(parseResult.GetValue(PipeName), parseResult.GetValue(SocketPath));

        private static Option<bool> CreateYesOption() => new("--yes", "-y") { Description = "Do not ask for confirmation." };

        private static Command CreatePrincipalList()
        {
            var output = GlobalOptions.CreateOutputOption();
            var command = new Command("list", "List security principals, showing which are administrators.") { output };

            return command.WithAction(async (parseResult, cancellationToken) =>
            {
                using var client = Connect(parseResult);
                var principals = await client.GetPrincipalsAsync(cancellationToken);

                if (GlobalOptions.IsJsonOutput(parseResult, output))
                {
                    Console.Out.WriteLine(JsonSerializer.Serialize(principals, CliJsonContext.Default.ListLocalAdminPrincipalSummary));
                    return ExitCodes.Success;
                }

                ConsoleIO.WriteTable(
                    ["Id", "Username", "Title", "Type", "Provider", "Administrator", "Built-in"],
                    principals.Select(p => new[] { p.Id, p.Username, p.Title, p.PrincipalType, p.Provider, p.IsAdministrator ? "yes" : "", p.IsBuiltIn ? "yes" : "" }));

                return ExitCodes.Success;
            });
        }

        private static Command CreatePasswordReset()
        {
            var principal = new Argument<string>("user") { Description = "Security principal id or username." };
            var yes = CreateYesOption();

            var command = new Command("password-reset", "Give a user a new, generated password. The new password is shown once.") { principal, yes };

            return command.WithAction(async (parseResult, cancellationToken) =>
            {
                var user = parseResult.GetValue(principal)!;
                Confirm(parseResult, yes, $"Reset the password for '{user}'? Their current password will stop working.");

                using var client = Connect(parseResult);
                var result = await client.PostAsync(LocalAdminChannel.PasswordResetRoute, new LocalAdminPrincipalRequest { Principal = user }, CliJsonContext.Default.LocalAdminPrincipalRequest, cancellationToken);

                return Report(result);
            });
        }

        private static Command CreateGrantAdmin()
        {
            var principal = new Argument<string>("user") { Description = "Security principal id or username." };
            var yes = CreateYesOption();

            var command = new Command("grant-admin", "Give a user the hub Administrator role.") { principal, yes };

            return command.WithAction(async (parseResult, cancellationToken) =>
            {
                var user = parseResult.GetValue(principal)!;
                Confirm(parseResult, yes, $"Give '{user}' the Administrator role, with full control of the hub?");

                using var client = Connect(parseResult);
                var result = await client.PostAsync(LocalAdminChannel.GrantAdministratorRoute, new LocalAdminPrincipalRequest { Principal = user }, CliJsonContext.Default.LocalAdminPrincipalRequest, cancellationToken);

                return Report(result);
            });
        }

        private static Command CreateCreateAdmin()
        {
            var username = new Argument<string>("username") { Description = "Username for the new administrator." };
            var title = new Option<string?>("--title") { Description = "Display name, defaults to the username." };

            var command = new Command("create-admin", "Add a new user with the Administrator role and a generated password, shown once.") { username, title };

            return command.WithAction(async (parseResult, cancellationToken) =>
            {
                using var client = Connect(parseResult);

                var request = new LocalAdminCreateAdministratorRequest { Username = parseResult.GetValue(username)!, Title = parseResult.GetValue(title) };
                var result = await client.PostAsync(LocalAdminChannel.CreateAdministratorRoute, request, CliJsonContext.Default.LocalAdminCreateAdministratorRequest, cancellationToken);

                return Report(result);
            });
        }

        private static Command CreateEnablePasswordLogin()
        {
            var command = new Command("enable-password-login", "Turn username and password sign in back on, e.g. when the hub's single sign on provider is unavailable.");

            return command.WithAction(async (parseResult, cancellationToken) =>
            {
                using var client = Connect(parseResult);
                var result = await client.PostAsync<LocalAdminPrincipalRequest>(LocalAdminChannel.EnablePasswordLoginRoute, null, CliJsonContext.Default.LocalAdminPrincipalRequest, cancellationToken);

                return Report(result);
            });
        }

        private static void Confirm(ParseResult parseResult, Option<bool> yes, string question)
        {
            if (!parseResult.GetValue(yes) && !ConsoleIO.Confirm(question))
            {
                throw new CliException("Cancelled, nothing was changed.");
            }
        }

        private static int Report(LocalAdminResult result)
        {
            if (!result.IsSuccess)
            {
                throw new CliException(result.Message);
            }

            ConsoleIO.Info(result.Message);

            if (!string.IsNullOrEmpty(result.GeneratedPassword))
            {
                // the password alone goes to stdout, so a script can capture it without parsing the messages
                ConsoleIO.Info($"New password for {result.Username ?? result.PrincipalId} (shown once, sign in and change it):");
                Console.Out.WriteLine(result.GeneratedPassword);
            }

            return ExitCodes.Success;
        }
    }
}
