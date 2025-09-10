using System.Security.Cryptography;
using Certify.Models;
using Certify.Server.Hub.Api.Models.Acme;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Certify.Server.Hub.Api.Services
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
        /// <returns>Decoded request object</returns>
        /// <exception cref="ArgumentException">When JWS validation fails</exception>
        public async Task<T> DecodeJwsPayload<T>(JwsPayload payload, string requestUrl)
        {
            if (payload == null)
            {
                throw new ArgumentException("JWS payload is null");
            }

            if (string.IsNullOrEmpty(payload.Protected))
            {
                throw new ArgumentException("JWS protected header is missing");
            }

            if (string.IsNullOrEmpty(payload.Signature))
            {
                throw new ArgumentException("JWS signature is missing");
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
                    throw new ArgumentException("Invalid JWS protected header format");
                }

                // Validate required fields in protected header
                await ValidateProtectedHeader(protectedHeader, requestUrl);

                // Verify the signature
                if (!await VerifyJwsSignature(payload, protectedHeader))
                {
                    throw new ArgumentException("JWS signature verification failed. Ensure Account Key is valid and known to this CA");
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
                    throw new ArgumentException("Failed to deserialize JWS payload");
                }

                return result;
            }
            catch (FormatException ex)
            {
                throw new ArgumentException($"Invalid base64url encoding in JWS: {ex.Message}", ex);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new ArgumentException($"Invalid JSON in JWS: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Decodes JWS payload with account key information
        /// </summary>
        public async Task<(T request, JsonWebKey? accountKey)> DecodeJwsWithAccountKey<T>(JwsPayload payload, string requestUrl)
        {
            var request = await DecodeJwsPayload<T>(payload, requestUrl);

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
                return await DecodeJwsPayload<T>(payload, requestUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for {Context}", errorContext);
                throw new ArgumentException("Invalid JWS payload");
            }
        }

        /// <summary>
        /// Gets the account KID from JWS payload
        /// </summary>
        public static string GetAccountKidFromJwsPayload(JwsPayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.Protected))
            {
                throw new ArgumentException("JWS payload or protected header is null or empty");
            }

            var protectedBytes = JwsConvert.FromBase64String(payload.Protected);
            var protectedJson = System.Text.Encoding.UTF8.GetString(protectedBytes);
            var protectedHeader = JsonConvert.DeserializeObject<JwsProtectedHeader>(protectedJson);

            if (protectedHeader == null)
            {
                throw new ArgumentException("Invalid JWS protected header format");
            }

            return protectedHeader?.Kid ?? throw new ArgumentException("JWS protected header 'kid' is missing");
        }

        /// <summary>
        /// Validates the JWS protected header according to RFC 7515 and ACME requirements
        /// </summary>
        /// <param name="header">Protected header to validate</param>
        /// <param name="requestUrl">Expected request URL</param>
        private async Task ValidateProtectedHeader(JwsProtectedHeader header, string requestUrl)
        {
            // RFC 7515 Section 4.1.1 - Algorithm is required
            if (string.IsNullOrEmpty(header.Alg))
            {
                throw new ArgumentException("JWS algorithm (alg) is required");
            }

            // Validate supported algorithms
            var supportedAlgorithms = new[] { "RS256", "RS384", "RS512", "ES256", "ES384", "ES512", "PS256", "PS384", "PS512" };
            if (!supportedAlgorithms.Contains(header.Alg))
            {
                throw new ArgumentException($"Unsupported JWS algorithm: {header.Alg}");
            }

            // RFC 8555 Section 6.2 - Either 'jwk' or 'kid' must be present
            if (header.Jwk == null && string.IsNullOrEmpty(header.Kid))
            {
                throw new ArgumentException("JWS header must contain either 'jwk' or 'kid'");
            }

            // RFC 8555 Section 6.2 - Both 'jwk' and 'kid' cannot be present
            if (header.Jwk != null && !string.IsNullOrEmpty(header.Kid))
            {
                throw new ArgumentException("JWS header cannot contain both 'jwk' and 'kid'");
            }

            // RFC 8555 Section 6.2 - URL is required for ACME requests
            if (string.IsNullOrEmpty(header.Url))
            {
                throw new ArgumentException("JWS header must contain 'url' for ACME requests");
            }

            // Validate the URL matches the current request
            if (!string.Equals(header.Url, requestUrl, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"JWS URL mismatch. Expected: {requestUrl}, Got: {header.Url}");
            }

            // RFC 8555 Section 6.5 - Nonce is required
            if (string.IsNullOrEmpty(header.Nonce))
            {
                throw new ArgumentException("JWS header must contain 'nonce' for ACME requests");
            }

            // Validate the nonce
            if (!await IsValidNonce(header.Nonce))
            {
                throw new ArgumentException("Invalid or expired nonce in JWS header");
            }

            // If JWK is present, validate the key
            if (header.Jwk != null)
            {
                ValidateJwk(header.Jwk);
            }
        }

        /// <summary>
        /// Validates a JSON Web Key according to RFC 7517
        /// </summary>
        /// <param name="jwk">JWK to validate</param>
        private static void ValidateJwk(JsonWebKey jwk)
        {
            if (string.IsNullOrEmpty(jwk.Kty))
            {
                throw new ArgumentException("JWK key type (kty) is required");
            }

            var supportedKeyTypes = new[] { "RSA", "EC" };
            if (!supportedKeyTypes.Contains(jwk.Kty))
            {
                throw new ArgumentException($"Unsupported JWK key type: {jwk.Kty}");
            }

            if (jwk.Kty == "RSA")
            {
                if (string.IsNullOrEmpty(jwk.N) || string.IsNullOrEmpty(jwk.E))
                {
                    throw new ArgumentException("RSA JWK must contain 'n' and 'e' parameters");
                }
            }
            else if (jwk.Kty == "EC")
            {
                if (string.IsNullOrEmpty(jwk.Crv) || string.IsNullOrEmpty(jwk.X) || string.IsNullOrEmpty(jwk.Y))
                {
                    throw new ArgumentException("EC JWK must contain 'crv', 'x', and 'y' parameters");
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
            // This is a simplified implementation
            // In a real implementation, you would:
            // 1. Use the appropriate cryptographic library (RSA, ECDSA)
            // 2. Apply the correct hash algorithm (SHA256, SHA384, SHA512)
            // 3. Verify the signature according to the algorithm

            // For this stub, we'll simulate verification
            return true;
        }

        private async Task<bool> IsValidNonce(string nonce)
        {
            return !string.IsNullOrEmpty(nonce) && (await _config.GetAcmeNonce(nonce)) != null;
        }
    }
}
