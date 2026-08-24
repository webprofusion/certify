using System.Security.Cryptography;
using Certify.Models;
using Certify.Server.Hub.Api.Models.Acme;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Certify.Server.Hub.Api.Services.Acme
{
    /// <summary>
    /// Service for validating JSON Web Signatures (JWS) according to RFC 7515 and ACME requirements
    /// </summary>
    public class AcmeJwsValidator
    {
        private readonly ILogger<AcmeJwsValidator> _logger;
        private readonly AcmeServerConfig _config;

        public AcmeJwsValidator(ILogger<AcmeJwsValidator> logger, AcmeServerConfig config)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Decodes and validates a JWS payload according to RFC 7515
        /// </summary>
        /// <typeparam name="T">Type of the request object</typeparam>
        /// <param name="payload">JWS payload</param>
        /// <param name="requestUrl">The expected request URL for validation</param>
        /// <param name="requireAccountKid">If true, the JWS must be signed using a registered account key referenced by 'kid'. Inline 'jwk' keys are rejected.</param>
        /// <returns>Decoded request object</returns>
        /// <exception cref="AcmeRequestException">When JWS validation fails</exception>
        public async Task<T> DecodeJwsPayload<T>(JwsPayload payload, string requestUrl, bool requireAccountKid = true)
        {
            if (payload == null)
            {
                throw Malformed("JWS payload is null");
            }

            if (string.IsNullOrEmpty(payload.Protected))
            {
                throw Malformed("JWS protected header is missing");
            }

            if (string.IsNullOrEmpty(payload.Signature))
            {
                throw Malformed("JWS signature is missing");
            }

            // RFC 7515 Section 7.2.1 - JWS structure validation
            try
            {
                // Decode and validate the protected header
                var protectedBytes = JwsConvert.FromBase64String(payload.Protected);
                var protectedJson = System.Text.Encoding.UTF8.GetString(protectedBytes);

                var protectedHeader = JsonConvert.DeserializeObject<JwsProtectedHeader>(protectedJson);
                if (protectedHeader == null)
                {
                    throw Malformed("Invalid JWS protected header format");
                }

                // Validate required fields in protected header
                await ValidateProtectedHeader(protectedHeader, requestUrl, requireAccountKid);

                // Verify the signature
                if (!await VerifyJwsSignature(payload, protectedHeader))
                {
                    throw Malformed("JWS signature verification failed. Ensure Account Key is valid and known to this CA");
                }

                // Decode the payload (RFC 7515 Section 7.2.2), allow blank payload for POST-As-Get
                if (string.IsNullOrEmpty(payload.Payload))
                {
                    return default!;
                }

                var payloadBytes = JwsConvert.FromBase64String(payload.Payload);
                var payloadJson = System.Text.Encoding.UTF8.GetString(payloadBytes);

                // Deserialize the JSON to the requested type
                var result = JsonConvert.DeserializeObject<T>(payloadJson);
                if (result == null)
                {
                    throw Malformed("Failed to deserialize JWS payload");
                }

                return result;
            }
            catch (FormatException ex)
            {
                throw Malformed($"Invalid base64url encoding in JWS: {ex.Message}", ex);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw Malformed($"Invalid JSON in JWS: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Decodes JWS payload with account key information
        /// </summary>
        public async Task<(T request, JsonWebKey? accountKey)> DecodeJwsWithAccountKey<T>(JwsPayload payload, string requestUrl, bool requireAccountKid = true)
        {
            var request = await DecodeJwsPayload<T>(payload, requestUrl, requireAccountKid);

            var protectedBytes = JwsConvert.FromBase64String(payload.Protected);
            var protectedJson = System.Text.Encoding.UTF8.GetString(protectedBytes);
            var protectedHeader = JsonConvert.DeserializeObject<JwsProtectedHeader>(protectedJson);

            return (request, protectedHeader?.Jwk);
        }

        /// <summary>
        /// Decodes JWS for POST-As-Get requests
        /// </summary>
        public async Task<T> DecodeJwsForPostAsGet<T>(JwsPayload payload, string requestUrl, string errorContext)
        {
            try
            {
                return await DecodeJwsPayload<T>(payload, requestUrl, requireAccountKid: true);
            }
            catch (AcmeRequestException ex)
            {
                // preserve the specific acme error type (badNonce etc) so the client can act on it
                _logger.LogError(ex, "Failed to decode JWS payload for {Context}", errorContext);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for {Context}", errorContext);
                throw Malformed("Invalid JWS payload");
            }
        }

        /// <summary>
        /// Gets the account KID from JWS payload
        /// </summary>
        public static string GetAccountKidFromJwsPayload(JwsPayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.Protected))
            {
                throw Malformed("JWS payload or protected header is null or empty");
            }

            var protectedBytes = JwsConvert.FromBase64String(payload.Protected);
            var protectedJson = System.Text.Encoding.UTF8.GetString(protectedBytes);
            var protectedHeader = JsonConvert.DeserializeObject<JwsProtectedHeader>(protectedJson);

            if (protectedHeader == null)
            {
                throw Malformed("Invalid JWS protected header format");
            }

            return protectedHeader?.Kid ?? throw Malformed("JWS protected header 'kid' is missing");
        }

        /// <summary>
        /// Validates the JWS protected header according to RFC 7515 and ACME requirements
        /// </summary>
        /// <param name="header">Protected header to validate</param>
        /// <param name="requestUrl">Expected request URL</param>
        /// <param name="requireAccountKid">If true, only a registered account 'kid' is accepted</param>
        private async Task ValidateProtectedHeader(JwsProtectedHeader header, string requestUrl, bool requireAccountKid)
        {
            // RFC 7515 Section 4.1.1 - Algorithm is required
            if (string.IsNullOrEmpty(header.Alg))
            {
                throw Malformed("JWS algorithm (alg) is required");
            }

            // Validate supported algorithms
            if (!AcmeKeyPolicy.IsSupportedSignatureAlgorithm(header.Alg))
            {
                throw Malformed($"Unsupported JWS algorithm: {header.Alg}");
            }

            // RFC 8555 Section 6.2 - Either 'jwk' or 'kid' must be present
            if (header.Jwk == null && string.IsNullOrEmpty(header.Kid))
            {
                throw Malformed("JWS header must contain either 'jwk' or 'kid'");
            }

            // RFC 8555 Section 6.2 - Both 'jwk' and 'kid' cannot be present
            if (header.Jwk != null && !string.IsNullOrEmpty(header.Kid))
            {
                throw Malformed("JWS header cannot contain both 'jwk' and 'kid'");
            }

            // Outside of account registration, requests must be signed by a registered account key referenced by 'kid'.
            // Accepting a caller supplied 'jwk' here would allow an unregistered party to sign their own requests.
            if (requireAccountKid && string.IsNullOrEmpty(header.Kid))
            {
                throw Malformed("JWS header must contain 'kid' referencing a registered account for this request");
            }

            // RFC 8555 Section 6.2 - URL is required for ACME requests
            if (string.IsNullOrEmpty(header.Url))
            {
                throw Malformed("JWS header must contain 'url' for ACME requests");
            }

            // Validate the URL matches the current request
            if (!string.Equals(header.Url, requestUrl, StringComparison.OrdinalIgnoreCase))
            {
                throw Malformed($"JWS URL mismatch. Expected: {requestUrl}, Got: {header.Url}");
            }

            // RFC 8555 Section 6.5 - Nonce is required. Nonce failures are reported as badNonce so the
            // client can retry using the fresh nonce returned with the error response.
            if (string.IsNullOrEmpty(header.Nonce))
            {
                throw BadNonce("JWS header must contain 'nonce' for ACME requests");
            }

            // Validate the nonce
            if (!await IsValidNonce(header.Nonce))
            {
                throw BadNonce("Invalid or expired nonce in JWS header");
            }

            // If JWK is present, this is an account registration and the key must meet key policy
            // before we store it and rely on it for every subsequent request from that account.
            if (header.Jwk != null)
            {
                var keyFailureReason = AcmeKeyPolicy.ValidateKeyForAlgorithm(header.Jwk, header.Alg);

                if (keyFailureReason != null)
                {
                    _logger.LogWarning("Rejected account key: {Reason}", keyFailureReason);
                    throw Malformed(keyFailureReason);
                }
            }
        }

        /// <summary>
        /// Verifies the JWS signature according to RFC 7515
        /// </summary>
        /// <param name="payload">JWS payload</param>
        /// <param name="header">Protected header</param>
        /// <returns>True if signature is valid</returns>
        private async Task<bool> VerifyJwsSignature(JwsPayload payload, JwsProtectedHeader header)
        {
            try
            {
                // Create the signing input (RFC 7515 Section 5.1)
                var signingInput = $"{payload.Protected}.{payload.Payload}";
                var signingInputBytes = System.Text.Encoding.UTF8.GetBytes(signingInput);

                // Decode the signature
                var signatureBytes = JwsConvert.FromBase64String(payload.Signature);

                // Get the public key from JWK or KID
                var publicKey = await GetPublicKey(header);

                if (publicKey == null)
                {
                    return false;
                }

                // Verify signature based on algorithm
                return VerifySignatureWithAlgorithm(signingInputBytes, signatureBytes, publicKey, header.Alg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying JWS signature");
                return false;
            }
        }

        /// <summary>
        /// Gets the public key from JWK or retrieves it using KID
        /// </summary>
        /// <param name="header">Protected header</param>
        /// <returns>Public key for verification</returns>
        private async Task<JsonWebKey?> GetPublicKey(JwsProtectedHeader header)
        {
            if (header.Jwk != null)
            {
                return header.Jwk;
            }
            else if (!string.IsNullOrEmpty(header.Kid))
            {
                var jwk = await _config.GetAccountKey(header.Kid);
                return jwk;
            }

            return null;
        }

        /// <summary>
        /// Verifies signature using the specified algorithm
        /// </summary>
        /// <param name="data">Data to verify</param>
        /// <param name="signature">Signature bytes</param>
        /// <param name="publicKey">Public key</param>
        /// <param name="algorithm">JWS algorithm</param>
        /// <returns>True if signature is valid</returns>
        private bool VerifySignatureWithAlgorithm(byte[] data, byte[] signature, JsonWebKey publicKey, string algorithm)
        {
            if (data == null || signature == null)
            {
                return false;
            }

            // Re-apply key policy at verification time, so a stored account key which no longer meets
            // policy (or does not match the algorithm being used) can no longer authorise requests.
            var keyFailureReason = AcmeKeyPolicy.ValidateKeyForAlgorithm(publicKey, algorithm);

            if (keyFailureReason != null)
            {
                _logger.LogError("Rejected JWS signature verification: {Reason}", keyFailureReason);
                return false;
            }

            var hashAlgorithm = algorithm[2..] switch
            {
                "256" => HashAlgorithmName.SHA256,
                "384" => HashAlgorithmName.SHA384,
                "512" => HashAlgorithmName.SHA512,
                _ => default
            };

            if (hashAlgorithm == default)
            {
                _logger.LogError("Unsupported JWS hash size in algorithm {Algorithm}", algorithm);
                return false;
            }

            var family = algorithm[..2];

            try
            {
                // key type, curve and key size were already confirmed to match the algorithm by key policy
                if (family is "RS" or "PS")
                {
                    using var rsa = RSA.Create();
                    rsa.ImportParameters(new RSAParameters
                    {
                        Modulus = JwsConvert.FromBase64String(publicKey.N),
                        Exponent = JwsConvert.FromBase64String(publicKey.E)
                    });

                    var padding = family == "RS" ? RSASignaturePadding.Pkcs1 : RSASignaturePadding.Pss;
                    return rsa.VerifyData(data, signature, hashAlgorithm, padding);
                }
                else if (family == "ES")
                {
                    var curve = publicKey.Crv switch
                    {
                        "P-256" => ECCurve.NamedCurves.nistP256,
                        "P-384" => ECCurve.NamedCurves.nistP384,
                        "P-521" => ECCurve.NamedCurves.nistP521,
                        _ => default
                    };

                    if (curve.Oid == null)
                    {
                        _logger.LogError("Unsupported EC curve for JWS verification: {Curve}", publicKey.Crv);
                        return false;
                    }

                    using var ecdsa = ECDsa.Create(new ECParameters
                    {
                        Curve = curve,
                        Q = new ECPoint
                        {
                            X = JwsConvert.FromBase64String(publicKey.X),
                            Y = JwsConvert.FromBase64String(publicKey.Y)
                        }
                    });

                    // JWS ECDSA signatures are fixed-width R||S (IEEE P1363), not DER
                    return ecdsa.VerifyData(data, signature, hashAlgorithm, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                }

                _logger.LogError("Unsupported JWS algorithm family: {Algorithm}", algorithm);
                return false;
            }
            catch (Exception ex)
            {
                // malformed key material or signature - fail closed
                _logger.LogError(ex, "Failed to verify JWS signature using algorithm {Algorithm}", algorithm);
                return false;
            }
        }

        private async Task<bool> IsValidNonce(string nonce)
        {
            // nonces are single use (RFC 8555 Section 6.5), consuming also enforces expiry
            return !string.IsNullOrEmpty(nonce) && await _config.ConsumeAcmeNonce(nonce);
        }

        private static AcmeRequestException Malformed(string detail)
            => new(AcmeErrorResponseService.AcmeErrorTypes.Malformed, detail);

        private static AcmeRequestException Malformed(string detail, Exception innerException)
            => new(AcmeErrorResponseService.AcmeErrorTypes.Malformed, detail, innerException);

        private static AcmeRequestException BadNonce(string detail)
            => new(AcmeErrorResponseService.AcmeErrorTypes.BadNonce, detail);
    }
}
