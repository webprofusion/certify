using System.Security.Cryptography;
using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Models.Acme;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Service for validating External Account Binding (EAB) according to RFC 8555 Section 7.3.4
    /// </summary>
    public class AcmeExternalAccountBindingValidator
    {
        private readonly ILogger<AcmeExternalAccountBindingValidator> _logger;
        private readonly ICertifyInternalApiClient _client;
        private readonly AcmeServerConfig _config;

        public AcmeExternalAccountBindingValidator(
            ILogger<AcmeExternalAccountBindingValidator> logger,
            ICertifyInternalApiClient client,
            AcmeServerConfig config)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Validates External Account Binding according to RFC 8555 Section 7.3.4
        /// </summary>
        /// <param name="eab">External Account Binding JWS</param>
        /// <param name="accountPublicKey">The public key from the account creation request</param>
        /// <param name="requestUrl">The request URL for validation</param>
        /// <returns>internal id of matching access token</returns>
        public async Task<string?> ValidateExternalAccountBinding(JwsPayload eab, JsonWebKey accountPublicKey, string requestUrl)
        {
            if (eab == null)
            {
                return null;
            }

            try
            {
                // 1. Decode and validate the EAB protected header
                var protectedBytes = JwsConvert.FromBase64String(eab.Protected);
                var protectedJson = System.Text.Encoding.UTF8.GetString(protectedBytes);
                var eabHeader = JsonConvert.DeserializeObject<JwsProtectedHeader>(protectedJson);

                if (eabHeader == null)
                {
                    _logger.LogError("Invalid EAB protected header format");
                    return null;
                }

                if (string.IsNullOrEmpty(eabHeader.Alg))
                {
                    eabHeader.Alg = "HS256"; // Default to HS256 if not specified
                }

                // 2. Validate EAB header requirements
                if (!ValidateEabHeader(eabHeader, requestUrl))
                {
                    _logger.LogError("Failed to validate EAB header");
                    return null;
                }

                if (await _config.IsEabKeyConsumed(eabHeader.Kid))
                {
                    _logger.LogError("EAB Key {keyId} has already been used to register an ACME account and cannot be re-used", eabHeader.Kid);
                    return null;
                }

                // 3. Retrieve the EAB secret key using the Key ID
                var eabMappedAccessToken = await GetEabMappedAccessToken(eabHeader.Kid);
                if (eabMappedAccessToken == null)
                {
                    _logger.LogError("EAB Key ID not found or invalid: {KeyId}", eabHeader.Kid);
                    return null;
                }

                // 4. Verify the EAB payload contains the account public key
                if (!ValidateEabPayload(eab.Payload, accountPublicKey))
                {
                    _logger.LogError("EAB payload does not match account public key");
                    return null;
                }

                // 5. Verify the EAB signature using HMAC
                // we used the key id to retrieve the secret key, now we convert it to a base64 encoded sha256 hash for our comparison

                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var bytes = System.Text.Encoding.UTF8.GetBytes(eabMappedAccessToken.Secret);
                var hashBytes = sha256.ComputeHash(bytes);
                var hashedEabKey = Management.Util.ToUrlSafeBase64String(hashBytes);

                if (!VerifyEabSignature(eab, hashedEabKey, eabHeader.Alg))
                {
                    _logger.LogError("EAB signature verification failed");
                    return null;
                }

                // 6. Mark the EAB key as used (prevent replay)
                await MarkEabKeyAsUsed(eabHeader.Kid);

                _logger.LogInformation("EAB validation successful for Key ID: {KeyId}", eabHeader.Kid);
                return eabMappedAccessToken.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating External Account Binding");
                return null;
            }
        }

        /// <summary>
        /// Validates EAB protected header according to RFC 8555
        /// </summary>
        private bool ValidateEabHeader(JwsProtectedHeader header, string requestUrl)
        {
            // Algorithm must be HMAC-based, default is HS256
            if (string.IsNullOrEmpty(header.Alg) && !header.Alg.StartsWith("HS"))
            {
                _logger.LogError("EAB algorithm must be HMAC-based, got: {Algorithm}", header.Alg);
                return false;
            }

            // Key ID is required
            if (string.IsNullOrEmpty(header.Kid))
            {
                _logger.LogError("EAB Key ID (kid) is required");
                return false;
            }

            // URL must match the newAccount endpoint
            if (!string.Equals(header.Url, requestUrl, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("EAB URL mismatch. Expected: {Expected}, Got: {Actual}",
                    requestUrl, header.Url);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates that EAB payload contains the account public key
        /// </summary>
        private bool ValidateEabPayload(string eabPayload, JsonWebKey accountPublicKey)
        {
            try
            {
                var payloadBytes = JwsConvert.FromBase64String(eabPayload);
                var payloadJson = System.Text.Encoding.UTF8.GetString(payloadBytes);
                var eabAccountKey = JsonConvert.DeserializeObject<JsonWebKey>(payloadJson);

                // Compare the keys (simplified - in practice, normalize and compare all relevant fields)
                return eabAccountKey?.Kty == accountPublicKey?.Kty &&
                       eabAccountKey?.N == accountPublicKey?.N &&
                       eabAccountKey?.E == accountPublicKey?.E;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate EAB payload");
                return false;
            }
        }

        /// <summary>
        /// Verify EAB signature using HMAC
        /// </summary>
        private bool VerifyEabSignature(JwsPayload eab, string secretKey, string algorithm)
        {
            try
            {
                // Create signing input: base64url(protected) + "." + base64url(payload)
                var signingInput = $"{eab.Protected}.{eab.Payload}";
                var signingInputBytes = System.Text.Encoding.UTF8.GetBytes(signingInput);

                // Decode the signature
                var signatureBytes = JwsConvert.FromBase64String(eab.Signature);

                // Compute HMAC based on algorithm
                HMAC hmac = algorithm switch
                {
                    "HS256" => new HMACSHA256(JwsConvert.FromBase64String(secretKey)),
                    "HS384" => new HMACSHA384(JwsConvert.FromBase64String(secretKey)),
                    "HS512" => new HMACSHA512(JwsConvert.FromBase64String(secretKey)),
                    _ => throw new ArgumentException($"Unsupported HMAC algorithm: {algorithm}")
                };

                using (hmac)
                {
                    var computedSignature = hmac.ComputeHash(signingInputBytes);

                    // Constant-time comparison to prevent timing attacks
                    var matchingKey = CryptographicOperations.FixedTimeEquals(signatureBytes, computedSignature);

                    if (matchingKey)
                    {
                        return true;
                    }
                    else
                    {
                        _logger.LogError("Supplied EAB key hash did not match expected value");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying EAB signature");
                return false;
            }
        }

        /// <summary>
        /// Retrieve EAB secret key for the given Key ID
        /// </summary>
        private async Task<AccessToken?> GetEabMappedAccessToken(string keyId)
        {
            var authContext = new AuthContext
            {
                UserId = "system"
            };

            var tokens = await _client.GetAssignedAccessTokens(authContext);
            var token = tokens.Single(t => t.AccessTokens.Any(a => a.Id == keyId)).AccessTokens.FirstOrDefault(a => a.Id == keyId);

            if (token == null)
            {
                _logger.LogError("EAB Key ID not found: {KeyId}", keyId);
                return null;
            }

            return token;
        }

        /// <summary>
        /// Mark EAB key as used to prevent replay attacks
        /// </summary>
        private async Task MarkEabKeyAsUsed(string keyId)
        {
            // Store used EAB keys with timestamp
            await _config.StoreAcmeConsumedEabKey(keyId, DateTime.UtcNow.ToString());
        }
    }
}
