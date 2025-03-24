using System.Diagnostics;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Certify.Service.Controllers
{
    public class ServiceAuthSchemeOptions : AuthenticationSchemeOptions { }
    public class ServiceAuthSchemeHandler : AuthenticationHandler<ServiceAuthSchemeOptions>
    {
        public ServiceAuthSchemeHandler(
            IOptionsMonitor<ServiceAuthSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder)
        {
        }

        protected async override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // provide and an artificial default identify when this auth scheme is used (for non-window auth)
            var claims = new[] { new Claim(ClaimTypes.Name, "service_user") };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
            var ticket = new AuthenticationTicket(principal, this.Scheme.Name);
            return AuthenticateResult.Success(ticket);
        }
    }

    public class ClaimsTransformer : IClaimsTransformation
    {
        private bool _requireWindowsAuth;
        public ClaimsTransformer(bool requireWindowsAuth = true)
        {
            _requireWindowsAuth = requireWindowsAuth;
        }
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var ci = (ClaimsIdentity)principal.Identity;

            if (_requireWindowsAuth)
            {
                // on windows, add group claims for the user

                var isAdmin = ci.Claims
                    .Where(x => x.Type == ClaimTypes.GroupSid || x.Type == ClaimTypes.PrimaryGroupSid)
                    .Any(x => x.Value == "S-1-5-32-544"); //Administrator group

                if (isAdmin)
                {
                    var roleClaim = new Claim(ClaimTypes.Role, "service_admin");
                    ci.AddClaim(roleClaim);
                }
            }
            else
            {
                // auto enable role for the current identity
                var roleClaim = new Claim(ClaimTypes.Role, "service_admin");
                ci.AddClaim(roleClaim);
            }

            return Task.FromResult(principal);
        }
    }

    [ApiController]
    [Authorize(Policy = "CertifyServiceAuth")]
    public class ControllerBase : Controller
    {
        internal void DebugLog(string msg = null,
            [System.Runtime.CompilerServices.CallerMemberName] string callerName = "",
              [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
#if DEBUG
            if (!string.IsNullOrEmpty(sourceFilePath))
            {
                sourceFilePath = System.IO.Path.GetFileName(sourceFilePath);
            }

            var output = $"API [{sourceFilePath}/{callerName}] {msg}";

            Console.ForegroundColor = ConsoleColor.Yellow;
            Debug.WriteLine(output);
            Console.ForegroundColor = ConsoleColor.White;
#endif
        }

        Client.AuthContext _currentAuthContext = null;

        /// <summary>
        /// Set the current auth context for the current request, only used internally when invoking controller outside of an http request
        /// </summary>
        /// <param name="authContext"></param>
        /// 
        [NonAction]
        public void SetCurrentAuthContext(Client.AuthContext authContext)
        {
            _currentAuthContext = authContext;
        }

        [NonAction]
        public string GetContextUserId()
        {
            if (_currentAuthContext != null)
            {
                return _currentAuthContext.UserId;
            }

            // TODO: sign passed value provided by public API using public APIs access token
            var contextUserId = Request?.Headers["X-Context-User-Id"];

            return contextUserId;
        }
    }
}
