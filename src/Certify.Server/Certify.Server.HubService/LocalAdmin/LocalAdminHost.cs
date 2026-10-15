using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;
using Certify.Models.Hub;
using Certify.Server.Core;
using Certify.Server.Hub.Api.Services.LocalAdmin;
using Certify.Service.Controllers;
using Certify.Shared;
using Microsoft.AspNetCore.Authentication;

namespace Certify.Server.HubService.LocalAdmin
{
    /// <summary>
    /// Serves the local admin channel: break-glass administration (see <see cref="LocalAdminOperations"/>) for an
    /// operating system administrator on the hub machine, over a named pipe on Windows or a Unix domain socket elsewhere.
    /// </summary>
    /// <remarks>
    /// This is a separate, minimal web host in the hub process rather than another endpoint on the hub's own Kestrel.
    /// Adding a pipe or socket listener to that Kestrel would make it ignore ASPNETCORE_URLS and similar, so a hub with no
    /// Kestrel:Endpoints configured (e.g. a container relying on ASPNETCORE_HTTP_PORTS) would lose its network endpoint.
    /// It also keeps the hub API's CORS, rate limiting and token authentication off this channel, and this channel's
    /// operations off the network.
    /// </remarks>
    internal sealed class LocalAdminHost : IAsyncDisposable
    {
        /// <summary>
        /// Role claim given to an accepted caller, matching what <see cref="ClaimsTransformer"/> gives a Windows administrator.
        /// </summary>
        public const string AdminRole = "service_admin";

        private const string AdminPolicy = "local-admin";

        private readonly WebApplication _app;
        private readonly string? _socketPath;

        private LocalAdminHost(WebApplication app, string endpointDescription, string? socketPath)
        {
            _app = app;
            _socketPath = socketPath;
            EndpointDescription = endpointDescription;
        }

        /// <summary>
        /// The pipe or socket the channel is listening on, for status reporting.
        /// </summary>
        public string EndpointDescription { get; }

        public static bool IsEnabled => !string.Equals(Environment.GetEnvironmentVariable(LocalAdminChannel.EnabledVariable), "false", StringComparison.OrdinalIgnoreCase);

        public static async Task<LocalAdminHost> StartAsync(LocalAdminOperations operations, ILoggerFactory loggerFactory)
        {
            var builder = WebApplication.CreateSlimBuilder();

            // nothing from the hub's own configuration (Kestrel endpoints, urls) may apply to this host
            builder.Configuration.Sources.Clear();
            builder.Services.AddSingleton(loggerFactory);

            string endpointDescription;
            string? socketPath = null;
            string authScheme;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                EnsureCanHostPipe();

                // the pipe ACL admits only LocalSystem and elevated administrators, and refuses remote callers
                var pipeName = ServiceEndpointHosting.ConfigureNamedPipeEndpoint(builder, LocalAdminChannel.GetPipeName());
                endpointDescription = $"named pipe {pipeName}";

                authScheme = NamedPipeConnection.AuthScheme;
                builder.Services.AddAuthentication(authScheme)
                    .AddScheme<NamedPipeAuthSchemeOptions, NamedPipeAuthSchemeHandler>(authScheme, _ => { });
                builder.Services.AddSingleton<IClaimsTransformation>(new ClaimsTransformer(requireWindowsAuth: true));
            }
            else
            {
                socketPath = PrepareSocketPath();
                endpointDescription = $"socket {socketPath}";

                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.Configure(new ConfigurationBuilder().Build());
                    options.ListenUnixSocket(socketPath);
                });

