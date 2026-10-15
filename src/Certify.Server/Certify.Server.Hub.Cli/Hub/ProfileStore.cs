using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Certify.Server.Hub.Cli.Cli;

namespace Certify.Server.Hub.Cli.Hub
{
    internal sealed class ProfileFile
    {
        public Dictionary<string, HubProfile> Profiles { get; set; } = [];
    }

    /// <summary>
    /// A signed in session with a hub. Tokens are held protected (see <see cref="ProfileStore"/>). API token secrets are
    /// never saved here, they are supplied per invocation.
    /// </summary>
    internal sealed class HubProfile
    {
        public string Url { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
    }

    /// <summary>
    /// Saved sign in profiles, in the user's own configuration directory.
    ///
    /// On Windows the tokens are encrypted with DPAPI for the current user. Elsewhere the file is only readable by its
    /// owner, the same protection other CLIs give their credential files. Either way the tokens are short lived: the
    /// hub forgets refresh tokens when it restarts, after which the user signs in again.
    /// </summary>
    internal sealed class ProfileStore
    {
        public const string DefaultProfileName = "default";

        /// <summary>
        /// Overrides the configuration directory, e.g. for an isolated automation account.
        /// </summary>
        public const string ConfigDirectoryVariable = "CERTIFY_HUB_CONFIG_DIR";

        private const string DpapiPrefix = "dpapi:";

        private readonly string _path;

        public ProfileStore(string? configDirectory = null)
        {
            _path = Path.Combine(configDirectory ?? GetDefaultConfigDirectory(), "profiles.json");
        }

        public static string GetDefaultConfigDirectory()
        {
            var configured = Environment.GetEnvironmentVariable(ConfigDirectoryVariable);

            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            if (OperatingSystem.IsWindows())
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "certify-hub");
            }

            var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var baseDirectory = string.IsNullOrWhiteSpace(xdgConfig)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                : xdgConfig;

            return Path.Combine(baseDirectory, "certify-hub");
        }

        public HubProfile? Get(string name)
        {
            var file = Load();
            return file.Profiles.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        }

        public void Save(string name, HubProfile profile)
        {
            var file = Load();
            Remove(file, name);
            file.Profiles[name] = profile;
            Write(file);
        }

        public bool Delete(string name)
        {
            var file = Load();

            if (!Remove(file, name))
            {
                return false;
            }

            Write(file);
            return true;
        }

        private static bool Remove(ProfileFile file, string name)
        {
            var key = file.Profiles.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            return key != null && file.Profiles.Remove(key);
        }

        private ProfileFile Load()
        {
            if (!File.Exists(_path))
            {
                return new ProfileFile();
            }

            try
            {
                return JsonSerializer.Deserialize(File.ReadAllText(_path), CliJsonContext.Default.ProfileFile) ?? new ProfileFile();
            }
            catch (JsonException ex)
            {
                throw new CliException($"Could not read the saved profiles in {_path} - {ex.Message}. Delete the file to sign in again.");
            }
        }

        private void Write(ProfileFile file)
        {
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            // made readable by its owner only before the tokens are written, including a file which already exists
            OutputFile.Write(_path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(file, CliJsonContext.Default.ProfileFile)));
        }

        public static string Protect(string value)
        {
            return OperatingSystem.IsWindows() ? DpapiPrefix + ProtectWindows(value) : value;
        }

        public static string? Unprotect(string? value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            {
                return value;
            }

            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            try
            {
                return UnprotectWindows(value[DpapiPrefix.Length..]);
            }
            catch (CryptographicException)
            {
                // e.g. a profile file copied from another user or machine
                return null;
            }
        }

        [SupportedOSPlatform("windows")]
        private static string ProtectWindows(string value) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

        [SupportedOSPlatform("windows")]
        private static string UnprotectWindows(string value) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
    }
}
