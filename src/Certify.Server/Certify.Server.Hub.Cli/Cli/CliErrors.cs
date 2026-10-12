namespace Certify.Server.Hub.Cli.Cli
{
    /// <summary>
    /// Process exit codes, which scripts calling the CLI rely on.
    /// </summary>
    internal static class ExitCodes
    {
        public const int Success = 0;

        /// <summary>
        /// The hub refused or failed the request, or could not be reached.
        /// </summary>
        public const int Failed = 1;

        /// <summary>
        /// The command line was incomplete or invalid.
        /// </summary>
        public const int Usage = 2;

        /// <summary>
        /// No usable credentials: not signed in, the session expired, or an API token was rejected.
        /// </summary>
        public const int AuthenticationRequired = 3;
    }

    /// <summary>
    /// A failure reported to the user as a message and exit code, without a stack trace.
    /// </summary>
    internal sealed class CliException : Exception
    {
        public CliException(string message, int exitCode = ExitCodes.Failed) : base(message)
        {
            ExitCode = exitCode;
        }

        public int ExitCode { get; }

        public static CliException Usage(string message) => new(message, ExitCodes.Usage);

        public static CliException Authentication(string message) => new(message, ExitCodes.AuthenticationRequired);
    }
}
