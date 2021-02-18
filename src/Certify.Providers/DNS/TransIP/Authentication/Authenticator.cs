using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Certify.Models.Config;
using Newtonsoft.Json;

namespace Certify.Providers.DNS.TransIP.Authentication
{
    internal class Authenticator
    {
        private const int DEFAULT_LOGIN_DURATION = 300;

        private readonly string _login;
        private readonly string _privateKey;
        private readonly int _loginDuration;
        private readonly Util _util;
        private readonly DigestCreator _digestCreator;
        private readonly DigestSigner _digestSigner;

        private string _token = "";
        private DateTime _tokenValidUntil = DateTime.MinValue;

        public Authenticator(
            string login,
            string privateKey,
            int loginDuration
        )
        {
            _login = login ?? throw new ArgumentNullException(nameof(login));
            _privateKey = privateKey ?? throw new ArgumentNullException(nameof(privateKey));
            _loginDuration = GetLoginDuration(loginDuration);

            _util = new Util();
            _digestCreator = new DigestCreator();
            _digestSigner = new DigestSigner();
        }

        private static int GetLoginDuration(int loginDuration)
        {
            var duration = (decimal)loginDuration / 60;
            if (duration < 1)
            {
                duration = DEFAULT_LOGIN_DURATION;
            }
            var temp = Convert.ToInt32(Math.Ceiling(duration));
            return temp;
        }

        public async Task<ActionResult<string>> GetLoginToken()
        {
            if (LoginNeeded)
            {
                _tokenValidUntil = DateTime.Now.AddMinutes(_loginDuration);
                var token = await Login();
                if (!token.IsSuccess)
                {
                    ResetToken();
                    return new ActionResult<string> { IsSuccess = false, Message = token.Message };
                }
                _token = token.Result;
            }

            return new ActionResult<string> { IsSuccess = true, Result = _token };
        }

        private bool LoginNeeded => DateTime.Now >= _tokenValidUntil;

        private async Task<ActionResult<string>> Login()
        {
            try
            {
                var payload = GetPayload();
                var signature = GetSignature(payload);
                var response = await Login(payload, signature);
                return ProcessResponse(response);
            }
            catch (Exception ex)
            {
                return new ActionResult<string> { IsSuccess = false, Message = ex.Message };
            }
        }

        private string GetPayload()
        {
            var payload = new DTO.LoginRequest
            {
                login = _login,
                nonce = _util.GetUniqueId(),
                read_only = false,
                expiration_time = $"{_loginDuration} minutes",
                label = $"Certify DnsProviderTransIP - {_util.GetUnixEpoch()}",
                global_key = true
            };

            return JsonConvert.SerializeObject(payload);
        }

        private string GetSignature(string loginRequest)
        {
            var digest = _digestCreator.Create(loginRequest);
            return _digestSigner.Sign(digest, _privateKey);
        }

        private static async Task<HttpResponseMessage> Login(string loginRequest, string signature)
        {
            var content = new StringContent(loginRequest, Encoding.UTF8, "application/json");
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Signature", signature);
            return await client.PostAsync($"{DnsProviderTransIP.BASE_URI}auth", content);
        }

        private ActionResult<string> ProcessResponse(HttpResponseMessage response)
        {
            var result = response.Content.ReadAsStringAsync().Result;
            var loginResult = JsonConvert.DeserializeObject<DTO.LoginResult>(result);
            return loginResult.token == null
                ? new ActionResult<string> { IsSuccess = false, Message = loginResult.error ?? result }
                : new ActionResult<string> { IsSuccess = true, Result = loginResult.token };
        }

        private void ResetToken()
        {
            _token = "";
            _tokenValidUntil = DateTime.MinValue;
        }
    }
}
