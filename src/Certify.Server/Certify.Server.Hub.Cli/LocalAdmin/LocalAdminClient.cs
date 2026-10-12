using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Certify.Models.Hub;
using Certify.Server.Hub.Cli.Cli;
using Certify.Server.Hub.Cli.Hub;

namespace Certify.Server.Hub.Cli.LocalAdmin
{
    /// <summary>
    /// Talks to the hub's local admin channel: HTTP over the hub's named pipe on Windows, or its Unix domain socket
    /// elsewhere. Only reachable on the hub machine itself, by an administrator (Windows) or root / the hub service user.
    /// </summary>
    internal sealed class LocalAdminClient : IDisposable
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

        private readonly HttpClient _http;
        private readonly string _endpointDescription;

        private LocalAdminClient(HttpClient http, string endpointDescription)
        {
            _http = http;
            _endpointDescription = endpointDescription;
        }

        public void Dispose() => _http.Dispose();

        public static LocalAdminClient Create(string? pipeName, string? socketPath)
        {
            var handler = new SocketsHttpHandler { UseProxy = false };
            string endpointDescription;

            if (LocalAdminChannel.IsNamedPipeTransport)
            {
                var name = string.IsNullOrWhiteSpace(pipeName) ? LocalAdminChannel.GetPipeName() : pipeName.Trim();
                endpointDescription = $"named pipe {name}";

                handler.ConnectCallback = async (_, cancellationToken) =>
                {
                    // Identification level lets the hub see who is calling without being able to act as the caller
                    var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);

                    try
                    {
                        await pipe.ConnectAsync(ConnectTimeout, cancellationToken);
                        return pipe;
                    }
                    catch
                    {
                        await pipe.DisposeAsync();
                        throw;
                    }
                };
            }
            else
            {
                var path = ResolveSocketPath(socketPath);
                endpointDescription = $"socket {path}";

                handler.ConnectCallback = async (_, cancellationToken) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                };
            }

            var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/"), Timeout = TimeSpan.FromMinutes(1) };
            return new LocalAdminClient(http, endpointDescription);
        }

        private static string ResolveSocketPath(string? socketPath)
        {
            if (!string.IsNullOrWhiteSpace(socketPath))
            {
                return socketPath.Trim();
            }

            var candidates = LocalAdminChannel.GetClientSocketPathCandidates().ToList();
            return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        }

        public Task<List<LocalAdminPrincipalSummary>> GetPrincipalsAsync(CancellationToken cancellationToken)
            => SendAsync(HttpMethod.Get, LocalAdminChannel.PrincipalsRoute, null, CliJsonContext.Default.ListLocalAdminPrincipalSummary, cancellationToken);

        public Task<LocalAdminResult> PostAsync<TRequest>(string route, TRequest? request, JsonTypeInfo<TRequest> requestType, CancellationToken cancellationToken)
            => SendAsync(HttpMethod.Post, route, request == null ? "{}" : JsonSerializer.Serialize(request, requestType), CliJsonContext.Default.LocalAdminResult, cancellationToken);

        private async Task<TResult> SendAsync<TResult>(HttpMethod method, string route, string? jsonBody, JsonTypeInfo<TResult> resultType, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(method, route.TrimStart('/'));

            if (jsonBody != null)
            {
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;

            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw ConnectionError(ex);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw CliException.Authentication($"The hub refused this caller on the local admin channel ({(int)response.StatusCode}). {ElevationHint}");
                }

                // operations report their own failures as a result with a message, which the caller shows
                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.BadRequest)
                {
                    throw HubConnection.ErrorFromResponse(response, body);
                }

                try
                {
                    return JsonSerializer.Deserialize(body, resultType) ?? throw new CliException($"The hub returned an empty response on the {_endpointDescription}.");
                }
                catch (JsonException)
                {
                    throw HubConnection.ErrorFromResponse(response, body);
                }
            }
        }

        private static string ElevationHint => LocalAdminChannel.IsNamedPipeTransport
            ? "Run this from an elevated prompt (Run as administrator) on the hub machine."
            : "Run this as root (e.g. with sudo) or as the hub service user, on the hub machine.";

        private CliException ConnectionError(HttpRequestException ex)
        {
            Exception? inner = ex;

            while (inner != null)
            {
                switch (inner)
                {
                    case UnauthorizedAccessException:
                    case SocketException { SocketErrorCode: SocketError.AccessDenied }:
                        return CliException.Authentication($"Access to the hub's local admin channel ({_endpointDescription}) was denied. {ElevationHint}");

                    case TimeoutException:
                    case SocketException { SocketErrorCode: SocketError.AddressNotAvailable or SocketError.ConnectionRefused }:
                    case FileNotFoundException:
                        return new CliException(
                            $"The hub's local admin channel ({_endpointDescription}) was not found. Check the hub service is running on this machine, and that {LocalAdminChannel.EnabledVariable} is not set to false.");
                }

                inner = inner.InnerException;
            }

            return new CliException($"Could not connect to the hub's local admin channel ({_endpointDescription}) - {ex.Message}");
        }
    }
}
