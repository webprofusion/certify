using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
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

#if DEBUG   // feature not in production
            if (authorization != null && authorization.Scheme == "Bearer")
            {
                //bearer token presented, validate principle
                var token = authorization.Parameter;

                var secretKey = ((ControllerBase)actionContext.ControllerContext.Controller).GetAuthSecretKey();

                var principal = AuthenticateJwtToken(token, secretKey).Result;

                if (principal == null)
                {
                    //invalid token
                    return false;
                }
                else
                {
                    actionContext.RequestContext.Principal = principal;
                    return true;
                }
            }
#endif

            var user = actionContext.RequestContext.Principal as System.Security.Principal.WindowsPrincipal;
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

        public static ClaimsPrincipal GetPrincipal(string token, string secret)
        {
            // adapted form https://stackoverflow.com/questions/40281050/jwt-authentication-for-asp-net-web-api
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadToken(token) as JwtSecurityToken;

                if (jwtToken == null)
                {
                    return null;
                }

                var symmetricKey = Convert.FromBase64String(secret);

                var validationParameters = new TokenValidationParameters()
                {
                    RequireExpirationTime = true,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    IssuerSigningKey = new SymmetricSecurityKey(symmetricKey)
                };

                SecurityToken securityToken;
                var principal = tokenHandler.ValidateToken(token, validationParameters, out securityToken);

                return principal;
            }
            catch (Exception)
            {
                //should write log
                return null;
            }
        }

        private static bool ValidateToken(string token, string secret, out string username)
        {
            username = null;

            var simplePrinciple = GetPrincipal(token, secret);

            if (simplePrinciple?.Identity == null)
            {
                return false;
            }

            var identity = simplePrinciple.Identity as ClaimsIdentity;

            if (!identity.IsAuthenticated)
            {
                return false;
            }

            var usernameClaim = identity.FindFirst(ClaimTypes.Name);
            username = usernameClaim?.Value;

            if (string.IsNullOrEmpty(username))
            {
                return false;
            }

            // More validation to check whether username exists in system etc

            return true;
        }

        protected Task<IPrincipal> AuthenticateJwtToken(string token, string secret)
        {
            string username;

            if (ValidateToken(token, secret, out username))
            {
                // based on username to get more information from database 
                // in order to build local identity
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, username)
                    // Add more claims if needed: Roles, ...
                };

                var identity = new ClaimsIdentity(claims, "Jwt");
                IPrincipal user = new ClaimsPrincipal(identity);

                return Task.FromResult(user);
            }

            return Task.FromResult<IPrincipal>(null);
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

            Console.ForegroundColor = ConsoleColor.Yellow;
            Debug.WriteLine(output);
            Console.ForegroundColor = ConsoleColor.White;
#endif
        }

        public string GetAuthSecretKey()
        {
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("SECRETREPLACEFROMCONFIG"));
        }

        public static string GenerateJwt(string userid, string secretkey, int expireMinutes = 20)
        {
            var symmetricKey = Convert.FromBase64String(secretkey);
            var tokenHandler = new JwtSecurityTokenHandler();

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

            var stoken = tokenHandler.CreateToken(tokenDescriptor);
            var token = tokenHandler.WriteToken(stoken);

            return token;
        }
    }
}
