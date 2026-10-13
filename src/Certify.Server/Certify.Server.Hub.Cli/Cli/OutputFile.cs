using System.CommandLine;

namespace Certify.Server.Hub.Cli.Cli
{
    /// <summary>
    /// Writes a response body to a file, or to stdout.
    /// </summary>
    internal static class OutputFile
    {
        public static Option<string?> CreateOption(string description) => new("--out", "-O") { Description = description };

        /// <summary>
        /// Write the content to the given file, or to stdout when no file (or "-") is given.
        ///
        /// On Linux and macOS a file is made readable by its owner only, before anything is written to it, since
        /// responses such as certificates with private keys are secrets. On Windows it takes the folder's permissions.
        /// </summary>
        /// <returns>The full path written, or null for stdout.</returns>
        public static string? Write(string? path, byte[] content)
        {
            if (string.IsNullOrWhiteSpace(path) || path == "-")
            {
                using var stdout = Console.OpenStandardOutput();
                stdout.Write(content);
                stdout.Flush();
                return null;
            }

            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (OperatingSystem.IsWindows())
            {
                File.WriteAllBytes(fullPath, content);
                return fullPath;
            }

            const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            if (File.Exists(fullPath))
            {
                File.SetUnixFileMode(fullPath, ownerOnly);
            }

            using (var stream = new FileStream(fullPath, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = ownerOnly }))
            {
                stream.Write(content);
            }

            return fullPath;
        }

        /// <summary>
        /// True when writing binary content to stdout would put it on a terminal rather than into a pipe or file.
        /// </summary>
        public static bool IsTerminal(string? path) => (string.IsNullOrWhiteSpace(path) || path == "-") && !Console.IsOutputRedirected;
    }
}
