using Certify.Server.Hub.Api.Models.Acme;

namespace Certify.Server.Hub.Api.Services.Acme
{
    /// <summary>
    /// Key quality rules for ACME account keys. Applied when an account is registered and again
    /// whenever a stored account key is used to verify a request signature, so a key which no longer
    /// meets policy cannot continue to authorise orders.
    /// </summary>
    public static class AcmeKeyPolicy
    {
        /// <summary>
        /// Minimum accepted RSA account key size. Matches the minimum accepted by the public CAs
        /// the hub proxies orders to.
        /// </summary>
        public const int MinRsaKeySizeBits = 2048;

        /// <summary>
        /// Maximum accepted RSA account key size, to bound signature verification cost.
        /// </summary>
        public const int MaxRsaKeySizeBits = 8192;

        /// <summary>
        /// JWS signature algorithms accepted for ACME requests. HMAC and 'none' are deliberately absent.
        /// </summary>
        public static readonly string[] SupportedSignatureAlgorithms =
        [
            "RS256", "RS384", "RS512",
            "ES256", "ES384", "ES512",
            "PS256", "PS384", "PS512"
        ];

        /// <summary>
        /// RFC 7518 Section 3.4 - each ECDSA algorithm is defined for exactly one curve.
        /// </summary>
        private static readonly Dictionary<string, string> _curveForEcAlgorithm = new(StringComparer.Ordinal)
        {
            { "ES256", "P-256" },
            { "ES384", "P-384" },
            { "ES512", "P-521" }
        };

        /// <summary>
        /// RFC 7518 Section 6.2.1.2 - EC coordinates are fixed width octet strings for the curve.
        /// </summary>
        private static readonly Dictionary<string, int> _coordinateSizeForCurve = new(StringComparer.Ordinal)
        {
            { "P-256", 32 },
            { "P-384", 48 },
            { "P-521", 66 }
        };

        public static bool IsSupportedSignatureAlgorithm(string? algorithm)
            => !string.IsNullOrEmpty(algorithm) && SupportedSignatureAlgorithms.Contains(algorithm);

        /// <summary>
        /// The curve an ECDSA algorithm requires, or null if the algorithm is not ECDSA.
        /// </summary>
        public static string? GetRequiredCurve(string? algorithm)
            => algorithm != null && _curveForEcAlgorithm.TryGetValue(algorithm, out var curve) ? curve : null;

        /// <summary>
        /// Validate an account key against key policy.
        /// </summary>
        /// <returns>null when the key is acceptable, otherwise the reason it was rejected.</returns>
        public static string? ValidateKey(JsonWebKey? jwk)
        {
            if (jwk == null)
            {
                return "JWK is required";
            }

            if (string.IsNullOrEmpty(jwk.Kty))
            {
                return "JWK key type (kty) is required";
            }

            return jwk.Kty switch
            {
                "RSA" => ValidateRsaKey(jwk),
                "EC" => ValidateEcKey(jwk),
                _ => $"Unsupported JWK key type: {jwk.Kty}"
            };
        }

        /// <summary>
        /// Validate an account key against key policy and confirm it is the right kind of key for the
        /// signature algorithm being used.
        /// </summary>
        /// <returns>null when the key may be used with the algorithm, otherwise the reason it was rejected.</returns>
        public static string? ValidateKeyForAlgorithm(JsonWebKey? jwk, string? algorithm)
        {
            if (!IsSupportedSignatureAlgorithm(algorithm))
            {
                return $"Unsupported JWS algorithm: {algorithm}";
            }

            var keyFailureReason = ValidateKey(jwk);
            if (keyFailureReason != null)
            {
                return keyFailureReason;
            }

            var requiredCurve = GetRequiredCurve(algorithm);

            if (requiredCurve == null)
            {
                // RS* and PS* both sign using an RSA key
                return jwk!.Kty == "RSA"
                    ? null
                    : $"JWS algorithm {algorithm} requires an RSA key but key type is {jwk.Kty}";
            }

            if (jwk!.Kty != "EC")
            {
                return $"JWS algorithm {algorithm} requires an EC key but key type is {jwk.Kty}";
            }

            if (!string.Equals(jwk.Crv, requiredCurve, StringComparison.Ordinal))
            {
                return $"JWS algorithm {algorithm} requires curve {requiredCurve} but the key uses curve {jwk.Crv}";
            }

            return null;
        }

        private static string? ValidateRsaKey(JsonWebKey jwk)
        {
            if (string.IsNullOrEmpty(jwk.N) || string.IsNullOrEmpty(jwk.E))
            {
                return "RSA JWK must contain 'n' and 'e' parameters";
            }

            byte[] modulus;
            byte[] exponent;

            try
            {
                modulus = JwsConvert.FromBase64String(jwk.N);
                exponent = JwsConvert.FromBase64String(jwk.E);
            }
            catch (Exception)
            {
                return "RSA JWK 'n' and 'e' must be base64url encoded";
            }

            var keySizeBits = GetBitLength(modulus);

            if (keySizeBits < MinRsaKeySizeBits)
            {
                return $"RSA account keys must be at least {MinRsaKeySizeBits} bits, the supplied key is {keySizeBits} bits";
            }

            if (keySizeBits > MaxRsaKeySizeBits)
            {
                return $"RSA account keys must be no larger than {MaxRsaKeySizeBits} bits, the supplied key is {keySizeBits} bits";
            }

            // a usable RSA public exponent is odd and greater than 1
            if (exponent.Length == 0 || (exponent[^1] & 1) == 0 || GetBitLength(exponent) < 2)
            {
                return "RSA JWK public exponent 'e' must be an odd value greater than 1";
            }

            return null;
        }

        private static string? ValidateEcKey(JsonWebKey jwk)
        {
            if (string.IsNullOrEmpty(jwk.Crv) || string.IsNullOrEmpty(jwk.X) || string.IsNullOrEmpty(jwk.Y))
            {
                return "EC JWK must contain 'crv', 'x', and 'y' parameters";
            }

            if (!_coordinateSizeForCurve.TryGetValue(jwk.Crv, out var coordinateSize))
            {
                return $"Unsupported EC curve: {jwk.Crv}";
            }

            byte[] x;
            byte[] y;

            try
            {
                x = JwsConvert.FromBase64String(jwk.X);
                y = JwsConvert.FromBase64String(jwk.Y);
            }
            catch (Exception)
            {
                return "EC JWK 'x' and 'y' must be base64url encoded";
            }

            if (x.Length != coordinateSize || y.Length != coordinateSize)
            {
                return $"EC JWK 'x' and 'y' must each be {coordinateSize} bytes for curve {jwk.Crv}";
            }

            return null;
        }

        /// <summary>
        /// Bit length of a big-endian unsigned integer, ignoring any leading zero padding.
        /// </summary>
        private static int GetBitLength(byte[] value)
        {
            var index = 0;

            while (index < value.Length && value[index] == 0)
            {
                index++;
            }

            if (index == value.Length)
            {
                return 0;
            }

            var bits = (value.Length - index - 1) * 8;

            for (var topByte = value[index]; topByte > 0; topByte >>= 1)
            {
                bits++;
            }

            return bits;
        }
    }
}
