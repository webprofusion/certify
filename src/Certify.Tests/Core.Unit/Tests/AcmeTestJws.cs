using System.Security.Cryptography;
using System.Text;
using Certify.Server.Hub.Api.Models.Acme;
using Newtonsoft.Json;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// An ES256 account key which signs ACME requests the way a client does.
    /// </summary>
    internal sealed class AcmeTestJws : System.IDisposable
    {
        private readonly ECDsa _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public JsonWebKey PublicKey { get; }

        public AcmeTestJws()
        {
            var parameters = _ecdsa.ExportParameters(false);

            PublicKey = new JsonWebKey
            {
                Kty = "EC",
                Crv = "P-256",
                X = JwsConvert.ToBase64String(parameters.Q.X!),
                Y = JwsConvert.ToBase64String(parameters.Q.Y!)
            };
        }

        /// <summary>
        /// A request carrying the account key inline, as a new-account request does.
        /// </summary>
        public JwsPayload SignWithJwk(string url, string nonce, object payload, string alg = "ES256")
            => Sign(new JwsProtectedHeader { Alg = alg, Jwk = PublicKey, Url = url, Nonce = nonce }, payload);

        /// <summary>
        /// A request signed as a registered account, referenced by its account url.
        /// </summary>
        public JwsPayload SignWithKid(string kid, string url, string nonce, object payload, string alg = "ES256")
            => Sign(new JwsProtectedHeader { Alg = alg, Jwk = null!, Kid = kid, Url = url, Nonce = nonce }, payload);

        private JwsPayload Sign(JwsProtectedHeader header, object payload)
        {
            var protectedPart = JwsConvert.ToBase64String(Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(header, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore })));
            var payloadPart = JwsConvert.ToBase64String(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));

            var signature = _ecdsa.SignData(
                Encoding.UTF8.GetBytes($"{protectedPart}.{payloadPart}"),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            return new JwsPayload
            {
                Protected = protectedPart,
                Payload = payloadPart,
                Signature = JwsConvert.ToBase64String(signature)
            };
        }

        public void Dispose() => _ecdsa.Dispose();
    }
}
