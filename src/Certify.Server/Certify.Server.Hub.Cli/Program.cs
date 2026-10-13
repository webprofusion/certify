using System.CommandLine;
using Certify.Server.Hub.Cli.Cli;
using Certify.Server.Hub.Cli.Commands;

var root = new RootCommand(
    "Certify Management Hub CLI. Call the hub API as a signed in user or with an API token, " +
    "or use 'admin' on the hub machine for break-glass administration.");

// hub API commands share the connection options, the local admin commands have their own
foreach (var command in AuthCommands.Create().Append(ApiCommands.Create()).Concat(ItemCommands.Create()))
{
    foreach (var option in GlobalOptions.All)
    {
        command.Options.Add(option);
    }

    root.Subcommands.Add(command);
}

root.Subcommands.Add(AdminCommands.Create());

// response files are off, as they would expand the "@file" form of --body into arguments
return await root.Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null }).InvokeAsync();