                authScheme = UnixPeerAuthSchemeHandler.SchemeName;
                builder.Services.AddAuthentication(authScheme)
                    .AddScheme<UnixPeerAuthSchemeOptions, UnixPeerAuthSchemeHandler>(authScheme, _ => { });
            }

            builder.Services.AddAuthorization(options => options.AddPolicy(AdminPolicy, policy => policy
                .AddAuthenticationSchemes(authScheme)
                .RequireClaim(ClaimTypes.Role, AdminRole)));

            var app = builder.Build();

            app.UseAuthentication();
            app.UseAuthorization();

            MapEndpoints(app, operations);

            try
            {
                await app.StartAsync();
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }

            if (socketPath != null)
            {
                // a standard directory already excludes everyone else, this covers a directory set by CERTIFY_HUB_ADMIN_SOCKET
                File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            return new LocalAdminHost(app, endpointDescription, socketPath);
        }

        private static void MapEndpoints(WebApplication app, LocalAdminOperations operations)
        {
            // the caller is bound as a ClaimsPrincipal rather than read from HttpContext: a lambda taking only an
            // HttpContext binds as a plain RequestDelegate, which discards the result it returns
            static string Caller(ClaimsPrincipal user) => user.Identity?.Name ?? "unknown local caller";

            static IResult ToResult(LocalAdminResult result) => Results.Json(result, statusCode: result.IsSuccess ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);

            app.MapGet(LocalAdminChannel.PrincipalsRoute, async () => Results.Json(await operations.GetPrincipals()))
                .RequireAuthorization(AdminPolicy);

            app.MapPost(LocalAdminChannel.PasswordResetRoute, async (LocalAdminPrincipalRequest request, ClaimsPrincipal user) => ToResult(await operations.ResetPassword(request, Caller(user))))
                .RequireAuthorization(AdminPolicy);

            app.MapPost(LocalAdminChannel.GrantAdministratorRoute, async (LocalAdminPrincipalRequest request, ClaimsPrincipal user) => ToResult(await operations.GrantAdministrator(request, Caller(user))))
                .RequireAuthorization(AdminPolicy);

            app.MapPost(LocalAdminChannel.CreateAdministratorRoute, async (LocalAdminCreateAdministratorRequest request, ClaimsPrincipal user) => ToResult(await operations.CreateAdministrator(request, Caller(user))))
                .RequireAuthorization(AdminPolicy);

            app.MapPost(LocalAdminChannel.EnablePasswordLoginRoute, async (ClaimsPrincipal user) => ToResult(await operations.EnablePasswordLogin(Caller(user))))
                .RequireAuthorization(AdminPolicy);
        }

        /// <summary>
        /// The pipe ACL grants creating further pipe instances only to LocalSystem and elevated administrators, so any
        /// other account (e.g. the hub run interactively without elevation during development) cannot host the pipe.
        /// Checked up front because a failed bind leaves the first pipe instance open in this process.
        /// </summary>
        [SupportedOSPlatform("windows")]
        private static void EnsureCanHostPipe()
        {
            using var identity = WindowsIdentity.GetCurrent();

            if (!identity.IsSystem && !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                throw new InvalidOperationException("The hub must run as LocalSystem (as the installed service does) or as an elevated administrator to host the local admin pipe.");
            }
        }

        /// <summary>
        /// Make sure the socket's directory exists, and is ours alone where it is one of the standard locations, and clear a
        /// socket left by a previous run.
        /// </summary>
        private static string PrepareSocketPath()
        {
            var socketPath = LocalAdminChannel.GetServerSocketPath();
            var directory = Path.GetDirectoryName(socketPath) ?? throw new InvalidOperationException($"Invalid local admin socket path {socketPath}");

            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            // A directory set by CERTIFY_HUB_ADMIN_SOCKET may be shared, e.g. /tmp, so its mode is left alone and the socket
            // file's own mode protects it. A standard location is a directory for the socket alone: making it ours fails
            // unless this process owns it (or runs as root), so one created by someone else, e.g. squatting on the shared
            // /tmp fallback, is refused rather than trusted, since its owner could replace the socket.
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LocalAdminChannel.SocketPathVariable)))
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            if (File.Exists(socketPath))
            {
                File.Delete(socketPath);
            }

            return socketPath;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();

            if (_socketPath != null && File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }
        }
    }
}
