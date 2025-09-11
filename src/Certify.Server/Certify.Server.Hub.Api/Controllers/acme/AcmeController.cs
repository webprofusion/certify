using Certify.Client;
using Certify.Server.Hub.Api.Models.Acme;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers.acme
{
    /// <summary>
    /// ACME API controller implementing minmimal RFC 8555 endpoints for certificate management, proxying orders via the managment hub
    /// </summary>
    [ApiController]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Route("acme")]
    public partial class AcmeController : ApiControllerBase
    {
        private readonly ILogger<AcmeController> _logger;
        private readonly ManagementAPI _mgmtAPI;
        private readonly IInstanceManagementStateProvider _stateProvider;
        private readonly AcmeBackgroundTaskService _backgroundTaskService;
        private readonly AcmeJwsValidator _jwsValidator;
        private readonly AcmeExternalAccountBindingValidator _eabValidator;
        private readonly AcmeHelper _acmeHelper;
        private readonly string _hubInstanceId;
        private readonly AcmeServerConfig _config;

        /// <summary>
        /// Initializes a new instance of the AcmeController
        /// </summary>
        public AcmeController(
            ILogger<AcmeController> logger,
            ManagementAPI mgmtAPI,
            IInstanceManagementStateProvider stateProvider,
            ICertifyInternalApiClient certifyInternalApi,
            AcmeServerConfig config,
            AcmeBackgroundTaskService backgroundTaskService,
            AcmeJwsValidator jwsValidator,
            AcmeExternalAccountBindingValidator eabValidator,
            AcmeHelper acmeHelper)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mgmtAPI = mgmtAPI ?? throw new ArgumentNullException(nameof(mgmtAPI));
            _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
            _backgroundTaskService = backgroundTaskService ?? throw new ArgumentNullException(nameof(backgroundTaskService));
            _jwsValidator = jwsValidator ?? throw new ArgumentNullException(nameof(jwsValidator));
            _eabValidator = eabValidator ?? throw new ArgumentNullException(nameof(eabValidator));
            _acmeHelper = acmeHelper ?? throw new ArgumentNullException(nameof(acmeHelper));

            _hubInstanceId = _stateProvider.GetManagementHubInstanceId();
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// ACME Directory endpoint - RFC 8555 Section 7.1.1
        /// </summary>
        /// <returns>Directory object with endpoint URLs</returns>
        [HttpGet("{key?}/directory")]
        [HttpGet("directory")]
        public IActionResult GetDirectory(string key = default!)
        {
            _acmeHelper.ValidateKeyIfSupplied(key);

            var baseUrl = AcmeHelper.BuildBaseUrl(Request, key);

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

        /// <summary>
        /// Issue new anti-replay nonce - RFC 8555 Section 6.5.1
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        [HttpHead("new-nonce")]
        [HttpGet("new-nonce")]
        [HttpHead("{key?}/new-nonce")]
        [HttpGet("{key?}/new-nonce")]
        public async Task<IActionResult> NewNonce(string key = default!)
        {
            await AddReplayNonceHeader();

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
            _acmeHelper.ValidateKeyIfSupplied(key);

            var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}";

            // Decode the JWS payload
            AccountRequest request;
            JsonWebKey newAccountKey;

            try
            {
                (request, newAccountKey) = await _jwsValidator.DecodeJwsWithAccountKey<AccountRequest>(payload, requestUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for new account request");
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Malformed, "Invalid JWS payload");
            }

            // Validate External Account Binding if required
            string validatedEabKeyInternalId = null;

            if (string.IsNullOrEmpty(key))
            {
                validatedEabKeyInternalId = await _eabValidator.ValidateExternalAccountBinding(request.ExternalAccountBinding, newAccountKey, requestUrl);
                if (string.IsNullOrEmpty(validatedEabKeyInternalId))
                {
                    return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.ExternalAccountRequired, "Invalid external account binding");
                }
            }
            else
            {
                // Validate access key instead
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.ExternalAccountRequired, "Invalid external account binding (key supplied but not supported)");
            }

            var accountId = AcmeHelper.GenerateAccountId();
            var baseUrl = AcmeHelper.BuildBaseUrl(Request, key);
            var account = new AcmeAccount
            {
                internalId = validatedEabKeyInternalId,
                Status = AccountStatus.Valid,
                Contact = request.Contact,
                TermsOfServiceAgreed = request.TermsOfServiceAgreed,
                Orders = $"{baseUrl}/account/{accountId}/orders",
            };

            var accountKid = AcmeHelper.BuildAccountUrl(baseUrl, accountId);

            // Store individual items persistently
            await _config.StoreAcmeAccount(accountKid, account);
            await _config.StoreAcmeAccountKey(accountKid, newAccountKey);

            await AddReplayNonceHeader();
            AddLocationHeader(AcmeHelper.BuildAccountUrl(baseUrl, accountId));

            return Created(accountKid, account);
        }

        /// <summary>
        /// Fetch account or deactivate
        /// </summary>
        /// <param name="payload"></param>
        /// <param name="accountId"></param>
        /// <returns></returns>
        // [HttpPost("{key}/account/{accountId}")]
        [HttpPost("account/{accountId}")]
        public async Task<IActionResult> Account([FromBody] JwsPayload payload, string accountId)
        {
            var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}";

            // Decode the JWS payload
            AccountRequest request;
            JsonWebKey newAccountKey;

            try
            {
                (request, newAccountKey) = await _jwsValidator.DecodeJwsWithAccountKey<AccountRequest>(payload, requestUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for new account request");
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Malformed, "Invalid JWS payload");
            }

            if (request.Status == "deactivated")
            {
                var matchedAccountKid = AcmeJwsValidator.GetAccountKidFromJwsPayload(payload);

                var deactivationAccount = await _config.GetAccount(matchedAccountKid);
                if (deactivationAccount == null)
                {
                    return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.ServerInternal, "Account not found");
                }

                // Remove from persistent storage
                await _config.RemoveAcmeAccount(matchedAccountKid);
                await _config.RemoveAcmeAccountKey(matchedAccountKid);

                // deactivated account
                return Ok(deactivationAccount);
            }
            else
            {
                var matchedAccountKid = AcmeJwsValidator.GetAccountKidFromJwsPayload(payload);

                var acc = await _config.GetAccount(matchedAccountKid);

                if (acc != null)
                {
                    return Ok(acc);
                }
                else
                {
                    return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.ServerInternal, "Account not found");
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
            _acmeHelper.ValidateKeyIfSupplied(key);

            var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}";

            // Decode the JWS payload
            NewOrderRequest request;
            try
            {
                request = await _jwsValidator.DecodeJwsPayload<NewOrderRequest>(payload, requestUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for new order request");
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Malformed, $"Invalid JWS payload: {ex.Message}");
            }

            //TODO: pre-check we can honor the required identifiers with a managed challenge, otherwise reject order

            var orderId = AcmeHelper.GenerateOrderId();
            var authorizationIds = new List<string>();

            var baseUrl = AcmeHelper.BuildBaseUrl(Request, key);

            // Create authorizations for each identifier
            foreach (var identifier in request.Identifiers)
            {
                var authId = AcmeHelper.GenerateAuthorizationId();

                var authorization = _acmeHelper.CreateAuthorization(identifier, baseUrl);

                authorizationIds.Add(AcmeHelper.BuildAuthorizationUrl(baseUrl, authId));

                await _config.StoreAcmeAuthorization(authId, authorization);
            }

            var order = new AcmeOrder
            {
                Id = orderId,
                Status = OrderStatus.Ready,
                Expires = DateTime.UtcNow.AddDays(30),
                Identifiers = request.Identifiers,
                NotBefore = request.NotBefore,
                NotAfter = request.NotAfter,
                Authorizations = authorizationIds,
                Finalize = $"{baseUrl}/order/{orderId}/finalize"
            };

            // create temp order in hub using a managed challenge
            var managedCert = AcmeHelper.PrepareManagedCertificate(orderId, request);

            var tempCert = await _mgmtAPI.UpdateManagedCertificate(_hubInstanceId, managedCert, CurrentAuthContext);
            if (tempCert == null)
            {
                _logger.LogError("Failed to create temporary managed certificate for order {OrderId}", orderId);
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.ServerInternal, "Failed to create temporary managed certificate");
            }

            order.ManagedCertificateId = tempCert.Id;

            // Store order
            await _config.StoreAcmeOrder(orderId, order);

            // Enqueue background task for order processing
            var taskEnqueued = await _backgroundTaskService.EnqueueOrderProcessingTask(
                orderId,
                tempCert.Id,
                CurrentAuthContext,
                _hubInstanceId);

            if (!taskEnqueued)
            {
                _logger.LogError("Failed to enqueue background task for order {OrderId}", orderId);
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.ServerInternal, "Failed to process order");
            }

            await AddReplayNonceHeader();

            var orderUrl = AcmeHelper.BuildOrderUrl(baseUrl, orderId);
            AddLocationHeader(orderUrl);

            return Created(orderUrl, order);
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
            _acmeHelper.ValidateKeyIfSupplied(key);

            var order = await _config.GetAcmeOrder(orderId);
            if (order == null)
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.OrderNotFound, "Order not found");
            }

            if (order.Status == OrderStatus.Invalid)
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.OrderNotReady, "Order has failed. Cannot complete finalization");
            }

            // Check if order is ready for finalization
            if (order.Status != OrderStatus.ReadyForInternalFinalization && order.Status != OrderStatus.Ready)
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.OrderNotReady, "Order is not ready for finalization");
            }

            var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}";

            // Decode the JWS payload
            FinalizeOrderRequest request;
            try
            {
                request = await _jwsValidator.DecodeJwsPayload<FinalizeOrderRequest>(payload, requestUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for finalize order request");
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Malformed, "Invalid JWS payload");
            }

            // Check if finalization is already in progress
            if (order.Status != OrderStatus.InternalFinalizationInProgress)
            {
                var baseUrl = AcmeHelper.BuildBaseUrl(Request, key);

                // Enqueue background task for order finalization
                var taskEnqueued = await _backgroundTaskService.EnqueueOrderFinalizationTask(
                    orderId,
                    request.Csr,
                    baseUrl,
                    CurrentAuthContext,
                    _hubInstanceId);

                if (!taskEnqueued)
                {
                    _logger.LogError("Failed to enqueue finalization task for order {OrderId}", orderId);
                    return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.ServerInternal, "Failed to process finalization");
                }
            }

            // Update order status to processing
            order.Status = OrderStatus.Processing;
            await _config.StoreAcmeOrder(orderId, order);

            await AddReplayNonceHeader();

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
            _acmeHelper.ValidateKeyIfSupplied(key);

            var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}";

            try
            {
                _ = await _jwsValidator.DecodeJwsForPostAsGet<object>(payload, requestUrl, "certificate request");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for certificate request");
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Malformed, "Invalid JWS payload");
            }

            var baseUrl = AcmeHelper.BuildBaseUrl(Request, key);
            var certUri = AcmeHelper.BuildCertificateUrl(baseUrl, certId);
            var order = await _config.GetAcmeOrderByCertificateUri(certUri);
            if (order == null)
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Malformed, "Invalid or unknown certId");
            }

            var managedCert = await _mgmtAPI.GetManagedCertificate(_hubInstanceId, order.ManagedCertificateId, CurrentAuthContext);
            var result = await _mgmtAPI.ExportCertificate(_hubInstanceId, order.ManagedCertificateId, "pem_fullchain", CurrentAuthContext);

            if (result?.Result == null)
            {
                _logger.LogError("Failed to export certificate for order {OrderId}", order.Id);
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.ServerInternal, "Failed to export certificate");
            }

            var certPEM = System.Text.Encoding.UTF8.GetString(result.Result);

            // delete order and temp managed cert
            await _mgmtAPI.RemoveManagedCertificate(_hubInstanceId, order.ManagedCertificateId, CurrentAuthContext);
            await _config.RemoveAcmeOrder(order.Id);

            await AddReplayNonceHeader();

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
            _acmeHelper.ValidateKeyIfSupplied(key);

            var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}";

            try
            {
                _ = await _jwsValidator.DecodeJwsForPostAsGet<object>(payload, requestUrl, "order status request");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for order status request");
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Malformed, "Invalid JWS payload");
            }

            var order = await _config.GetAcmeOrder(orderId);

            if (order == null)
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.OrderNotFound, "Order not found");
            }

            await AddReplayNonceHeader();

            if (order.Status == OrderStatus.ReadyForInternalFinalization)
            {
                order.Status = OrderStatus.Processing;
            }
            else if (order.Status == OrderStatus.InternalFinalizationInProgress)
            {
                order.Status = OrderStatus.Processing;
            }

            if (order.Status == OrderStatus.Processing)
            {
                AddRetryAfterHeader(5);
            }

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
        public async Task<IActionResult> GetAuthorization(string authId, [FromBody] JwsPayload payload, string key = default!)
        {
            _acmeHelper.ValidateKeyIfSupplied(key);

            var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}";

            try
            {
                _ = await _jwsValidator.DecodeJwsForPostAsGet<object>(payload, requestUrl, "authorization request");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for authorization request");
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Malformed, "Invalid JWS payload");
            }

            var authorization = await _config.GetAcmeAuthorization(authId);
            if (authorization == null)
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.AuthorizationNotFound, "Authorization not found");
            }

            await AddReplayNonceHeader();
            return Ok(authorization);
        }

        private async Task AddReplayNonceHeader()
        {
            Response.Headers.Append("Replay-Nonce", await _acmeHelper.GenerateNonce());
        }

        private void AddLocationHeader(string location)
        {
            Response.Headers.Append("Location", location);
        }

        private void AddRetryAfterHeader(int seconds = 5)
        {
            Response.Headers.Append("Retry-After", seconds.ToString());
        }
    }
}

