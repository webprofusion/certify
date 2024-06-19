using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Service.Controllers
{
    [Authorize]
    public class AuthController : Controller
    {

        [HttpPost, Route("token")]
        [AllowAnonymous]
        public async Task<IActionResult> AcquireToken(string authJwt)
        {
            var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            if (!result.Succeeded)
            {
                return Unauthorized();
            }

            var claims = new Claim[]
            {
                new Claim(ClaimTypes.Sid, "service-client"),
            };

            var identity = new ClaimsIdentity(claims: claims, authenticationType: BearerTokenDefaults.AuthenticationScheme);
            var servicePrinciple = new ClaimsPrincipal(identity: identity);

            // consume a service token request and return a long lived JWT token
            var response = new AccessTokenResponse
            {
                AccessToken = "",
                ExpiresIn = 3600,
                RefreshToken = "",
            };
            return Ok(response);
        }

        [HttpPost, Route("refresh")]
        public async Task<string> Refresh(string token)
        {
            // validate refresh token, issue new JWT token

            var jwt = "";
            return await Task.FromResult(jwt);
        }
    }
}
