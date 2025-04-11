using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Controllers;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
namespace Certify.Service.Controllers
{
    public class CustomAuthCheckAttribute : AuthorizeAttribute
    {
        protected override bool IsAuthorized(HttpActionContext actionContext)
        {

            // check if action is allow anonymous
            if (actionContext.ActionDescriptor.GetCustomAttributes<AllowAnonymousAttribute>().Any()
               || actionContext.ControllerContext.ControllerDescriptor.GetCustomAttributes<AllowAnonymousAttribute>().Any())
            {
                return true;
            }

            // check clients authorization scheme
            var request = actionContext.Request;
            var authorization = request.Headers.Authorization;

            var user = actionContext.RequestContext.Principal as WindowsPrincipal;

            if (user.IsInRole(WindowsBuiltInRole.Administrator))
            {
                return true;
            }

            if (user.IsInRole(WindowsBuiltInRole.PowerUser))
            {
                return true;
            }

            return false;
        }
    }

    [CustomAuthCheck]
    public class ControllerBase : ApiController
    {
        public void DebugLog(string msg = null,
            [System.Runtime.CompilerServices.CallerMemberName] string callerName = "",
              [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
#if DEBUG
            if (!string.IsNullOrEmpty(sourceFilePath))
            {
                sourceFilePath = System.IO.Path.GetFileName(sourceFilePath);
            }

            var output = $"API [{sourceFilePath}/{callerName}] {msg}";

            Debug.WriteLine(output);
#endif
        }

        public string GetAuthSecretKey()
        {
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("SECRETREPLACEFROMCONFIG"));
        }

        public static string GenerateJwt(string userid, string secretkey, int expireMinutes = 20)
        {
            var symmetricKey = Convert.FromBase64String(secretkey);
            var tokenHandler = new JsonWebTokenHandler();

            var now = DateTime.UtcNow;
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, userid)
                }),

                Expires = now.AddMinutes(Convert.ToInt32(expireMinutes)),

                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(symmetricKey),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            return tokenHandler.CreateToken(tokenDescriptor);
        }
    }
}
