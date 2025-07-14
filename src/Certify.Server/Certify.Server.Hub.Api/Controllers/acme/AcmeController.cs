using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public class AcmeController : ControllerBase
    {
        private readonly ILogger<AcmeController> _logger;
        private static readonly ConcurrentDictionary<string, AcmeAccount> _accounts = new();
        private static readonly ConcurrentDictionary<string, JsonWebKey> _accountKeys = new();
        private static readonly ConcurrentDictionary<string, AcmeOrder> _orders = new();
        private static readonly ConcurrentDictionary<string, AcmeAuthorization> _authorizations = new();
        private static readonly ConcurrentDictionary<string, string> _nonces = new();

        public AcmeController(ILogger<AcmeController> logger)
        {
            _logger = logger;

            LoadSavedState();
        }

        /// <summary>
        /// ACME Directory endpoint - RFC 8555 Section 7.1.1
        /// </summary>
        /// <returns>Directory object with endpoint URLs</returns>
        [HttpGet("directory")]
        public IActionResult GetDirectory()
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}/acme";

            var directory = new AcmeDirectory
            {
                NewNonce = $"{baseUrl}/new-nonce",
                NewAccount = $"{baseUrl}/new-account",
                NewOrder = $"{baseUrl}/new-order",
                RevokeCert = $"{baseUrl}/revoke-cert",
                KeyChange = $"{baseUrl}/key-change",
                Meta = new DirectoryMeta
                {
                    TermsOfService = "https://example.com/terms",
                    Website = "https://example.com",
                    CaaIdentities = new[] { "example.com" },
                    ExternalAccountRequired = true
                }
            };

            return Ok(directory);
        }

        /// <summary>
        /// New nonce endpoint - RFC 8555 Section 7.2
        /// </summary>
        /// <returns>Fresh nonce in Replay-Nonce header</returns>
        [HttpHead("new-nonce")]
        [HttpGet("new-nonce")]
        public IActionResult NewNonce()
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
        [HttpPost("new-account")]
        public IActionResult NewAccount([FromBody] JwsPayload payload)
        {

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
            var account = new AcmeAccount
            {
                Status = AccountStatus.Valid,
                Contact = request.Contact,
                TermsOfServiceAgreed = request.TermsOfServiceAgreed,
                Orders = $"{Request.Scheme}://{Request.Host}/acme/account/{accountId}/orders",

            };

            var accountKid = $"{Request.Scheme}://{Request.Host}/acme/account/{accountId}";

            _accounts[accountKid] = account;

            _accountKeys[accountKid] = newAccountKey;

            StoreCurrentState();

            Response.Headers.Add("Location", $"{Request.Scheme}://{Request.Host}/acme/account/{accountId}");
            Response.Headers.Add("Replay-Nonce", GenerateNonce());

            return Created(accountKid, account);
        }

        private void StoreCurrentState()
        {
            var settingsPath = Models.EnvironmentUtil.EnsuredAppDataPath("acme-server");

            System.IO.File.WriteAllText(Path.Join(settingsPath, "accounts.json"), System.Text.Json.JsonSerializer.Serialize(_accounts));
            System.IO.File.WriteAllText(Path.Join(settingsPath, "account-keys.json"), System.Text.Json.JsonSerializer.Serialize(_accountKeys));
            System.IO.File.WriteAllText(Path.Join(settingsPath, "orders.json"), System.Text.Json.JsonSerializer.Serialize(_orders));
        }

        private void LoadSavedState()
        {
            var settingsPath = Models.EnvironmentUtil.EnsuredAppDataPath("acme-server");

            if (_accounts.Count == 0)
            {
                var accountsPath = Path.Join(settingsPath, "accounts.json");
                if (System.IO.File.Exists(accountsPath))
                {
                    var accounts = System.Text.Json.JsonSerializer.Deserialize<ConcurrentDictionary<string, AcmeAccount>>(System.IO.File.ReadAllText(accountsPath));
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
                    var accountKeys = System.Text.Json.JsonSerializer.Deserialize<ConcurrentDictionary<string, JsonWebKey>>(System.IO.File.ReadAllText(accountKeyPath));
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
                    var orders = System.Text.Json.JsonSerializer.Deserialize<ConcurrentDictionary<string, AcmeOrder>>(System.IO.File.ReadAllText(ordersPath));
                    _orders.Clear();
                    foreach (var a in orders)
                    {
                        _orders.TryAdd(a.Key, a.Value);
                    }
                }
            }
        }

        /// <summary>
        /// New order endpoint - RFC 8555 Section 7.4
        /// </summary>
        /// <param name="payload">JWS payload containing order creation request</param>
        /// <returns>Order object</returns>
        [HttpPost("new-order")]
        public IActionResult NewOrder([FromBody] JwsPayload payload)
        {

            // Decode the JWS payload
            NewOrderRequest request;
            try
            {
                request = DecodeJwsPayload<NewOrderRequest>(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for new order request");
                return BadRequest(new AcmeError { Type = "urn:ietf:params:acme:error:malformed", Detail = "Invalid JWS payload" });
            }

            var orderId = GenerateOrderId();
            var authorizationIds = new List<string>();

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
                            Url = $"{Request.Scheme}://{Request.Host}/acme/challenge/{GenerateChallengeId()}"
                        },
                        new AcmeChallenge
                        {
                            Type = "dns-01",
                            Status = ChallengeStatus.Valid,
                            Token = GenerateToken(),
                            Url = $"{Request.Scheme}://{Request.Host}/acme/challenge/{GenerateChallengeId()}"
                        }
                    }
                };

                _authorizations[authId] = authorization;
                authorizationIds.Add($"{Request.Scheme}://{Request.Host}/acme/authz/{authId}");
            }

            var order = new AcmeOrder
            {
                Status = OrderStatus.Pending,
                Expires = DateTime.UtcNow.AddDays(30),
                Identifiers = request.Identifiers,
                NotBefore = request.NotBefore,
                NotAfter = request.NotAfter,
                Authorizations = authorizationIds,
                Finalize = $"{Request.Scheme}://{Request.Host}/acme/order/{orderId}/finalize"
            };

            _orders[orderId] = order;

            StoreCurrentState();

            Response.Headers.Add("Location", $"{Request.Scheme}://{Request.Host}/acme/order/{orderId}");
            Response.Headers.Add("Replay-Nonce", GenerateNonce());

            return Created($"{Request.Scheme}://{Request.Host}/acme/order/{orderId}", order);
        }

        /// <summary>
        /// Finalize order endpoint - RFC 8555 Section 7.4
        /// </summary>
        /// <param name="orderId">Order identifier</param>
        /// <param name="payload">JWS payload containing finalization request with CSR</param>
        /// <returns>Updated order object</returns>
        [HttpPost("order/{orderId}/finalize")]
        public IActionResult FinalizeOrder(string orderId, [FromBody] JwsPayload payload)
        {
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

            // Simulate certificate issuance
            // order.Status = OrderStatus.Processing;

            // In a real implementation, you would process the CSR here
            // For this stub, we'll simulate issuing a certificate
            /*Task.Run(async () =>
            {
                await Task.Delay(2000); // Simulate processing time
                order.Status = OrderStatus.Valid;
                var certId = GenerateCertificateId();
                order.Certificate = $"{Request.Scheme}://{Request.Host}/acme/cert/{certId}";
            });*/

            order.Status = OrderStatus.Valid;
            var certId = GenerateCertificateId();
            order.Certificate = $"{Request.Scheme}://{Request.Host}/acme/cert/{certId}";

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
        public IActionResult DownloadCertificate(string certId, [FromBody] JwsPayload payload)
        {

            try
            {
                _ = DecodeJwsPayload<object>(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for certificate request");
                return BadRequest(new AcmeError { Type = "urn:ietf:params:acme:error:malformed", Detail = "Invalid JWS payload" });
            }

            // In a real implementation, you would retrieve the actual certificate
            // For this stub, we'll return a mock certificate
            var mockCertificate = @"-----BEGIN CERTIFICATE-----
MIIGFDCCBPygAwIBAgISLMbVaSKYCxXTlj5le0Vxj/c3MA0GCSqGSIb3DQEBCwUA
MFoxCzAJBgNVBAYTAlVTMSAwHgYDVQQKExcoU1RBR0lORykgTGV0J3MgRW5jcnlw
dDEpMCcGA1UEAxMgKFNUQUdJTkcpIFdhbm5hYmUgV2F0ZXJjcmVzcyBSMTEwHhcN
MjUwNzA5MDcyNzI2WhcNMjUxMDA3MDcyNzI1WjAAMIICIjANBgkqhkiG9w0BAQEF
AAOCAg8AMIICCgKCAgEAzyGeCodGdU1mNP6/rzDbYdYnxlcI9todQXQYUggD40Xq
Vbv3u0Z1pz83qZ8yBDDwb33nf+8C+/Oq8UmvFv0o0ozVkOJnSpzWF+mjOBlCxzNS
Y+VgMG4NNrxdYqIr1E8mHyhadT0CPzcIga/jbfxvuEEhztYRnubSySsicnSk6oHb
Iza2uLSxcNYavkGsiYF6EulaDS3pRMAvEdCvJiILaLUxwHAOoF9f4YD7yRykdwwG
fNjbG/u3jEdgwE6APMX6Wtf0hxu1UCqfE17Orulkn/GatuWbVX9pSk7U8n2CtMJ4
ZsUZo9qHYjRIbzSRZb3huPNi/lB3GCYgwrU7aYpWgmDBO7FcshBsdyMYhJie2C1i
I43IHCEf3+sAluVIvPIo/uG3e2uMxS1y2JKbqK4Uyghey9h0UlGBkj2saB5C+46e
YYtFqRPPsIEJKINHPsNXGLGGmjWxQVcQiRKr0gCboiFdOIYqrKh/sVeia+pmj8Bn
+ZO/RjaOQ/vKO/9qwAPgKDUt0htWWQy/HJaExH01xuhhVfk68aRSZ9gP99NIAkgE
6wLN200nm1if6fTiAoJFx+ufSsDXw3891bURt4mwpALMnA5Ux+kYNav5Fx+IcR7P
o20g9ocFj1kusTz7Ek+iDeNh+u2lZZNDFHaHtZAROo1VM/mYL3FMYfjJs4/onssC
AwEAAaOCAiwwggIoMA4GA1UdDwEB/wQEAwIHgDATBgNVHSUEDDAKBggrBgEFBQcD
ATAMBgNVHRMBAf8EAjAAMB8GA1UdIwQYMBaAFBPL1/aunfxpQmTWXHwjzIX5R8e2
MDcGCCsGAQUFBwEBBCswKTAnBggrBgEFBQcwAoYbaHR0cDovL3N0Zy1yMTEuaS5s
ZW5jci5vcmcvMEAGA1UdEQEB/wQ2MDSCFnRlc3QucHJvamVjdGJpZHMuY28udWuC
Gnd3dy50ZXN0LnByb2plY3RiaWRzLmNvLnVrMBMGA1UdIAQMMAowCAYGZ4EMAQIB
MDIGA1UdHwQrMCkwJ6AloCOGIWh0dHA6Ly9zdGctcjExLmMubGVuY3Iub3JnLzU2
LmNybDCCAQwGCisGAQQB1nkCBAIEgf0EgfoA+AB1AN2ZNPyl5ySAyVZofYE0mQhJ
skn3tWnYx7yrP1zB825kAAABl+5KnS8AAAQDAEYwRAIgIyu24r12zx258c6Xdvih
rddE7DSY0VYaCEqlmrs7pJkCIH0wPWmDb8IS3qTdCovsfPtH9vecOwvj9iLG25xC
i0m/AH8AwF0gVDhcss+yF5INLw3Hg1JhR7GqT++Xynjh8LuE/O0AAAGX7kqlAwAI
AAAFAEP89CwEAwBIMEYCIQDv37DXcAibsbR3dG+ejpbcoPj92rWLyQruY99iWVHi
GQIhAIK/iowQfYZNM7g116Gy/oEKYH6T6AN6bGObIoNZp4MtMA0GCSqGSIb3DQEB
CwUAA4IBAQBEJMvk34H7sfuMmpXvt+2tWA6h4KOXZIakwyCAvmSpTEwtaovadszu
jkTgYU1yezGEjjexS68dUfy2NEYGhrTq5oZg803TPFgHUN+0YpZsncOuk/cJxA1k
CVwfODjUX2gauMQYe24ARVNLMPvEpcyvkqUu1kN5Fn3kFeWEvpL4c17BMi3AyoSe
aatcIuSF0pEyowHfajxIO/NqGMbCay0wLZvd57JByOAH50aZbm6PEidGlcqmQZZD
RGF9UFNelIwRD/EZT/+G+xLn6oJpO5KeqwZet7xGUjRGLQE5iBn6nx/VXsv2EZsP
sQeiudUQtw0tzemFEKfvLT8zIX+pHcfa
-----END CERTIFICATE-----
";

            Response.Headers.Add("Replay-Nonce", GenerateNonce());

            // Return the certificate as plain text with proper content type
            return Content(mockCertificate, "application/pem-certificate-chain");
        }

        /// <summary>
        /// Post-As-Get order status endpoint - RFC 8555 Section 7.4
        /// </summary>
        /// <param name="orderId">Order identifier</param>
        /// <returns>Order object</returns>
        [HttpPost("order/{orderId}")]
        public IActionResult GetOrder(string orderId, [FromBody] JwsPayload payload)
        {
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

            order.Status = OrderStatus.Ready; // Simulate order being ready for this example

            Response.Headers.Add("Replay-Nonce", GenerateNonce());
            return Ok(order);
        }

        /// <summary>
        /// Post-As-Get authorization endpoint - RFC 8555 Section 7.5
        /// </summary>
        /// <param name="authId">Authorization identifier</param>
        /// <returns>Authorization object</returns>
        [HttpPost("authz/{authId}")]
        public IActionResult GetAuthorization(string authId, [FromBody] JwsPayload payload)
        {
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

        /// <summary>
        /// Test endpoint to verify JWS payload decoding (for development/testing only)
        /// </summary>
        /// <param name="payload">JWS payload to decode</param>
        /// <returns>Decoded payload information</returns>
        [HttpPost("test/decode-jws")]
        public IActionResult TestDecodeJws([FromBody] JwsPayload payload)
        {
            try
            {
                // Decode the protected header
                var protectedBytes = JwsConvert.FromBase64String(payload.Protected);
                var protectedJson = System.Text.Encoding.UTF8.GetString(protectedBytes);
                var protectedHeader = JsonConvert.DeserializeObject<object>(protectedJson);

                // Decode the payload
                var payloadBytes = JwsConvert.FromBase64String(payload.Payload);
                var payloadJson = System.Text.Encoding.UTF8.GetString(payloadBytes);
                var payloadObject = JsonConvert.DeserializeObject<object>(payloadJson);

                return Ok(new
                {
                    Protected = protectedHeader,
                    Payload = payloadObject,
                    Signature = payload.Signature
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new AcmeError
                {
                    Type = "urn:ietf:params:acme:error:malformed",
                    Detail = $"Failed to decode JWS: {ex.Message}"
                });
            }
        }

        // Helper methods
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

        private string GenerateAccountId() => Guid.NewGuid().ToString("N");
        private string GenerateOrderId() => Guid.NewGuid().ToString("N");
        private string GenerateAuthorizationId() => Guid.NewGuid().ToString("N");
        private string GenerateChallengeId() => Guid.NewGuid().ToString("N");
        private string GenerateCertificateId() => Guid.NewGuid().ToString("N");
        private string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

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
                    throw new ArgumentException("JWS signature verification failed");
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
        /// Converts JWK to a public key object
        /// </summary>
        /// <param name="jwk">JSON Web Key</param>
        /// <returns>Public key object</returns>
        private object ConvertJwkToPublicKey(JsonWebKey jwk)
        {
            // This is a simplified implementation
            // In a real implementation, you would properly convert JWK to RSA/EC public key
            // For this stub, we'll return a placeholder
            return new object();
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

    // ACME Data Models
    public class AcmeDirectory
    {
        [JsonPropertyName("newNonce")]
        public string NewNonce { get; set; }

        [JsonPropertyName("newAccount")]
        public string NewAccount { get; set; }

        [JsonPropertyName("newOrder")]
        public string NewOrder { get; set; }

        [JsonPropertyName("revokeCert")]
        public string RevokeCert { get; set; }

        [JsonPropertyName("keyChange")]
        public string KeyChange { get; set; }

        [JsonPropertyName("meta")]
        public DirectoryMeta Meta { get; set; }
    }

    public class DirectoryMeta
    {
        [JsonPropertyName("termsOfService")]
        public string TermsOfService { get; set; }

        [JsonPropertyName("website")]
        public string Website { get; set; }

        [JsonPropertyName("caaIdentities")]
        public string[] CaaIdentities { get; set; }

        [JsonPropertyName("externalAccountRequired")]
        public bool ExternalAccountRequired { get; set; }
    }

    public class JwsPayload
    {

        [JsonProperty("protected")]
        public string Protected { get; set; }

        [JsonProperty("payload")]
        public string Payload { get; set; }

        [JsonProperty("signature")]
        public string Signature { get; set; }
    }

    public class NewAccountRequest
    {
        [JsonPropertyName("contact")]
        public string[] Contact { get; set; }

        [JsonPropertyName("termsOfServiceAgreed")]
        public bool TermsOfServiceAgreed { get; set; }

        [JsonPropertyName("externalAccountBinding")]
        public ExternalAccountBinding ExternalAccountBinding { get; set; }
    }

    public class ExternalAccountBinding
    {
        [JsonPropertyName("protected")]
        public string Protected { get; set; }

        [JsonPropertyName("payload")]
        public string Payload { get; set; }

        [JsonPropertyName("signature")]
        public string Signature { get; set; }
    }

    public class AcmeAccount
    {
        [JsonPropertyName("status")]
        public AccountStatus Status { get; set; }

        [JsonPropertyName("contact")]
        public string[] Contact { get; set; }

        [JsonPropertyName("termsOfServiceAgreed")]
        public bool TermsOfServiceAgreed { get; set; }

        [JsonPropertyName("orders")]
        public string Orders { get; set; }
    }

    public class NewOrderRequest
    {
        [JsonPropertyName("identifiers")]
        public AcmeIdentifier[] Identifiers { get; set; }

        [JsonPropertyName("notBefore")]
        public DateTime? NotBefore { get; set; }

        [JsonPropertyName("notAfter")]
        public DateTime? NotAfter { get; set; }
    }

    public class AcmeOrder
    {
        [JsonPropertyName("status")]
        public OrderStatus Status { get; set; }

        [JsonPropertyName("expires")]
        public DateTime Expires { get; set; }

        [JsonPropertyName("identifiers")]
        public AcmeIdentifier[] Identifiers { get; set; }

        [JsonPropertyName("notBefore")]
        public DateTime? NotBefore { get; set; }

        [JsonPropertyName("notAfter")]
        public DateTime? NotAfter { get; set; }

        [JsonPropertyName("authorizations")]
        public List<string> Authorizations { get; set; }

        [JsonPropertyName("finalize")]
        public string Finalize { get; set; }

        [JsonPropertyName("certificate")]
        public string Certificate { get; set; }
    }

    public class AcmeIdentifier
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; }
    }

    public class AcmeAuthorization
    {
        [JsonPropertyName("identifier")]
        public AcmeIdentifier Identifier { get; set; }

        [JsonPropertyName("status")]
        public AuthorizationStatus Status { get; set; }

        [JsonPropertyName("expires")]
        public DateTime Expires { get; set; }

        [JsonPropertyName("challenges")]
        public List<AcmeChallenge> Challenges { get; set; }
    }

    public class AcmeChallenge
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("status")]
        public ChallengeStatus Status { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("token")]
        public string Token { get; set; }
    }

    public class FinalizeOrderRequest
    {
        [JsonPropertyName("csr")]
        public string Csr { get; set; }
    }

    public class AcmeError
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("detail")]
        public string Detail { get; set; }
    }

    // Status enums
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AccountStatus
    {
        Valid,
        Deactivated,
        Revoked
    }

    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OrderStatus
    {
        Pending,
        Ready,
        Processing,
        Valid,
        Invalid
    }

    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AuthorizationStatus
    {
        Pending,
        Valid,
        Invalid,
        Deactivated,
        Expired,
        Revoked
    }

    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChallengeStatus
    {
        Pending,
        Processing,
        Valid,
        Invalid
    }

    // Additional data models for JWS validation
    public class JwsProtectedHeader
    {
        [JsonProperty("alg")]
        public string Alg { get; set; }

        [JsonProperty("jwk")]
        public JsonWebKey Jwk { get; set; }

        [JsonProperty("kid")]
        public string Kid { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("nonce")]
        public string Nonce { get; set; }
    }

    public class JsonWebKey
    {
        [JsonProperty("kty")]
        public string Kty { get; set; }

        [JsonProperty("n")]
        public string N { get; set; }

        [JsonProperty("e")]
        public string E { get; set; }

        [JsonProperty("crv")]
        public string Crv { get; set; }

        [JsonProperty("x")]
        public string X { get; set; }

        [JsonProperty("y")]
        public string Y { get; set; }
    }
}
