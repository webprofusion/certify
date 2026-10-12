using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.Extensions.Options;

namespace Certify.Server.HubService.LocalAdmin
{
    internal class UnixPeerAuthSchemeOptions : AuthenticationSchemeOptions { }

    /// <summary>
    /// Authenticates callers on the local admin Unix domain socket by the operating system user at the other end.
    /// </summary>
    /// <remarks>
    /// The socket's directory and file modes are the access boundary, as the pipe ACL is on Windows. On Linux the peer
    /// is additionally checked with SO_PEERCRED and only root or the hub's own service user is accepted. Other platforms
    /// have no equivalent here, so they rely on the file modes alone.
    /// </remarks>
    internal class UnixPeerAuthSchemeHandler : AuthenticationHandler<UnixPeerAuthSchemeOptions>
    {
        public const string SchemeName = "LocalAdminUnixPeer";

        private const int SolSocket = 1;
        private const int SoPeerCred = 17;

        public UnixPeerAuthSchemeHandler(IOptionsMonitor<UnixPeerAuthSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        [DllImport("libc", EntryPoint = "geteuid")]
        private static extern uint GetEffectiveUserId();

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var socket = Context.Features.Get<IConnectionSocketFeature>()?.Socket;

            if (socket?.LocalEndPoint is not UnixDomainSocketEndPoint)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            string callerName;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // struct ucred { pid_t pid; uid_t uid; gid_t gid; }
                Span<byte> credentials = stackalloc byte[12];

                if (socket.GetRawSocketOption(SolSocket, SoPeerCred, credentials) < credentials.Length)
                {
                    return Task.FromResult(AuthenticateResult.Fail("Could not read the credentials of the local admin socket caller."));
                }

                var pid = BitConverter.ToInt32(credentials[..4]);
                var uid = BitConverter.ToUInt32(credentials[4..8]);

                if (uid != 0 && uid != GetEffectiveUserId())
                {
                    Logger.LogWarning("Local admin socket connection refused for uid {uid} (pid {pid}), only root or the hub service user may use it.", uid, pid);
                    return Task.FromResult(AuthenticateResult.Fail("Only root or the hub service user may use the local admin channel."));
                }

                callerName = $"uid {uid} (pid {pid})";
            }
            else
            {
                callerName = "local socket user";
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, callerName), new Claim(ClaimTypes.Role, LocalAdminHost.AdminRole)],
                Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
