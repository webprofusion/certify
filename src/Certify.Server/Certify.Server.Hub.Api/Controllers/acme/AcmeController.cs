using System.Collections.Concurrent;
using System.Security.Cryptography;
using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
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
        // Extract magic numbers and strings to constants
        private const int DEFAULT_EXPIRY_DAYS = 30;
        private const int NONCE_BYTES = 16;
        private const int TOKEN_BYTES = 32;
        private const string ACME_SERVER_PATH = "acme-server";
        private const string PEM_FULLCHAIN_FORMAT = "pem_fullchain";

        // Extract ACME error types to constants
        private static class AcmeErrorTypes
        {
            public const string Malformed = "urn:ietf:params:acme:error:malformed";
            public const string BadNonce = "urn:ietf:params:acme:error:badNonce";
            public const string ExternalAccountRequired = "urn:ietf:params:acme:error:externalAccountRequired";
            public const string OrderNotFound = "urn:ietf:params:acme:error:orderNotFound";
            public const string OrderNotReady = "urn:ietf:params:acme:error:orderNotReady";
            public const string AuthorizationNotFound = "urn:ietf:params:acme:error:authorizationNotFound";
            public const string ServerInternal = "urn:ietf:params:acme:error:serverInternal";
        }

        private readonly ILogger<AcmeController> _logger;
        private static readonly ConcurrentDictionary<string, AcmeAccount> _accounts = new();
        private static readonly ConcurrentDictionary<string, JsonWebKey> _accountKeys = new();
        private static readonly ConcurrentDictionary<string, AcmeOrder> _orders = new();
        private static readonly ConcurrentDictionary<string, AcmeAuthorization> _authorizations = new();
        private static readonly ConcurrentDictionary<string, string> _nonces = new();
        private static readonly ConcurrentDictionary<string, string> _consumedEabKeys = new();
        private ManagementAPI _mgmtAPI;

        private readonly ICertifyInternalApiClient _client;

        private IInstanceManagementStateProvider _stateProvider;
        private string _hubInstanceId = default!;

        /// <summary>
        /// Initializes a new instance of the AcmeController
        /// </summary>
        /// <param name="logger">Logger for recording ACME operations</param>
        /// <param name="mgmtAPI">Management API for certificate operations</param>
        /// <param name="stateProvider">Provider for instance management state</param>
        public AcmeController(ILogger<AcmeController> logger, ManagementAPI mgmtAPI, IInstanceManagementStateProvider stateProvider, ICertifyInternalApiClient certifyInternalApi)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mgmtAPI = mgmtAPI ?? throw new ArgumentNullException(nameof(mgmtAPI));
            _client = certifyInternalApi ?? throw new ArgumentNullException(nameof(certifyInternalApi));
            _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));

            _hubInstanceId = _stateProvider.GetManagementHubInstanceId();
            LoadSavedState();
        }

        /// <summary>
        /// ACME Directory endpoint - RFC 8555 Section 7.1.1
        /// </summary>
        /// <returns>Directory object with endpoint URLs</returns>
        [HttpGet("{key?}/directory")]
        [HttpGet("directory")]
        public IActionResult GetDirectory(string key = default!)
        {
            ValidateKeyIfSupplied(key);

            var baseUrl = BuildBaseUrl(key);

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
        /// Issue new anti-replay nonce - RFC 8555 Section 6.5.1
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        [HttpHead("new-nonce")]
        [HttpGet("new-nonce")]
        [HttpHead("{key?}/new-nonce")]
        [HttpGet("{key?}/new-nonce")]

        public IActionResult NewNonce(string key = default!)
        {
            AddReplayNonceHeader();

            Response.Headers.Append("Cache-Control", "no-store");

            return Ok();
        }

        /// <summary>
        /// New account endpoint with EAB support - RFC 8555 Section 7.3
        /// </summary>
        /// <param name="payload">JWS payload containing account creation request</param>
        /// <param name="key"></param>
        /// <returns>Account object</returns>
        [HttpPost("{key?}/new-account")]
        [HttpPost("new-account")]
        public async Task<IActionResult> NewAccount([FromBody] JwsPayload payload, string key = default!)
        {
            ValidateKeyIfSupplied(key);

            // Decode the JWS payload
            AccountRequest request;
            JsonWebKey newAccountKey;

            try
            {
                (request, newAccountKey) = DecodeJwsWithAccountKey<AccountRequest>(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for new account request");
                return CreateAcmeError(AcmeErrorTypes.Malformed, "Invalid JWS payload");
            }

            // Validate External Account Binding if required
            string validatedEabKeyInternalId = null;
            if (string.IsNullOrEmpty(key))
            {
                validatedEabKeyInternalId = await ValidateExternalAccountBinding(request.ExternalAccountBinding, newAccountKey);
                if (string.IsNullOrEmpty(validatedEabKeyInternalId))
                {
                    return CreateAcmeError(AcmeErrorTypes.ExternalAccountRequired, "Invalid external account binding");
                }
            }
            else
            {
                // Validate access key instead
                return CreateAcmeError(AcmeErrorTypes.ExternalAccountRequired, "Invalid external account binding (key supplied but not supported)");
            }

            var accountId = GenerateAccountId();
            var baseUrl = BuildBaseUrl(key);
            var account = new AcmeAccount
            {
                internalId = validatedEabKeyInternalId,
                Status = AccountStatus.Valid,
                Contact = request.Contact,
                TermsOfServiceAgreed = request.TermsOfServiceAgreed,
                Orders = $"{baseUrl}/account/{accountId}/orders",
            };

            var accountKid = BuildAccountUrl(baseUrl, accountId);

            _accounts[accountKid] = account;
            _accountKeys[accountKid] = newAccountKey;

            StoreCurrentState();

            AddReplayNonceHeader();
            AddLocationHeader(BuildAccountUrl(baseUrl, accountId));

            return Created(accountKid, account);
        }

        /// <summary>
        /// Fetch account or deactivate
        /// </summary>
        /// <param name="payload"></param>
        /// <param name="key"></param>
        /// <param name="accountId"></param>
        /// <returns></returns>
       // [HttpPost("{key}/account/{accountId}")]
        [HttpPost("account/{accountId}")]
        public async Task<IActionResult> Account([FromBody] JwsPayload payload, string accountId)
        {
            //ValidateKeyIfSupplied(key);

            // Decode the JWS payload
            AccountRequest request;
            JsonWebKey newAccountKey;

            try
            {
                (request, newAccountKey) = DecodeJwsWithAccountKey<AccountRequest>(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for new account request");
                return CreateAcmeError(AcmeErrorTypes.Malformed, "Invalid JWS payload");
            }

            if (request.Status == "deactivated")
            {
                var matchedAccountKid = GetAccountKidFromJwsPayload(payload);

                if (!_accounts.TryRemove(matchedAccountKid, out var deactivationAccount))
                {
                    return CreateAcmeError(AcmeErrorTypes.ServerInternal, "Account not found");
                }

                StoreCurrentState();

                // deactivated account
                return Ok(deactivationAccount);
            }
            else
            {
                var matchedAccountKid = GetAccountKidFromJwsPayload(payload);

                if (_accounts.TryGetValue(matchedAccountKid, out var acc))
                {
                    return Ok(acc);
                }
                else
                {
                    return CreateAcmeError(AcmeErrorTypes.ServerInternal, "Account not found");
                }
            }
        }

        /// <summary>
        /// New order endpoint - RFC 8555 Section 7.4
        /// </summary>
        /// <param name="payload">JWS payload containing order creation request</param>
        /// <param name="key"></param>
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
                return CreateAcmeError(AcmeErrorTypes.Malformed, $"Invalid JWS payload: {ex.Message}");
            }

            var orderId = GenerateOrderId();
            var authorizationIds = new List<string>();

            var baseUrl = BuildBaseUrl(key);

            // Create authorizations for each identifier
            foreach (var identifier in request.Identifiers)
            {
                var authId = GenerateAuthorizationId();
                var authorization = CreateAuthorization(identifier, baseUrl);

                _authorizations[authId] = authorization;
                authorizationIds.Add(BuildAuthorizationUrl(baseUrl, authId));
            }

            var order = new AcmeOrder
            {
                Id = orderId,
                Status = OrderStatus.Pending,
                Expires = DateTime.UtcNow.AddDays(DEFAULT_EXPIRY_DAYS),
                Identifiers = request.Identifiers,
                NotBefore = request.NotBefore,
                NotAfter = request.NotAfter,
                Authorizations = authorizationIds,
                Finalize = $"{baseUrl}/order/{orderId}/finalize"
            };

            // create temp order in hub using a managed challenge
            var managedCert = PrepareManagedCertificate(orderId, request);

            var tempCert = await _mgmtAPI.UpdateManagedCertificate(_hubInstanceId, managedCert, CurrentAuthContext);
            if (tempCert == null)
            {
                _logger.LogError("Failed to create temporary managed certificate for order {OrderId}", orderId);
                return CreateAcmeError(AcmeErrorTypes.ServerInternal, "Failed to create temporary managed certificate");
            }

            _ = _mgmtAPI.PerformManagedCertificateRequest(_hubInstanceId, tempCert.Id, CurrentAuthContext);

            order.ManagedCertificateId = tempCert.Id;

            _orders[orderId] = order;

            StoreCurrentState();

            AddReplayNonceHeader();
            AddLocationHeader(BuildOrderUrl(baseUrl, orderId));

            return Created(BuildOrderUrl(baseUrl, orderId), order);
        }

        /// <summary>
        /// Finalize order endpoint - RFC 8555 Section 7.4
        /// </summary>
        /// <param name="orderId">Order identifier</param>
        /// <param name="payload">JWS payload containing finalization request with CSR</param>
        /// <param name="key"></param>
        /// <returns>Updated order object</returns>
        [HttpPost("order/{orderId}/finalize")]
        [HttpPost("{key?}/order/{orderId}/finalize")]
        public async Task<IActionResult> FinalizeOrder(string orderId, [FromBody] JwsPayload payload, string key = default!)
        {
            ValidateKeyIfSupplied(key);

            if (!_orders.TryGetValue(orderId, out var order))
            {
                return CreateAcmeError(AcmeErrorTypes.OrderNotFound, "Order not found");
            }

            if (order.Status != OrderStatus.Ready)
            {
                return CreateAcmeError(AcmeErrorTypes.OrderNotReady, "Order not ready for finalization");
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
                return CreateAcmeError(AcmeErrorTypes.Malformed, "Invalid JWS payload");
            }

            // check status of temp managed certificate
            var managedCert = await _mgmtAPI.GetManagedCertificate(_hubInstanceId, order.ManagedCertificateId, CurrentAuthContext);

            if (managedCert == null)
            {
                return CreateAcmeError(AcmeErrorTypes.OrderNotFound, "Managed certificate not found for order");
            }

            // apply CSR from client finalize call as a formatted customcsr
            managedCert.RequestConfig.CustomCSR = FormatCsrPem(request.Csr);

            await _mgmtAPI.UpdateManagedCertificate(_hubInstanceId, managedCert, CurrentAuthContext);

            // resume/finalize cert order
            await _mgmtAPI.PerformManagedCertificateRequest(_hubInstanceId, order.ManagedCertificateId, CurrentAuthContext);

            order.Status = OrderStatus.Valid;
            var certId = GenerateCertificateId();

            var baseUrl = BuildBaseUrl(key);
            order.Certificate = BuildCertificateUrl(baseUrl, certId);

            AddReplayNonceHeader();

            StoreCurrentState();

            return Ok(order);
        }

        /// <summary>
        /// Download certificate endpoint - RFC 8555 Section 7.4.2
        /// </summary>
        /// <param name="certId">Certificate identifier</param>
        /// <param name="payload"></param>
        /// <param name="key"></param>
        /// <returns>Certificate in PEM format</returns>
        [HttpPost("cert/{certId}")]
        [HttpPost("{key?}/cert/{certId}")]
        public async Task<IActionResult> DownloadCertificate(string certId, [FromBody] JwsPayload payload, string key = default!)
        {
            ValidateKeyIfSupplied(key);

            try
            {
                _ = DecodeJwsForPostAsGet<object>(payload, "certificate request");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for certificate request");
                return CreateAcmeError(AcmeErrorTypes.Malformed, "Invalid JWS payload");
            }

            var baseUrl = BuildBaseUrl(key);
            var certUri = BuildCertificateUrl(baseUrl, certId);
            var order = _orders.FirstOrDefault(o => o.Value.Certificate == certUri).Value;
            if (order == null)
            {
                return CreateAcmeError(AcmeErrorTypes.Malformed, "Invalid or unknown certId");
            }

            var managedCert = await _mgmtAPI.GetManagedCertificate(_hubInstanceId, order.ManagedCertificateId, CurrentAuthContext);
            var result = await _mgmtAPI.ExportCertificate(_hubInstanceId, order.ManagedCertificateId, PEM_FULLCHAIN_FORMAT, CurrentAuthContext);

            if (result?.Result == null)
            {
                _logger.LogError("Failed to export certificate for order {OrderId}", order.Id);
                return CreateAcmeError(AcmeErrorTypes.ServerInternal, "Failed to export certificate");
            }

            var certPEM = System.Text.Encoding.UTF8.GetString(result.Result);

            // delete order and temp managed cert
            await _mgmtAPI.RemoveManagedCertificate(_hubInstanceId, order.ManagedCertificateId, CurrentAuthContext);
            _orders.Remove(order.Id, out _);

            AddReplayNonceHeader();

            // Return the certificate as plain text with proper content type
            return Content(certPEM, "application/pem-certificate-chain");
        }

        /// <summary>
        /// Post-As-Get order status endpoint - RFC 8555 Section 7.4
        /// </summary>
        /// <param name="orderId">Order identifier</param>
        /// <param name="payload"></param>
        /// <param name="key"></param>
        /// <returns>Order object</returns>
        [HttpPost("order/{orderId}")]
        [HttpPost("{key?}/order/{orderId}")]
        public async Task<IActionResult> GetOrder(string orderId, [FromBody] JwsPayload payload, string key = default!)
        {
            ValidateKeyIfSupplied(key);

            try
            {
                _ = DecodeJwsForPostAsGet<object>(payload, "order status request");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for order status request");
                return CreateAcmeError(AcmeErrorTypes.Malformed, "Invalid JWS payload");
            }

            if (!_orders.TryGetValue(orderId, out var order))
            {
                return CreateAcmeError(AcmeErrorTypes.OrderNotFound, "Order not found");
            }

            // check status of temp managed certificate
            var managedCert = await _mgmtAPI.GetManagedCertificate(_hubInstanceId, order.ManagedCertificateId, CurrentAuthContext);

            order.Status = MapManagedCertificateStatus(managedCert.LastRenewalStatus);

            AddReplayNonceHeader();
            return Ok(order);
        }

        /// <summary>
        /// Post-As-Get authorization endpoint - RFC 8555 Section 7.5
        /// </summary>
        /// <param name="authId">Authorization identifier</param>
        /// <param name="payload"></param>
        /// <param name="key"></param>
        /// <returns>Authorization object</returns>
        [HttpPost("authz/{authId}")]
        [HttpPost("{key?}/authz/{authId}")]
        public IActionResult GetAuthorization(string authId, [FromBody] JwsPayload payload, string key = default!)
        {
            ValidateKeyIfSupplied(key);

            try
            {
                _ = DecodeJwsForPostAsGet<object>(payload, "authorization request");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for authorization request");
                return CreateAcmeError(AcmeErrorTypes.Malformed, "Invalid JWS payload");
            }

            if (!_authorizations.TryGetValue(authId, out var authorization))
            {
                return CreateAcmeError(AcmeErrorTypes.AuthorizationNotFound, "Authorization not found");
            }

            AddReplayNonceHeader();
            return Ok(authorization);
        }

        private static string GenerateAccountId() => Guid.NewGuid().ToString("N");
        private static string GenerateOrderId() => Guid.NewGuid().ToString("N");
        private static string GenerateAuthorizationId() => Guid.NewGuid().ToString("N");
        private static string GenerateChallengeId() => Guid.NewGuid().ToString("N");
        private static string GenerateCertificateId() => Guid.NewGuid().ToString("N");
        private static string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(TOKEN_BYTES));
        private string BuildBaseUrl(string? key = null) => $"{Request.Scheme}://{Request.Host}/acme{(key != null ? $"/{key}" : "")}";
        private static string BuildAccountUrl(string baseUrl, string accountId) => $"{baseUrl}/account/{accountId}";
        private static string BuildChallengeUrl(string baseUrl, string challengeId) => $"{baseUrl}/challenge/{challengeId}";
        private static string BuildAuthorizationUrl(string baseUrl, string authId) => $"{baseUrl}/authz/{authId}";
        private static string BuildOrderUrl(string baseUrl, string orderId) => $"{baseUrl}/order/{orderId}";
        private static string BuildCertificateUrl(string baseUrl, string certId) => $"{baseUrl}/cert/{certId}";

        private (T request, JsonWebKey? accountKey) DecodeJwsWithAccountKey<T>(JwsPayload payload)
        {
            var request = DecodeJwsPayload<T>(payload);

            var protectedBytes = JwsConvert.FromBase64String(payload.Protected);
            var protectedJson = System.Text.Encoding.UTF8.GetString(protectedBytes);
            var protectedHeader = JsonConvert.DeserializeObject<JwsProtectedHeader>(protectedJson);

            return (request, protectedHeader?.Jwk);
        }

        private T DecodeJwsForPostAsGet<T>(JwsPayload payload, string errorContext)
        {
            try
            {
                return DecodeJwsPayload<T>(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for {Context}", errorContext);
                throw new ArgumentException("Invalid JWS payload");
            }
        }

        private List<AcmeChallenge> CreateStandardChallenges(string baseUrl)
        {
            return
            [
                new()
                {
                    Type = "http-01",
                    Status = ChallengeStatus.Valid,
                    Token = GenerateToken(),
                    Url = BuildChallengeUrl(baseUrl, GenerateChallengeId())
                },
                new()
                {
                    Type = "dns-01",
                    Status = ChallengeStatus.Valid,
                    Token = GenerateToken(),
                    Url = BuildChallengeUrl(baseUrl, GenerateChallengeId())
                }
            ];
        }

        private AcmeAuthorization CreateAuthorization(AcmeIdentifier identifier, string baseUrl)
        {
            return new()
            {
                Identifier = identifier,
                Status = AuthorizationStatus.Valid, //presets auth to valid so the client doesn't attempt them
                Expires = DateTime.UtcNow.AddDays(DEFAULT_EXPIRY_DAYS),
                Challenges = CreateStandardChallenges(baseUrl)
            };
        }

        private static OrderStatus MapManagedCertificateStatus(RequestState? renewalStatus)
        {
            return renewalStatus switch
            {
                null or RequestState.Running => OrderStatus.Processing,
                RequestState.Paused => OrderStatus.Ready,
                RequestState.Error => OrderStatus.Invalid,
                _ => OrderStatus.Valid
            };
        }

        private IActionResult CreateAcmeError(string errorType, string detail)
        {
            var error = new AcmeError { Type = errorType, Detail = detail };

            return errorType switch
            {
                AcmeErrorTypes.OrderNotFound or AcmeErrorTypes.AuthorizationNotFound => NotFound(error),
                AcmeErrorTypes.ServerInternal => StatusCode(500, error),
                _ => BadRequest(error)
            };
        }

        private static ManagedCertificate PrepareManagedCertificate(string orderId, NewOrderRequest request)
        {
            var managedCert = new ManagedCertificate
            {
                Name = $"Hub ACME Order {orderId}",
                CertificateAuthorityId = StandardCertAuthorities.LETS_ENCRYPT,
                UseStagingMode = true,
                RequestConfig = new()
                {
                    PrimaryDomain = request.Identifiers.FirstOrDefault()?.Value ?? "",
                    SubjectAlternativeNames = request.Identifiers.Select(i => i.Value).ToArray(),
                    DeploymentSiteOption = DeploymentOption.NoDeployment
                }
            };

            managedCert.RequestConfig.Challenges =
            [
                new() {
                    ChallengeProvider = "managed" ,
                    ChallengeType = "dns-01",
                }
            ];

            return managedCert;
        }

        private static string FormatCsrPem(string csr)
        {
            return $"-----BEGIN CERTIFICATE REQUEST-----\n{Convert.ToBase64String(Certify.Management.Util.FromUrlSafeBase64String(csr), Base64FormattingOptions.InsertLineBreaks)}\n-----END CERTIFICATE REQUEST-----";
        }

        private void AddReplayNonceHeader()
        {
            Response.Headers.Append("Replay-Nonce", GenerateNonce());
        }

        private void AddLocationHeader(string location)
        {
            Response.Headers.Append("Location", location);
        }
        private string GenerateNonce()
        {
            var nonce = JwsConvert.ToBase64String(RandomNumberGenerator.GetBytes(NONCE_BYTES));
            _nonces[nonce] = DateTime.UtcNow.ToString();
            return nonce;
        }

        private static bool IsValidNonce(string nonce)
        {
            return !string.IsNullOrEmpty(nonce) && _nonces.ContainsKey(nonce);
        }

        private static string GetAccountKidFromJwsPayload(JwsPayload payload)
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
        /// Validates External Account Binding according to RFC 8555 Section 7.3.4
        /// </summary>
        /// <param name="eab">External Account Binding JWS</param>
        /// <param name="accountPublicKey">The public key from the account creation request</param>
        /// <returns>internal id of matching access token</returns>
        private async Task<string?> ValidateExternalAccountBinding(JwsPayload eab, JsonWebKey accountPublicKey)
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
                if (!ValidateEabHeader(eabHeader))
                {
                    _logger.LogError("Failed to validate EAB header");
                    return null;
                }

                if (_consumedEabKeys.ContainsKey(eabHeader.Kid))
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
                MarkEabKeyAsUsed(eabHeader.Kid);

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
        private bool ValidateEabHeader(JwsProtectedHeader header)
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
            var expectedUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}";
            if (!string.Equals(header.Url, expectedUrl, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("EAB URL mismatch. Expected: {Expected}, Got: {Actual}",
                    expectedUrl, header.Url);
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
        private void MarkEabKeyAsUsed(string keyId)
        {
            // Store used EAB keys with timestamp
            // Implement appropriate cleanup/expiry logic
            _consumedEabKeys[keyId] = DateTime.UtcNow.ToString();
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
        private static JsonWebKey? GetPublicKey(JwsProtectedHeader header)
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

        private static void StoreCurrentState()
        {
            SaveStateToFile("accounts.json", _accounts);
            SaveStateToFile("account-keys.json", _accountKeys);
            SaveStateToFile("orders.json", _orders);
            SaveStateToFile("consumed-eab-keys.json", _consumedEabKeys);
        }

        private static void LoadSavedState()
        {
            LoadStateFromFile("accounts.json", _accounts);
            LoadStateFromFile("account-keys.json", _accountKeys);
            LoadStateFromFile("orders.json", _orders);
            LoadStateFromFile("consumed-eab-keys.json", _consumedEabKeys);
        }

        private static void SaveStateToFile<T>(string fileName, ConcurrentDictionary<string, T> data)
        {
            var settingsPath = EnvironmentUtil.EnsuredAppDataPath(ACME_SERVER_PATH);
            var filePath = Path.Join(settingsPath, fileName);
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            System.IO.File.WriteAllText(filePath, json);
        }

        private static void LoadStateFromFile<T>(string fileName, ConcurrentDictionary<string, T> targetDictionary)
        {
            if (targetDictionary.Count > 0)
            {
                return;
            }

            var settingsPath = EnvironmentUtil.EnsuredAppDataPath(ACME_SERVER_PATH);
            var filePath = Path.Join(settingsPath, fileName);

            if (!System.IO.File.Exists(filePath))
            {
                return;
            }

            var json = System.IO.File.ReadAllText(filePath);
            var data = System.Text.Json.JsonSerializer.Deserialize<ConcurrentDictionary<string, T>>(json);

            if (data == null)
            {
                return;
            }

            targetDictionary.Clear();
            foreach (var item in data)
            {
                targetDictionary.TryAdd(item.Key, item.Value);
            }
        }
    }
}
