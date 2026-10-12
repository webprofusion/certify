using Certify.Server.Hub.Cli.Cli;

namespace Certify.Server.Hub.Cli.Hub
{
    internal enum HubAuthMode
    {
        None,

        /// <summary>
        /// A hub API access token (client id and secret), for tooling and automation.
        /// </summary>
        ApiToken,

        /// <summary>
        /// A session from <c>certify-hub login</c>, saved in a profile.
        /// </summary>
        Login
    }

    /// <summary>
    /// Connection settings as given on the command line, before environment variables and saved profiles are applied.
    /// </summary>
    internal sealed class HubCliInputs
    {
        public string? Url { get; init; }
        public string? ProfileName { get; init; }
        public string? ClientId { get; init; }
        public string? ClientSecret { get; init; }
        public bool Insecure { get; init; }
        public string? CaCertPath { get; init; }
    }

    internal sealed class HubSettings
    {
        public const string UrlVariable = "CERTIFY_HUB_URL";
        public const string ClientIdVariable = "CERTIFY_HUB_CLIENT_ID";
        public const string ClientSecretVariable = "CERTIFY_HUB_CLIENT_SECRET";
        public const string InsecureVariable = "CERTIFY_HUB_INSECURE";
        public const string CaCertVariable = "CERTIFY_HUB_CA_CERT";

        public required Uri BaseUri { get; init; }
        public required HubAuthMode AuthMode { get; init; }
        public required string ProfileName { get; init; }
        public HubProfile? Profile { get; init; }
        public string? ClientId { get; init; }
        public string? ClientSecret { get; init; }
        public bool Insecure { get; init; }
        public string? CaCertPath { get; init; }

        /// <summary>
        /// Resolve the hub and credentials to use. For each setting the command line wins over the environment, which
        /// wins over the saved profile. An API token is used in preference to a saved sign in, and a saved sign in is only
        /// used with the hub it was made for.
        /// </summary>
        public static HubSettings Resolve(HubCliInputs inputs, Func<string, string?> getEnvironmentVariable, Func<string, HubProfile?> getProfile)
        {
            var profileName = string.IsNullOrWhiteSpace(inputs.ProfileName) ? ProfileStore.DefaultProfileName : inputs.ProfileName.Trim();
            var profile = getProfile(profileName);

            var urlText = FirstValue(inputs.Url, getEnvironmentVariable(UrlVariable), profile?.Url)
                ?? throw CliException.Usage($"No hub URL given. Use --url, set {UrlVariable}, or sign in with 'certify-hub login --url <hub url>'.");

            var baseUri = ParseHubUrl(urlText);

            // the id and secret are taken as a pair from one place, so a stray environment variable cannot pair with a
            // command line value to form a credential nobody intended
            string? clientId = null;
            string? clientSecret = null;

            if (!string.IsNullOrWhiteSpace(inputs.ClientId) || !string.IsNullOrWhiteSpace(inputs.ClientSecret))
            {
                clientId = RequirePairValue(inputs.ClientId, "--client-id");
                clientSecret = RequirePairValue(inputs.ClientSecret, "--client-secret or --client-secret-stdin");
            }
            else if (!string.IsNullOrWhiteSpace(getEnvironmentVariable(ClientIdVariable)) || !string.IsNullOrWhiteSpace(getEnvironmentVariable(ClientSecretVariable)))
            {
                clientId = RequirePairValue(getEnvironmentVariable(ClientIdVariable), ClientIdVariable);
                clientSecret = RequirePairValue(getEnvironmentVariable(ClientSecretVariable), ClientSecretVariable);
            }

            var isProfileForThisHub = profile != null
                && !string.IsNullOrWhiteSpace(profile.AccessToken)
                && Uri.TryCreate(profile.Url, UriKind.Absolute, out var profileUri)
                && Uri.Compare(profileUri, baseUri, UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;

            var authMode = clientId != null ? HubAuthMode.ApiToken : isProfileForThisHub ? HubAuthMode.Login : HubAuthMode.None;

            return new HubSettings
            {
                BaseUri = baseUri,
                AuthMode = authMode,
                ProfileName = profileName,
                Profile = profile,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Insecure = inputs.Insecure || string.Equals(getEnvironmentVariable(InsecureVariable), "true", StringComparison.OrdinalIgnoreCase),
                CaCertPath = FirstValue(inputs.CaCertPath, getEnvironmentVariable(CaCertVariable))
            };
        }

        /// <summary>
        /// Parse a hub URL, normalised to end with a slash so relative API paths resolve beneath any path prefix.
        /// </summary>
        public static Uri ParseHubUrl(string url)
        {
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw CliException.Usage($"'{url}' is not a valid hub URL, expected e.g. https://hub.example.com:9696");
            }

            var builder = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };

            if (!builder.Path.EndsWith('/'))
            {
                builder.Path += "/";
            }

            return builder.Uri;
        }

        private static string? FirstValue(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

        private static string RequirePairValue(string? value, string name)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw CliException.Usage($"An API token needs both a client id and a secret, {name} is missing.")
                : value.Trim();
        }
    }
}
