using System.Text;

namespace Certify.Server.Hub.Cli.Cli
{
    /// <summary>
    /// Console input and output. Results go to stdout and everything else to stderr, so that scripts can capture a
    /// command's output without prompts or messages mixed in.
    /// </summary>
    internal static class ConsoleIO
    {
        public static void Info(string message) => Console.Error.WriteLine(message);

        public static void Error(string message)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(message);
            Console.ForegroundColor = previous;
        }

        public static string Prompt(string label)
        {
            EnsureInteractive($"{label} is required");
            Console.Error.Write($"{label}: ");
            return Console.ReadLine()?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Read a secret without echoing it.
        /// </summary>
        public static string PromptSecret(string label)
        {
            EnsureInteractive($"{label} is required, supply it on stdin with the matching --*-stdin option");
            Console.Error.Write($"{label}: ");

            var secret = new StringBuilder();

            while (true)
            {
                var key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.Enter)
                {
                    break;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (secret.Length > 0)
                    {
                        secret.Length--;
                    }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    secret.Append(key.KeyChar);
                }
            }

            Console.Error.WriteLine();
            return secret.ToString();
        }

        /// <summary>
        /// Read one line from stdin, for secrets piped in by scripts.
        /// </summary>
        public static string ReadStdinLine(string what)
        {
            var line = Console.In.ReadLine();

            if (string.IsNullOrEmpty(line))
            {
                throw CliException.Usage($"Expected the {what} on stdin.");
            }

            return line.TrimEnd('\r', '\n');
        }

        public static bool Confirm(string question)
        {
            EnsureInteractive("Confirmation is required, pass --yes to confirm non-interactively");
            Console.Error.Write($"{question} [y/N]: ");
            var answer = Console.ReadLine()?.Trim();
            return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureInteractive(string message)
        {
            if (Console.IsInputRedirected)
            {
                throw CliException.Usage(message);
            }
        }

        /// <summary>
        /// Write rows as a plain aligned table on stdout.
        /// </summary>
        public static void WriteTable(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
        {
            var data = rows.Select(r => r.Select(v => v ?? string.Empty).ToArray()).ToList();
            var widths = headers.Select((h, i) => Math.Max(h.Length, data.Count == 0 ? 0 : data.Max(r => i < r.Length ? r[i].Length : 0))).ToArray();

            string Line(IReadOnlyList<string> values) => string.Join("  ", values.Select((v, i) => i == values.Count - 1 ? v : v.PadRight(widths[i]))).TrimEnd();

            Console.Out.WriteLine(Line(headers));
            Console.Out.WriteLine(Line(widths.Select(w => new string('-', w)).ToArray()));

            foreach (var row in data)
            {
                Console.Out.WriteLine(Line(row));
            }
        }
    }
}
