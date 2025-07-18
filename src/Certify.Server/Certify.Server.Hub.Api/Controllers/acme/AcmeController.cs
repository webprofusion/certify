using System.Collections.Concurrent;
using System.Security.Cryptography;
using Certify.Models;
using Certify.Server.Hub.Api.Models.Acme;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace Certify.Server.Hub.Api.Controllers.acme
{
    /// <summary>
    /// ACME API controller implementing RFC 8555 endpoints for certificate management
    /// </summary>
    [ApiController]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Route("acme")]
    public class AcmeController : ApiControllerBase
    {
        private readonly ILogger<AcmeController> _logger;
        private static readonly ConcurrentDictionary<string, AcmeAccount> _accounts = new();
        private static readonly ConcurrentDictionary<string, JsonWebKey> _accountKeys = new();
        private static readonly ConcurrentDictionary<string, AcmeOrder> _orders = new();
        private static readonly ConcurrentDictionary<string, AcmeAuthorization> _authorizations = new();
        private static readonly ConcurrentDictionary<string, string> _nonces = new();
        private ManagementAPI _mgmtAPI;
        private IInstanceManagementStateProvider _stateProvider;
        private string _hubInstanceId = default!;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="mgmtAPI"></param>
        public AcmeController(ILogger<AcmeController> logger, ManagementAPI mgmtAPI, IInstanceManagementStateProvider stateProvider)
        {
            _logger = logger;
            _mgmtAPI = mgmtAPI;
            _stateProvider = stateProvider;

            _hubInstanceId = _stateProvider.GetManagementHubInstanceId();
            LoadSavedState();
        }

        private string GenerateAccountId() => Guid.NewGuid().ToString("N");
        private string GenerateOrderId() => Guid.NewGuid().ToString("N");
        private string GenerateAuthorizationId() => Guid.NewGuid().ToString("N");
        private string GenerateChallengeId() => Guid.NewGuid().ToString("N");
        private string GenerateCertificateId() => Guid.NewGuid().ToString("N");
        private string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        /// <summary>
        /// ACME Directory endpoint - RFC 8555 Section 7.1.1
        /// </summary>
        /// <returns>Directory object with endpoint URLs</returns>
        [HttpGet("{key?}/directory")]
        [HttpGet("directory")]
        public IActionResult GetDirectory(string key = default!)
        {
            ValidateKeyIfSupplied(key);

            var baseUrl = $"{Request.Scheme}://{Request.Host}/acme{(key != null ? $"/{key}" : "")}";

            var directory = new AcmeDirectory
            {
                NewNonce = $"{baseUrl}/new-nonce",
                NewAccount = $"{baseUrl}/new-account",
                NewOrder = $"{baseUrl}/new-order",
                RevokeCert = $"{baseUrl}/revoke-cert",
                KeyChange = $"{baseUrl}/key-change",
                Meta = new DirectoryMeta
                {
                    ExternalAccountRequired = (string.IsNullOrEmpty(key))
                }
            };

            return Ok(directory);
        }

        private bool ValidateKeyIfSupplied(string key)
        {
            return true;
        }

        /// <summary>
        /// New nonce endpoint - RFC 8555 Section 7.2
        /// </summary>
        /// <returns>Fresh nonce in Replay-Nonce header</returns>
        [HttpHead("new-nonce")]
        [HttpGet("new-nonce")]
        [HttpHead("{key?}/new-nonce")]
        [HttpGet("{key?}/new-nonce")]
        public IActionResult NewNonce(string key = default!)
        {
            var nonce = GenerateNonce();
            Response.Headers.Add("Replay-Nonce", nonce);
            Response.Headers.Add("Cache-Control", "no-store");
            return Ok();
        }

        /// <summary>
        /// New account endpoint with EAB support - RFC 8555 Section 7.3
        /// </summary>
        /// <param name="payload">JWS payload containing account creation request</param>
        /// <returns>Account object</returns>
        [HttpPost("{key?}/new-account")]
        [HttpPost("new-account")]
        public IActionResult NewAccount([FromBody] JwsPayload payload, string key = default!)
        {
            ValidateKeyIfSupplied(key);

            // Decode the JWS payload
            NewAccountRequest request;
            JsonWebKey newAccountKey;
            try
            {
                request = DecodeJwsPayload<NewAccountRequest>(payload);

                var protectedBytes = JwsConvert.FromBase64String(payload.Protected);
                var protectedJson = System.Text.Encoding.UTF8.GetString(protectedBytes);
                newAccountKey = (JsonConvert.DeserializeObject<JwsProtectedHeader>(protectedJson)).Jwk;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for new account request");
                return BadRequest(new AcmeError { Type = "urn:ietf:params:acme:error:malformed", Detail = "Invalid JWS payload" });
            }

            // Validate External Account Binding if required
            if (request.ExternalAccountBinding != null)
            {
                if (!ValidateExternalAccountBinding(request.ExternalAccountBinding))
                {
                    return BadRequest(new AcmeError { Type = "urn:ietf:params:acme:error:externalAccountRequired", Detail = "Invalid external account binding" });
                }
            }

            var accountId = GenerateAccountId();
            var baseUrl = $"{Request.Scheme}://{Request.Host}/acme{(key != null ? $"/{key}" : "")}";
            var account = new AcmeAccount
            {
                Status = AccountStatus.Valid,
                Contact = request.Contact,
                TermsOfServiceAgreed = request.TermsOfServiceAgreed,
                Orders = $"{baseUrl}/account/{accountId}/orders",

            };

            var accountKid = $"{baseUrl}/account/{accountId}";

            _accounts[accountKid] = account;

            _accountKeys[accountKid] = newAccountKey;

            StoreCurrentState();

            Response.Headers.Add("Location", $"{baseUrl}/account/{accountId}");
            Response.Headers.Add("Replay-Nonce", GenerateNonce());

            return Created(accountKid, account);
        }

        /// <summary>
        /// New order endpoint - RFC 8555 Section 7.4
        /// </summary>
        /// <param name="payload">JWS payload containing order creation request</param>
        /// <returns>Order object</returns>
        [HttpPost("new-order")]
        [HttpPost("{key?}/new-order")]
        public async Task<IActionResult> NewOrder([FromBody] JwsPayload payload, string key = default!)
        {

            ValidateKeyIfSupplied(key);

            // Decode the JWS payload
            NewOrderRequest request;
            try
            {
                request = DecodeJwsPayload<NewOrderRequest>(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for new order request");
                return BadRequest(new AcmeError { Type = "urn:ietf:params:acme:error:malformed", Detail = $"Invalid JWS payload: {ex.Message}" });
            }

            var orderId = GenerateOrderId();
            var authorizationIds = new List<string>();

            var baseUrl = $"{Request.Scheme}://{Request.Host}/acme{(key != null ? $"/{key}" : "")}";

            // Create authorizations for each identifier

            // TODO: if caller has permission to request a managed challenge, mark auth as valid

            foreach (var identifier in request.Identifiers)
            {
                var authId = GenerateAuthorizationId();
                var authorization = new AcmeAuthorization
                {
                    Identifier = identifier,
                    Status = AuthorizationStatus.Valid, //presets auth to valid so the client doesn't attempt them
                    Expires = DateTime.UtcNow.AddDays(30),
                    Challenges = new List<AcmeChallenge>
                    {
                        new AcmeChallenge
                        {
                            Type = "http-01",
                            Status = ChallengeStatus.Valid,
                            Token = GenerateToken(),
                            Url = $"{baseUrl}/challenge/{GenerateChallengeId()}"
                        },
                        new AcmeChallenge
                        {
                            Type = "dns-01",
                            Status = ChallengeStatus.Valid,
                            Token = GenerateToken(),
                            Url = $"{baseUrl}/challenge/{GenerateChallengeId()}"
                        }
                    }
                };

                _authorizations[authId] = authorization;
                authorizationIds.Add($"{baseUrl}/authz/{authId}");
            }

            var order = new AcmeOrder
            {
                Id = orderId,
                Status = OrderStatus.Pending,
                Expires = DateTime.UtcNow.AddDays(30),
                Identifiers = request.Identifiers,
                NotBefore = request.NotBefore,
                NotAfter = request.NotAfter,
                Authorizations = authorizationIds,
                Finalize = $"{baseUrl}/order/{orderId}/finalize"
            };

            // create temp order in hub using a managed challenge
            var managedCert = new ManagedCertificate
            {
                Name = $"Hub ACME Order {orderId}",
                CertificateAuthorityId = StandardCertAuthorities.LETS_ENCRYPT,
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = request.Identifiers.FirstOrDefault()?.Value ?? "",
                    SubjectAlternativeNames = request.Identifiers.Select(i => i.Value).ToArray(),
                    DeploymentSiteOption = DeploymentOption.NoDeployment
                }
            };

            managedCert.RequestConfig.Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>
            {
                new CertRequestChallengeConfig
                {
                    ChallengeProvider = "managed" , ChallengeType = "dns-01",
                }
            };

            var tempCert = await _mgmtAPI.UpdateManagedCertificate(_hubInstanceId, managedCert, CurrentAuthContext);
            if (tempCert == null)
            {
                _logger.LogError("Failed to create temporary managed certificate for order {OrderId}", orderId);
                return StatusCode(500, new AcmeError { Type = "urn:ietf:params:acme:error:serverInternal", Detail = "Failed to create temporary managed certificate" });
            }

            _ = _mgmtAPI.PerformManagedCertificateRequest(_hubInstanceId, tempCert.Id, CurrentAuthContext);

            order.ManagedCertificateId = tempCert.Id;

            _orders[orderId] = order;

            StoreCurrentState();

            Response.Headers.Add("Location", $"{baseUrl}/order/{orderId}");
            Response.Headers.Add("Replay-Nonce", GenerateNonce());

            return Created($"{baseUrl}/order/{orderId}", order);
        }

        /// <summary>
        /// Finalize order endpoint - RFC 8555 Section 7.4
        /// </summary>
        /// <param name="orderId">Order identifier</param>
        /// <param name="payload">JWS payload containing finalization request with CSR</param>
        /// <returns>Updated order object</returns>
        [HttpPost("order/{orderId}/finalize")]
        [HttpPost("{key?}/order/{orderId}/finalize")]
        public async Task<IActionResult> FinalizeOrder(string orderId, [FromBody] JwsPayload payload, string key = default!)
        {
            ValidateKeyIfSupplied(key);

            if (!_orders.TryGetValue(orderId, out var order))
            {
                return NotFound(new AcmeError { Type = "urn:ietf:params:acme:error:orderNotFound", Detail = "Order not found" });
            }

            if (order.Status != OrderStatus.Ready)
            {
                return BadRequest(new AcmeError { Type = "urn:ietf:params:acme:error:orderNotReady", Detail = "Order not ready for finalization" });
            }

            // Decode the JWS payload
            FinalizeOrderRequest request;
            try
            {
                request = DecodeJwsPayload<FinalizeOrderRequest>(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for finalize order request");
                return BadRequest(new AcmeError { Type = "urn:ietf:params:acme:error:malformed", Detail = "Invalid JWS payload" });
            }

            // check status of temp managed certificate
            // 
            var managedCert = await _mgmtAPI.GetManagedCertificate(_hubInstanceId, order.ManagedCertificateId, CurrentAuthContext);

            // apply CSR from client finalize call as a formatted customcsr
            managedCert.RequestConfig.CustomCSR = $"-----BEGIN CERTIFICATE REQUEST-----\n{Convert.ToBase64String(Certify.Management.Util.FromUrlSafeBase64String(request.Csr), Base64FormattingOptions.InsertLineBreaks)}\n-----END CERTIFICATE REQUEST-----";

            await _mgmtAPI.UpdateManagedCertificate(_hubInstanceId, managedCert, CurrentAuthContext);

            // resume/finalize cert order
            await _mgmtAPI.PerformManagedCertificateRequest(_hubInstanceId, order.ManagedCertificateId, CurrentAuthContext);

            order.Status = OrderStatus.Valid;
            var certId = GenerateCertificateId();

            var baseUrl = $"{Request.Scheme}://{Request.Host}/acme{(key != null ? $"/{key}" : "")}";
            order.Certificate = $"{baseUrl}/cert/{certId}";

            Response.Headers.Add("Replay-Nonce", GenerateNonce());

            StoreCurrentState();

            return Ok(order);
        }

        /// <summary>
        /// Download certificate endpoint - RFC 8555 Section 7.4.2
        /// </summary>
        /// <param name="certId">Certificate identifier</param>
        /// <returns>Certificate in PEM format</returns>
        [HttpPost("cert/{certId}")]
        [HttpPost("{key?}/cert/{certId}")]
        public async Task<IActionResult> DownloadCertificate(string certId, [FromBody] JwsPayload payload, string key = default!)
        {
            ValidateKeyIfSupplied(key);

            try
            {
                _ = DecodeJwsPayload<object>(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for certificate request");
                return NotFound(new AcmeError { Type = "urn:ietf:params:acme:error:malformed", Detail = "Invalid JWS payload" });
            }

            var baseUrl = $"{Request.Scheme}://{Request.Host}/acme{(key != null ? $"/{key}" : "")}";

            var certUri = $"{baseUrl}/cert/{certId}";
            var order = _orders.FirstOrDefault(o => o.Value.Certificate == certUri).Value;
            if (order == null)
            {
                return BadRequest(new AcmeError { Type = "urn:ietf:params:acme:error:malformed", Detail = "Invalid or unknown certId" });
            }

            var managedCert = await _mgmtAPI.GetManagedCertificate(_hubInstanceId, order.ManagedCertificateId, CurrentAuthContext);
            var result = await _mgmtAPI.ExportCertificate(_hubInstanceId, order.ManagedCertificateId, "pem_fullchain", CurrentAuthContext);

            var certPEM = System.Text.Encoding.UTF8.GetString(result.Result);

            // delete order and temp managed cert
            await _mgmtAPI.RemoveManagedCertificate(_hubInstanceId, order.ManagedCertificateId, CurrentAuthContext);
            _orders.Remove(order.Id, out _);

            Response.Headers.Add("Replay-Nonce", GenerateNonce());

            // Return the certificate as plain text with proper content type
            return Content(certPEM, "application/pem-certificate-chain");
        }

        /// <summary>
        /// Post-As-Get order status endpoint - RFC 8555 Section 7.4
        /// </summary>
        /// <param name="orderId">Order identifier</param>
        /// <returns>Order object</returns>
        [HttpPost("order/{orderId}")]
        [HttpPost("{key?}/order/{orderId}")]
        public async Task<IActionResult> GetOrder(string orderId, [FromBody] JwsPayload payload, string key = default!)
        {
            ValidateKeyIfSupplied(key);

            try
            {
                _ = DecodeJwsPayload<object>(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for finalize order request");
                return BadRequest(new AcmeError { Type = "urn:ietf:params:acme:error:malformed", Detail = "Invalid JWS payload" });
            }

            if (!_orders.TryGetValue(orderId, out var order))
            {
                return NotFound(new AcmeError { Type = "urn:ietf:params:acme:error:orderNotFound", Detail = "Order not found" });
            }

            // check status of temp managed certificate
            // 
            var managedCert = await _mgmtAPI.GetManagedCertificate(_hubInstanceId, order.ManagedCertificateId, CurrentAuthContext);

            if (managedCert.LastRenewalStatus == null || managedCert.LastRenewalStatus == RequestState.Running)
            {
                order.Status = OrderStatus.Processing; // Simulate order being processed
            }
            else if (managedCert.LastRenewalStatus == RequestState.Paused)
            {

                order.Status = OrderStatus.Ready; // Simulate order being ready for this example

            }
            else
            {
                order.Status = managedCert.LastRenewalStatus == RequestState.Error ? OrderStatus.Invalid : OrderStatus.Valid;
            }

            Response.Headers.Add("Replay-Nonce", GenerateNonce());
            return Ok(order);
        }

        /// <summary>
        /// Post-As-Get authorization endpoint - RFC 8555 Section 7.5
        /// </summary>
        /// <param name="authId">Authorization identifier</param>
        /// <returns>Authorization object</returns>
        [HttpPost("authz/{authId}")]
        [HttpPost("{key?}/authz/{authId}")]
        public IActionResult GetAuthorization(string authId, [FromBody] JwsPayload payload, string key = default!)
        {
            ValidateKeyIfSupplied(key);

            try
            {
                _ = DecodeJwsPayload<object>(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for finalize order request");
                return BadRequest(new AcmeError { Type = "urn:ietf:params:acme:error:malformed", Detail = "Invalid JWS payload" });
            }

            if (!_authorizations.TryGetValue(authId, out var authorization))
            {
                return NotFound(new AcmeError { Type = "urn:ietf:params:acme:error:authorizationNotFound", Detail = "Authorization not found" });
            }

            Response.Headers.Add("Replay-Nonce", GenerateNonce());
            return Ok(authorization);
        }

        private string GenerateNonce()
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            _nonces[nonce] = DateTime.UtcNow.ToString();
            return nonce;
        }

        private bool IsValidNonce(string nonce)
        {
            return !string.IsNullOrEmpty(nonce) && _nonces.ContainsKey(nonce);
        }

        private string GetNonceFromHeaders()
        {
            return Request.Headers["Replay-Nonce"].FirstOrDefault();
        }

        private bool ValidateExternalAccountBinding(ExternalAccountBinding eab)
        {
            // In a real implementation, validate the EAB signature
            // For this stub, we'll just check that it's not null
            return eab != null && !string.IsNullOrEmpty(eab.Protected);
        }

        /// <summary>
        /// Decodes and validates a JWS payload according to RFC 7515
        /// </summary>
        /// <typeparam name="T">Type of the request object</typeparam>
        /// <param name="payload">JWS payload</param>
        /// <returns>Decoded request object</returns>
        /// <exception cref="ArgumentException">When JWS validation fails</exception>
        private T DecodeJwsPayload<T>(JwsPayload payload)
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
                ValidateProtectedHeader(protectedHeader);

                // Verify the signature
                if (!VerifyJwsSignature(payload, protectedHeader))
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
        /// Validates the JWS protected header according to RFC 7515 and ACME requirements
        /// </summary>
        /// <param name="header">Protected header to validate</param>
        private void ValidateProtectedHeader(JwsProtectedHeader header)
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
            var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}";
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
            if (!IsValidNonce(header.Nonce))
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
        private void ValidateJwk(JsonWebKey jwk)
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
        private bool VerifyJwsSignature(JwsPayload payload, JwsProtectedHeader header)
        {
            try
            {
                // Create the signing input (RFC 7515 Section 5.1)
                var signingInput = $"{payload.Protected}.{payload.Payload}";
                var signingInputBytes = System.Text.Encoding.UTF8.GetBytes(signingInput);

                // Decode the signature
                var signatureBytes = JwsConvert.FromBase64String(payload.Signature);

                // Get the public key from JWK or KID
                var publicKey = GetPublicKey(header);
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
        private JsonWebKey? GetPublicKey(JwsProtectedHeader header)
        {
            if (header.Jwk != null)
            {
                return header.Jwk;
            }
            else if (!string.IsNullOrEmpty(header.Kid))
            {
                if (_accountKeys.TryGetValue(header.Kid, out var jwk))
                {
                    return jwk;
                }
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

        private void StoreCurrentState()
        {
            var settingsPath = EnvironmentUtil.EnsuredAppDataPath("acme-server");

            System.IO.File.WriteAllText(Path.Join(settingsPath, "accounts.json"), System.Text.Json.JsonSerializer.Serialize(_accounts));
            System.IO.File.WriteAllText(Path.Join(settingsPath, "account-keys.json"), System.Text.Json.JsonSerializer.Serialize(_accountKeys));
            System.IO.File.WriteAllText(Path.Join(settingsPath, "orders.json"), System.Text.Json.JsonSerializer.Serialize(_orders));
        }

        private void LoadSavedState()
        {
            var settingsPath = EnvironmentUtil.EnsuredAppDataPath("acme-server");

            if (_accounts.Count == 0)
            {
                var accountsPath = Path.Join(settingsPath, "accounts.json");
                if (System.IO.File.Exists(accountsPath))
                {
                    var json = System.IO.File.ReadAllText(accountsPath);
                    var accounts = System.Text.Json.JsonSerializer.Deserialize<ConcurrentDictionary<string, AcmeAccount>>(json);
                    _accounts.Clear();
                    foreach (var a in accounts)
                    {
                        _accounts.TryAdd(a.Key, a.Value);
                    }
                }
            }

            if (_accountKeys.Count == 0)
            {
                var accountKeyPath = Path.Join(settingsPath, "account-keys.json");
                if (System.IO.File.Exists(accountKeyPath))
                {
                    var json = System.IO.File.ReadAllText(accountKeyPath);
                    var accountKeys = System.Text.Json.JsonSerializer.Deserialize<ConcurrentDictionary<string, JsonWebKey>>(json);
                    _accountKeys.Clear();
                    foreach (var a in accountKeys)
                    {
                        _accountKeys.TryAdd(a.Key, a.Value);
                    }
                }
            }

            if (_orders.Count == 0)
            {
                var ordersPath = Path.Join(settingsPath, "orders.json");
                if (System.IO.File.Exists(ordersPath))
                {
                    var json = System.IO.File.ReadAllText(ordersPath);
                    var orders = System.Text.Json.JsonSerializer.Deserialize<ConcurrentDictionary<string, AcmeOrder>>(json);
                    _orders.Clear();
                    foreach (var a in orders)
                    {
                        _orders.TryAdd(a.Key, a.Value);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Base64 URL encoding/decoding utility for JWS (JSON Web Signature)
    /// </summary>
    public static class JwsConvert
    {
        /// <summary>
        /// Encodes the data to the base64url string without padding.
        /// </summary>
        /// <param name="data">The data to encode.</param>
        /// <returns>The encoded data.</returns>
        public static string ToBase64String(byte[] data)
        {
            var s = Convert.ToBase64String(data); // Regular base64 encoder
            s = s.Split('=')[0]; // Remove any trailing '='s
            s = s.Replace('+', '-'); // 62nd char of encoding
            s = s.Replace('/', '_'); // 63rd char of encoding
            return s;
        }

        /// <summary>
        /// Decodes the base64url string without padding.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns>The decoded data.</returns>
        /// <exception cref="System.ArgumentException">If <paramref name="data"/> is illegal base64 URL string.</exception>
        public static byte[] FromBase64String(string data)
        {
            var s = data;
            s = s.Replace('-', '+'); // 62nd char of encoding
            s = s.Replace('_', '/'); // 63rd char of encoding
            switch (s.Length % 4) // Pad with trailing '='s
            {
                case 0: break; // No pad chars in this case
                case 2: s += "=="; break; // Two pad chars
                case 3: s += "="; break; // One pad char
                default:
                    throw new ArgumentException("Invalid base64url string");
            }

            return Convert.FromBase64String(s); // Standard base64 decoder
        }
    }
}
