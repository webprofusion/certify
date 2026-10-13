using System.CommandLine;
using System.Text.Json;

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
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException or InvalidOperationException)
                {
                    // e.g. an output file which cannot be written, or a response which is not what was expected
                    ConsoleIO.Error(ex.Message);
                    return ExitCodes.Failed;
                }
            });

            return command;
        }
    }
}
