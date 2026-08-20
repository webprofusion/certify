using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.Extensions.Options;

namespace Certify.Service.Controllers
{
    public class NamedPipeAuthSchemeOptions : AuthenticationSchemeOptions { }

    /// <summary>
    /// Authenticates callers which arrive over the local named pipe transport, using the windows
    /// identity of the process at the other end of the pipe.
    /// </summary>
    /// <remarks>
    /// The pipe ACL is the access boundary (see ServiceEndpointHosting), this scheme only resolves which
    /// caller is on the other end so the usual role claims can be applied by ClaimsTransformer.
    /// Requests which did not arrive over a pipe are left for the other registered schemes.
    /// </remarks>
    public class NamedPipeAuthSchemeHandler : AuthenticationHandler<NamedPipeAuthSchemeOptions>
    {
        public NamedPipeAuthSchemeHandler(
            IOptionsMonitor<NamedPipeAuthSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var pipeFeature = Context.Features.Get<IConnectionNamedPipeFeature>();

            if (pipeFeature == null || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // not a named pipe connection, defer to the other schemes on the policy
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            return Task.FromResult(AuthenticatePipeCaller(pipeFeature));
        }

        [SupportedOSPlatform("windows")]
        private AuthenticateResult AuthenticatePipeCaller(IConnectionNamedPipeFeature pipeFeature)
        {
            WindowsIdentity callerIdentity = null;

            try
            {
                // Capture the caller and nothing else while impersonating. The client connects at
                // Identification level, which is enough to read who they are but not enough to act on
                // their behalf - and notably not enough to open files, so any assembly the runtime
                // still needs to load would fail to load inside this callback. Everything else is
                // therefore done below, once the thread is back on our own token.
                pipeFeature.NamedPipe.RunAsClient(() => callerIdentity = WindowsIdentity.GetCurrent());

                if (callerIdentity?.IsAuthenticated != true)
                {
                    // an anonymous impersonation level client lands here (RunAsClient itself throws
                    // for those, handled below), as does any caller we cannot identify
                    return AuthenticateResult.Fail("Could not resolve the identity of the named pipe caller.");
                }

                var identity = new ClaimsIdentity(
                    callerIdentity.Claims.ToList(),
                    Scheme.Name,
                    ClaimTypes.Name,
                    ClaimTypes.Role);

                Logger.LogDebug("Named pipe connection authenticated as {callerName}", callerIdentity.Name);

                return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
            }
            catch (Exception exp)
            {
                Logger.LogError(exp, "Failed to authenticate named pipe caller");

                return AuthenticateResult.Fail(exp);
            }
            finally
            {
                callerIdentity?.Dispose();
            }
        }
    }
}
