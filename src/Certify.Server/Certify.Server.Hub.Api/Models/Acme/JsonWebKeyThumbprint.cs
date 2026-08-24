using System.Security.Cryptography;
using System.Text;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// JSON Web Key thumbprints (RFC 7638), used to compare two public keys for equality
    /// independently of member ordering, key type, or any optional members present.
    /// </summary>
    public static class JsonWebKeyThumbprint
    {
        /// <summary>
        /// Compute the RFC 7638 SHA-256 thumbprint of a JWK.
        /// </summary>
        /// <returns>The base64url encoded thumbprint, or null if the key is incomplete, malformed or of an unsupported type.</returns>
        public static string? Compute(JsonWebKey? key)
        {
            var canonicalJson = GetCanonicalJson(key);

            if (canonicalJson == null)
            {
                return null;
            }

            return JwsConvert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
        }

        /// <summary>
        /// True when both keys are present, complete and represent the same public key.
        /// Fails closed, so an incomplete or unsupported key on either side is never a match.
        /// </summary>
        public static bool IsSameKey(JsonWebKey? a, JsonWebKey? b)
        {
            var thumbprintA = Compute(a);
            var thumbprintB = Compute(b);

            return thumbprintA != null
                && thumbprintB != null
                && string.Equals(thumbprintA, thumbprintB, StringComparison.Ordinal);
        }

        /// <summary>
        /// Build the RFC 7638 canonical JSON form: the required members for the key type only,
        /// in lexicographic order, with no whitespace.
        /// </summary>
        private static string? GetCanonicalJson(JsonWebKey? key)
        {
            if (key == null)
            {
                return null;
            }

            if (key.Kty == "RSA")
            {
                var e = NormaliseBase64Url(key.E);
                var n = NormaliseBase64Url(key.N);

                if (e == null || n == null)
                {
                    return null;
                }

                return $"{{\"e\":\"{e}\",\"kty\":\"RSA\",\"n\":\"{n}\"}}";
            }

            if (key.Kty == "EC")
            {
                var crv = NormaliseCurveName(key.Crv);
                var x = NormaliseBase64Url(key.X);
                var y = NormaliseBase64Url(key.Y);

                if (crv == null || x == null || y == null)
                {
                    return null;
                }

                return $"{{\"crv\":\"{crv}\",\"kty\":\"EC\",\"x\":\"{x}\",\"y\":\"{y}\"}}";
            }

            return null;
        }

        /// <summary>
        /// JWK members are base64url without padding, but tolerate clients which pad or which use
        /// standard base64, so an equivalent key is not reported as a different key. Values which are
        /// not base64url are rejected, so a crafted member value cannot alter the canonical JSON.
        /// </summary>
        private static string? NormaliseBase64Url(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalised = value.Trim().Replace('+', '-').Replace('/', '_').TrimEnd('=');

            if (normalised.Length == 0 || !normalised.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'))
            {
                return null;
            }

            return normalised;
        }

        /// <summary>
        /// Curve names are short identifiers, rejecting anything else keeps the canonical JSON well formed.
        /// </summary>
        private static string? NormaliseCurveName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalised = value.Trim();

            if (!normalised.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return null;
            }

            return normalised;
        }
    }
}
