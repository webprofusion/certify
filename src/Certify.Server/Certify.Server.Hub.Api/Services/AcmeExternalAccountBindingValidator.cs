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
        /// <returns>Validation result, including the internal id of the matching access token and the owning security principal, or the reason for failure</returns>
        public async Task<EabValidationResult> ValidateExternalAccountBinding(JwsPayload eab, JsonWebKey accountPublicKey, string requestUrl)
        {
            if (eab == null)
            {
                return EabValidationResult.Failed("External account binding is required but was not supplied");
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
                    return EabValidationResult.Failed("Invalid external account binding protected header format");
                }

                if (string.IsNullOrEmpty(eabHeader.Alg))
                {
                    eabHeader.Alg = "HS256"; // Default to HS256 if not specified
                }

                // 2. Validate EAB header requirements
                var headerFailureReason = ValidateEabHeader(eabHeader, requestUrl);
                if (headerFailureReason != null)
                {
                    _logger.LogError("Failed to validate EAB header");
                    return EabValidationResult.Failed(headerFailureReason);
                }

                if (await _config.IsEabKeyConsumed(eabHeader.Kid))
                {
                    _logger.LogError("EAB Key {keyId} has already been used to register an ACME account and cannot be re-used", eabHeader.Kid);
                    return EabValidationResult.Failed("External account binding key has already been used to register an ACME account and cannot be re-used");
                }

                // 3. Retrieve the EAB secret key using the Key ID
                var eabMapping = await GetEabMappedAccessToken(eabHeader.Kid);
                if (eabMapping == null)
                {
                    _logger.LogError("EAB Key ID not found or invalid: {KeyId}", eabHeader.Kid);
                    return EabValidationResult.Failed("External account binding key id was not found, is invalid, expired or revoked");
                }

                var eabMappedAccessToken = eabMapping.Value.Token;
                var securityPrincipalId = eabMapping.Value.SecurityPrincipalId;

                // 4. Confirm the owning principal is authorised to use Managed ACME (i.e. is a Managed ACME Consumer)
                if (!await HasManagedAcmeAccess(securityPrincipalId, eabMapping.Value.ScopedAssignedRoles))
                {
                    _logger.LogError("Security principal {PrincipalId} mapped to EAB Key ID {KeyId} is not authorised to perform managed ACME orders", securityPrincipalId, eabHeader.Kid);
                    return EabValidationResult.Failed("The account associated with this external account binding key is not authorised to perform managed ACME orders");
                }

                // 4. Verify the EAB payload contains the account public key
                if (!ValidateEabPayload(eab.Payload, accountPublicKey))
                {
                    _logger.LogError("EAB payload does not match account public key");
                    return EabValidationResult.Failed("External account binding payload does not match the account public key");
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
                    return EabValidationResult.Failed("External account binding signature verification failed");
                }

                // 6. Mark the EAB key as used (prevent replay)
                await MarkEabKeyAsUsed(eabHeader.Kid);

                _logger.LogInformation("EAB validation successful for Key ID: {KeyId}", eabHeader.Kid);
                return EabValidationResult.Success(eabMappedAccessToken.Id, securityPrincipalId, eabMapping.Value.ScopedAssignedRoles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating External Account Binding");
                return EabValidationResult.Failed("An unexpected error occurred while validating the external account binding");
            }
        }

        /// <summary>
        /// Validates EAB protected header according to RFC 8555
        /// </summary>
        /// <returns>null if the header is valid, otherwise the reason for the validation failure</returns>
        private string? ValidateEabHeader(JwsProtectedHeader header, string requestUrl)
        {
            // Algorithm must be HMAC-based, default is HS256
            if (string.IsNullOrEmpty(header.Alg) || !header.Alg.StartsWith("HS", StringComparison.Ordinal))
            {
                _logger.LogError("EAB algorithm must be HMAC-based, got: {Algorithm}", header.Alg);
                return "External account binding algorithm must be HMAC-based (HS256, HS384 or HS512)";
            }

            // Key ID is required
            if (string.IsNullOrEmpty(header.Kid))
            {
                _logger.LogError("EAB Key ID (kid) is required");
                return "External account binding key id (kid) is required";
            }

            // URL must match the newAccount endpoint
            if (!string.Equals(header.Url, requestUrl, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("EAB URL mismatch. Expected: {Expected}, Got: {Actual}",
                    requestUrl, header.Url);
                return "External account binding url does not match the new account request url";
            }

            return null;
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
        /// Retrieve EAB secret key for the given Key ID, along with the security principal it is assigned to
        /// </summary>
        private async Task<(AccessToken Token, string SecurityPrincipalId, List<string> ScopedAssignedRoles)?> GetEabMappedAccessToken(string keyId)
        {
            var authContext = new AuthContext
            {
                UserId = StandardSecurityPrincipals.System
            };

            var tokens = await _client.GetAssignedAccessTokens(authContext);

            var matches = tokens
                .SelectMany(assigned => assigned.AccessTokens
                    .Where(a => a.Id == keyId)
                    .Select(a => (Token: a, assigned.SecurityPrincipalId, assigned.ScopedAssignedRoles)))
                .ToList();

            if (matches.Count == 0)
            {
                _logger.LogError("EAB Key ID not found: {KeyId}", keyId);
                return null;
            }

            if (matches.Count > 1)
            {
                // ambiguous mapping, we cannot determine the owning principal so fail closed
                _logger.LogError("EAB Key ID {KeyId} is assigned to more than one security principal", keyId);
                return null;
            }

            var match = matches[0];

            if (string.IsNullOrWhiteSpace(match.SecurityPrincipalId))
            {
                _logger.LogError("EAB Key ID {KeyId} has no associated security principal", keyId);
                return null;
            }

            if (match.Token.DateRevoked != null || (match.Token.DateExpiry != null && match.Token.DateExpiry <= DateTimeOffset.UtcNow))
            {
                _logger.LogError("EAB Key ID {KeyId} refers to a revoked or expired access token", keyId);
                return null;
            }

            return (match.Token, match.SecurityPrincipalId, match.ScopedAssignedRoles ?? []);
        }

        /// <summary>
        /// Confirms the given security principal is permitted to perform managed ACME orders.
        /// This action is only granted by the Managed ACME Consumer policy.
        /// </summary>
        public async Task<bool> HasManagedAcmeAccess(string securityPrincipalId, List<string>? scopedAssignedRoles = null)
        {
            if (string.IsNullOrWhiteSpace(securityPrincipalId))
            {
                return false;
            }

            var check = new AccessCheck(securityPrincipalId, ResourceTypes.ManagedAcme, StandardResourceActions.ManagedAcmePerformOrder);

            if (scopedAssignedRoles?.Count > 0)
            {
                check.ScopedAssignedRoles = scopedAssignedRoles;
            }

            try
            {
                return await _client.CheckSecurityPrincipalHasAccess(check, new AuthContext { UserId = StandardSecurityPrincipals.System });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check managed ACME access for security principal {PrincipalId}", securityPrincipalId);
                return false;
            }
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
