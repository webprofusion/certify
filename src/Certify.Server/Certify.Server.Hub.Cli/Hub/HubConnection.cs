using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Certify.Server.Hub.Cli.Cli;

namespace Certify.Server.Hub.Cli.Hub
{
    /// <summary>
    /// Sends authenticated requests to the hub API, as an API token or as a signed in user.
    ///
    /// A signed in session refreshes its access token when it expires, saving the replacement refresh token (each is
    /// single use). The hub keeps refresh tokens in memory only, so after a hub restart the refresh fails and the user is
    /// asked to sign in again. That is expected, and is why automation should use an API token instead.
    /// </summary>
    internal sealed class HubConnection : IDisposable
    {
        public const string ClientIdHeader = "X-Client-ID";
        public const string ClientSecretHeader = "X-Client-Secret";

        private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(30);

        private readonly HttpClient _http;
        private readonly ProfileStore _profiles;

        private string? _accessToken;
        private string? _refreshToken;

        public HubConnection(HubSettings settings, ProfileStore profiles)
        {
            Settings = settings;
            _profiles = profiles;
            _http = new HttpClient(HubTls.CreateHandler(settings.Insecure, settings.CaCertPath))
            {
                BaseAddress = settings.BaseUri,
                Timeout = TimeSpan.FromMinutes(5)
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"certify-hub-cli/{typeof(HubConnection).Assembly.GetName().Version}");

            if (settings.AuthMode == HubAuthMode.Login)
            {
                _accessToken = ProfileStore.Unprotect(settings.Profile?.AccessToken);
                _refreshToken = ProfileStore.Unprotect(settings.Profile?.RefreshToken);
            }
        }

        public HubSettings Settings { get; }

        public void Dispose() => _http.Dispose();

        /// <summary>
        /// Send a request and return the response body, failing on any unsuccessful status.
        /// </summary>
        public async Task<string> SendAsync(HttpMethod method, string relativeUrl, string? jsonBody, bool authenticate, CancellationToken cancellationToken)
            => Encoding.UTF8.GetString(await SendForBytesAsync(method, relativeUrl, jsonBody, authenticate, cancellationToken));

        /// <summary>
        /// Send a request and return the response body as bytes, for responses which may be binary such as a PFX file.
        /// </summary>
        public async Task<byte[]> SendForBytesAsync(HttpMethod method, string relativeUrl, string? jsonBody, bool authenticate, CancellationToken cancellationToken)
        {
            using var response = await SendRawAsync(method, relativeUrl, jsonBody, authenticate, cancellationToken);
            return await ReadSuccessAsync(response, cancellationToken);
        }

        /// <summary>
        /// Read a response body, failing on any unsuccessful status.
        /// </summary>
        public static async Task<byte[]> ReadSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw ErrorFromResponse(response, Encoding.UTF8.GetString(body));
            }

