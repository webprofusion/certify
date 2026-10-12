using System.CommandLine;

namespace Certify.Server.Hub.Cli.Cli
{
    internal static class CommandActions
    {
        /// <summary>
        /// Set a command's action, reporting a <see cref="CliException"/> as a message and its exit code.
        /// </summary>
        public static Command WithAction(this Command command, Func<ParseResult, CancellationToken, Task<int>> action)
        {
            command.SetAction(async (parseResult, cancellationToken) =>
            {
                try
                {
                    return await action(parseResult, cancellationToken);
                }
                catch (CliException ex)
                {
                    ConsoleIO.Error(ex.Message);
                    return ex.ExitCode;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    ConsoleIO.Error("Cancelled.");
                    return ExitCodes.Failed;
                }
            });

            return command;
        }
    }
}
