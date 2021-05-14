using System;
using System.Threading.Tasks;
using System.Web.Http;
using Certify.Management;

namespace Certify.Service
{
    [RoutePrefix("api/auth")]
    public class AuthController : Controllers.ControllerBase
    {
        public class AuthModel
        {
            public string Key { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
        }

        private ICertifyManager _certifyManager = null;

        public AuthController(Management.ICertifyManager manager)
        {
            _certifyManager = manager;
        }
#if !RELEASE //feature not production ready
        [HttpGet, Route("windows")]
        public async Task<string> GetWindowsAuthKey()
        {


            // user is using windows authentication, return an initial secret auth token. TODO: user must be able to invalidate existing auth key
            var encryptedBytes = System.Security.Cryptography.ProtectedData.Protect(
                    System.Text.Encoding.UTF8.GetBytes(this.ActionContext.RequestContext.Principal.Identity.Name),
                    System.Text.Encoding.UTF8.GetBytes("authtoken"), System.Security.Cryptography.DataProtectionScope.LocalMachine
                    );

            var secret = Convert.ToBase64String(encryptedBytes);

            var userIdPlusSecret = ActionContext.RequestContext.Principal.Identity.Name + ":" + secret;

            // return auth secret as Base64 string suitable for Basic Authorization https://en.wikipedia.org/wiki/Basic_access_authentication
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(userIdPlusSecret));


        }

        [HttpPost, Route("token")]
        [AllowAnonymous]
        public async Task<string> AcquireToken(AuthModel model)
        {
            DebugLog();

            // TODO: validate authkey and return new JWT

            if (model.Key == "windows123")
            {
                var jwt = GenerateJwt("certifyuser", GetAuthSecretKey());
                return jwt;
            }
            else
            {
                return null;
            }
        }

        [HttpPost, Route("refresh")]
        public async Task<string> Refresh()
        {
            DebugLog();

            // TODO: validate refresh token and return new JWT

            var jwt = GenerateJwt("certifyuser", GetAuthSecretKey());
            return jwt;
        }
#endif
    }
}