            return body;
        }

        public async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string relativeUrl, string? jsonBody, bool authenticate, CancellationToken cancellationToken)
        {
            if (authenticate && Settings.AuthMode == HubAuthMode.None)
            {
                throw CliException.Authentication(
                    $"Not signed in to {Settings.BaseUri}. Run 'certify-hub login --url {Settings.BaseUri}', or supply an API token with --client-id and --client-secret (or {HubSettings.ClientIdVariable} and {HubSettings.ClientSecretVariable}).");
            }

            if (authenticate && Settings.AuthMode == HubAuthMode.Login && IsExpired(_accessToken))
            {
                await RefreshAsync(cancellationToken);
            }

            var response = await SendOnceAsync(method, relativeUrl, jsonBody, authenticate, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && authenticate && Settings.AuthMode == HubAuthMode.Login)
            {
                response.Dispose();
                await RefreshAsync(cancellationToken);
                response = await SendOnceAsync(method, relativeUrl, jsonBody, authenticate, cancellationToken);
            }

            return response;
        }

        private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string relativeUrl, string? jsonBody, bool authenticate, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(method, relativeUrl.TrimStart('/'));

            if (jsonBody != null)
            {
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            }

            if (authenticate)
            {
                if (Settings.AuthMode == HubAuthMode.ApiToken)
                {
                    request.Headers.Add(ClientIdHeader, Settings.ClientId);
                    request.Headers.Add(ClientSecretHeader, Settings.ClientSecret);
                }
                else if (Settings.AuthMode == HubAuthMode.Login)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                }
            }

            try
            {
                return await _http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
            {
                throw new CliException(
                    $"The hub's TLS certificate at {Settings.BaseUri} is not trusted - {ex.InnerException.Message.TrimEnd('.')}. Use --ca-cert <file> to trust a private CA, or --insecure to skip certificate validation.");
            }
            catch (HttpRequestException ex)
            {
                throw new CliException($"Could not reach the hub at {Settings.BaseUri} - {ex.Message}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new CliException($"The request to {Settings.BaseUri} timed out.");
            }
        }

        /// <summary>
        /// Exchange the saved refresh token for a new session, saving both replacement tokens.
        /// </summary>
        private async Task RefreshAsync(CancellationToken cancellationToken)
        {
            var expired = CliException.Authentication(
                $"Your session with {Settings.BaseUri} has expired (sessions also end when the hub restarts). Run 'certify-hub login' again.");

            if (string.IsNullOrWhiteSpace(_refreshToken))
            {
                throw expired;
            }

            using var response = await SendOnceAsync(HttpMethod.Post, $"api/v1/auth/refresh?refreshToken={Uri.EscapeDataString(_refreshToken)}", null, authenticate: false, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw expired;
            }

            var session = ParseSession(await response.Content.ReadAsStringAsync(cancellationToken)) ?? throw expired;

            _accessToken = session.AccessToken;
            _refreshToken = session.RefreshToken;

            SaveSession(_profiles, Settings.ProfileName, Settings.BaseUri, Settings.Profile?.Username, session);
        }

        internal sealed record Session(string AccessToken, string RefreshToken, string? Username);

        /// <summary>
        /// Read the tokens from a login or refresh response.
        /// </summary>
        internal static Session? ParseSession(string json)
        {
            if (TryParseObject(json) is not JsonObject response)
            {
                return null;
            }

            var accessToken = response["accessToken"]?.GetValueKind() == JsonValueKind.String ? response["accessToken"]!.GetValue<string>() : null;
            var refreshToken = response["refreshToken"]?.GetValueKind() == JsonValueKind.String ? response["refreshToken"]!.GetValue<string>() : null;
            var username = response["securityPrincipal"]?["username"]?.GetValueKind() == JsonValueKind.String ? response["securityPrincipal"]!["username"]!.GetValue<string>() : null;

            return string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken)
                ? null
                : new Session(accessToken, refreshToken, username);
        }

        public static void SaveSession(ProfileStore profiles, string profileName, Uri baseUri, string? username, Session session)
        {
            profiles.Save(profileName, new HubProfile
            {
                Url = baseUri.ToString(),
                Username = session.Username ?? username,
                AccessToken = ProfileStore.Protect(session.AccessToken),
                RefreshToken = ProfileStore.Protect(session.RefreshToken)
            });
        }

        /// <summary>
        /// When a JWT expires, read from its exp claim. Null when the token cannot be read.
        /// </summary>
        public static DateTimeOffset? GetExpiry(string? jwt)
        {
            var parts = jwt?.Split('.');

            if (parts == null || parts.Length != 3)
            {
                return null;
            }

            try
            {
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

                using var document = JsonDocument.Parse(Convert.FromBase64String(payload));

                return document.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                    : null;
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                return null;
            }
        }

        private static bool IsExpired(string? jwt)
        {
            var expiry = GetExpiry(jwt);
            return expiry != null && expiry.Value - RefreshMargin < DateTimeOffset.UtcNow;
        }

        public DateTimeOffset? AccessTokenExpiry => GetExpiry(_accessToken);

        /// <summary>
        /// Turn an unsuccessful hub response into a message, preferring the problem details the hub returns.
        /// </summary>
        public static CliException ErrorFromResponse(HttpResponseMessage response, string body)
        {
            var status = (int)response.StatusCode;
            var detail = DescribeBody(body);
            var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $": {detail}";

            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => CliException.Authentication($"The hub did not accept the credentials (401){suffix}. Check the API token, or run 'certify-hub login' again."),
                HttpStatusCode.Forbidden => new CliException($"Permission denied (403){suffix}. The signed in user or API token does not hold a role allowing this."),
                HttpStatusCode.NotFound => new CliException($"Not found (404){suffix}"),
                HttpStatusCode.TooManyRequests => new CliException($"Rate limited by the hub (429), retry after {response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 60} seconds{suffix}"),
                _ => new CliException($"Request failed ({status} {response.ReasonPhrase}){suffix}")
            };
        }

        internal static string? DescribeBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            if (TryParseObject(body) is JsonObject problem)
            {
                foreach (var field in new[] { "detail", "title", "message" })
                {
                    if (problem[field]?.GetValueKind() == JsonValueKind.String && !string.IsNullOrWhiteSpace(problem[field]!.GetValue<string>()))
                    {
                        return problem[field]!.GetValue<string>();
                    }
                }
            }

            var text = body.Trim();
            return text.Length > 500 ? text[..500] + "..." : text;
        }

        internal static JsonNode? TryParseObject(string json)
        {
            try
            {
                return JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
