using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Models.Acme;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.Services.Acme;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Certify.Shared.Core.Utils.PKI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Certify.Server.Hub.Api.Controllers.acme
{
    /// <summary>
    /// ACME API controller implementing minmimal RFC 8555 endpoints for certificate management, proxying orders via the managment hub
    /// </summary>
    [ApiController]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Route("acme")]
    [EnableRateLimiting(RateLimitingExtension.AcmePolicy)]
    // RFC 8555 Section 6.5 - every response from this controller carries a fresh replay nonce,
    // including error responses, so a client which fails a request can immediately retry.
    [ServiceFilter(typeof(AcmeReplayNonceFilter))]
    public partial class AcmeController : ApiControllerBase
    {
        private readonly ILogger<AcmeController> _logger;
        private readonly ManagementAPI _mgmtAPI;
        private readonly IInstanceManagementStateProvider _stateProvider;
        private readonly AcmeBackgroundTaskService _backgroundTaskService;
        private readonly AcmeJwsValidator _jwsValidator;
        private readonly AcmeExternalAccountBindingValidator _eabValidator;
        private readonly ManagedChallengeScopeService _managedChallengeScopeService;
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
            ManagedChallengeScopeService managedChallengeScopeService,
            AcmeHelper acmeHelper)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mgmtAPI = mgmtAPI ?? throw new ArgumentNullException(nameof(mgmtAPI));
            _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
            _backgroundTaskService = backgroundTaskService ?? throw new ArgumentNullException(nameof(backgroundTaskService));
            _jwsValidator = jwsValidator ?? throw new ArgumentNullException(nameof(jwsValidator));
            _eabValidator = eabValidator ?? throw new ArgumentNullException(nameof(eabValidator));
            _managedChallengeScopeService = managedChallengeScopeService ?? throw new ArgumentNullException(nameof(managedChallengeScopeService));
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
        public IActionResult NewNonce(string key = default!)
        {
            // the replay nonce itself is added to every response by AcmeReplayNonceFilter
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
                // new-account is the only flow where the caller supplies their account key inline via 'jwk'
                (request, newAccountKey) = await _jwsValidator.DecodeJwsWithAccountKey<AccountRequest>(payload, requestUrl, requireAccountKid: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for new account request");
                return AcmeErrorResponseService.CreateAcmeErrorForException(ex, "Invalid JWS payload");
            }

            if (newAccountKey == null)
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Malformed, "JWS header must contain 'jwk' for new account requests");
            }

            // Validate External Account Binding if required
            string validatedEabKeyInternalId;
            string owningSecurityPrincipalId;
            List<string> owningScopedAssignedRoles = [];

            if (string.IsNullOrEmpty(key))
            {
                var eabResult = await _eabValidator.ValidateExternalAccountBinding(request.ExternalAccountBinding, newAccountKey, requestUrl);
                if (eabResult?.IsValid != true)
                {
                    var failureReason = eabResult?.FailureReason ?? "Invalid external account binding";
                    return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.ExternalAccountRequired, failureReason);
                }

                validatedEabKeyInternalId = eabResult.TokenInternalId!;
                owningSecurityPrincipalId = eabResult.SecurityPrincipalId!;
                owningScopedAssignedRoles = eabResult.ScopedAssignedRoles ?? [];
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
                SecurityPrincipalId = owningSecurityPrincipalId,
                ScopedAssignedRoles = owningScopedAssignedRoles,
                Status = AccountStatus.Valid,
                Contact = request.Contact,
                TermsOfServiceAgreed = request.TermsOfServiceAgreed,
                Orders = $"{baseUrl}/account/{accountId}/orders",
            };

            var accountKid = AcmeHelper.BuildAccountUrl(baseUrl, accountId);

            // Store individual items persistently
            await _config.StoreAcmeAccount(accountKid, account);
            await _config.StoreAcmeAccountKey(accountKid, newAccountKey);

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

            try
            {
                // signature is verified against the registered account key referenced by 'kid'
                request = await _jwsValidator.DecodeJwsPayload<AccountRequest>(payload, requestUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for account request");
                return AcmeErrorResponseService.CreateAcmeErrorForException(ex, "Invalid JWS payload");
            }

            var matchedAccountKid = AcmeJwsValidator.GetAccountKidFromJwsPayload(payload);

            // the signing account may only act on its own account resource
            if (!string.Equals(matchedAccountKid, AcmeHelper.BuildAccountUrl(AcmeHelper.BuildBaseUrl(Request), accountId), StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Account request for {AccountId} did not match the signing account", accountId);
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Unauthorized, "Account not found");
            }

            var acc = await _config.GetAccount(matchedAccountKid);
            if (acc == null)
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Unauthorized, "Account not found");
            }

            if (request?.Status == "deactivated")
            {
                // Remove from persistent storage
                await _config.RemoveAcmeAccount(matchedAccountKid);
                await _config.RemoveAcmeAccountKey(matchedAccountKid);

                acc.Status = AccountStatus.Deactivated;
            }


            return Ok(acc);
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

            // Decode the JWS payload. Signature is verified against the registered account key referenced by 'kid'.
            NewOrderRequest request;
            try
            {
                request = await _jwsValidator.DecodeJwsPayload<NewOrderRequest>(payload, requestUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for new order request");
                return AcmeErrorResponseService.CreateAcmeErrorForException(ex, $"Invalid JWS payload: {ex.Message}");
            }

            // Resolve the authenticated account which signed this request
            var accountKid = AcmeJwsValidator.GetAccountKidFromJwsPayload(payload);
            var account = await _config.GetAccount(accountKid);

            if (account == null || account.Status != AccountStatus.Valid)
            {
                _logger.LogWarning("New order request rejected, account {AccountKid} is unknown or not valid", accountKid);
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Unauthorized, "Account is unknown or not valid");
            }

            // The account must be linked to a security principal authorised to perform managed ACME orders.
            // Accounts registered before ownership tracking was introduced have no principal and must re-register.
            if (!await _eabValidator.HasManagedAcmeAccess(account.SecurityPrincipalId, account.ScopedAssignedRoles))
            {
                _logger.LogWarning("New order request rejected, account {AccountKid} is not authorised to perform managed ACME orders", accountKid);
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Unauthorized, "Account is not authorised to perform managed ACME orders");
            }

            if (request?.Identifiers == null || request.Identifiers.Length == 0)
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Malformed, "Order must contain at least one identifier");
            }

            // Reject orders that cannot be satisfied by a managed challenge within the principal's role scope.
            var identifierValues = request.Identifiers.Select(i => i.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            var challengeScopeCheck = await _managedChallengeScopeService.ValidatePrincipalCanSatisfyIdentifiers(
                account.SecurityPrincipalId,
                identifierValues,
                account.ScopedAssignedRoles,
                StandardResourceActions.ManagedAcmePerformOrder);

            if (!challengeScopeCheck.CanSatisfy)
            {
                _logger.LogWarning(
                    "New order request rejected for account {AccountKid}: {Reason}",
                    accountKid,
                    challengeScopeCheck.FailureReason);

                return AcmeErrorResponseService.CreateAcmeError(
                    AcmeErrorResponseService.AcmeErrorTypes.Unauthorized,
                    challengeScopeCheck.FailureReason ?? "No accessible managed challenge matches the requested identifiers for this account");
            }

            var orderId = AcmeHelper.GenerateOrderId();
            var authorizationUrls = new List<string>();
            var authorizationIds = new List<string>();
            var createdAt = DateTime.UtcNow;

            var baseUrl = AcmeHelper.BuildBaseUrl(Request, key);

            // Create authorizations for each identifier
            foreach (var identifier in request.Identifiers)
            {
                var authId = AcmeHelper.GenerateAuthorizationId();

                var authorization = _acmeHelper.CreateAuthorization(identifier, baseUrl);
                authorization.AccountKid = accountKid;

                authorizationIds.Add(authId);
                authorizationUrls.Add(AcmeHelper.BuildAuthorizationUrl(baseUrl, authId));

                await _config.StoreAcmeAuthorization(authId, authorization);
            }

            var order = new AcmeOrder
            {
                Id = orderId,
                Status = OrderStatus.Ready,
                CreatedAt = createdAt,
                Expires = createdAt.Add(AcmeBackgroundTaskService.OrderMaxAge),
                Identifiers = request.Identifiers,
                NotBefore = request.NotBefore,
                NotAfter = request.NotAfter,
                Authorizations = authorizationUrls,
                AuthorizationIds = authorizationIds,
                Finalize = $"{baseUrl}/order/{orderId}/finalize",
                HubInstanceId = _hubInstanceId,
                AccountKid = accountKid
            };

            // Store order first so authorization cleanup works if later steps fail.
            await _config.StoreAcmeOrder(orderId, order);

            // create temp order in hub using a managed challenge, carrying principal scope for fulfillment
            var managedCert = AcmeHelper.PrepareManagedCertificate(
                orderId,
                request,
                accountKid,
                account.SecurityPrincipalId,
                account.ScopedAssignedRoles);

            var tempCert = await _mgmtAPI.UpdateManagedCertificate(_hubInstanceId, managedCert, CurrentAuthContext);
            if (tempCert == null)
            {
                _logger.LogError("Failed to create temporary managed certificate for order {OrderId}", orderId);
                await _config.RemoveAcmeOrder(orderId);
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.ServerInternal, "Failed to create temporary managed certificate");
            }

            order.ManagedCertificateId = tempCert.Id;
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
                await AcmeBackgroundTaskService.CleanupOrderAsync(_config, _mgmtAPI, order, CurrentAuthContext, _logger, _hubInstanceId);
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.ServerInternal, "Failed to process order");
            }


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

            var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}";

            // Decode and verify the JWS payload before touching any order state
            FinalizeOrderRequest request;
            try
            {
                request = await _jwsValidator.DecodeJwsPayload<FinalizeOrderRequest>(payload, requestUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode JWS payload for finalize order request");
                return AcmeErrorResponseService.CreateAcmeErrorForException(ex, "Invalid JWS payload");
            }

            var order = await _config.GetAcmeOrder(orderId);
            if (order == null)
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.OrderNotFound, "Order not found");
            }

            if (!IsOrderOwnedBySigningAccount(order, payload, nameof(FinalizeOrder)))
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Unauthorized, "Order not found");
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

            // RFC 8555 Section 7.4 - the CSR must not request identifiers beyond those on the order, and those
            // identifiers must still be within the account's domain restrictions (which may have been narrowed
            // since the order was created).
            var csrCheck = await ValidateFinalizeCsrIdentifiers(order, request.Csr);
            if (csrCheck != null)
            {
                return csrCheck;
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

            AddRetryAfterHeader(60);
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
                return AcmeErrorResponseService.CreateAcmeErrorForException(ex, "Invalid JWS payload");
            }

            var baseUrl = AcmeHelper.BuildBaseUrl(Request, key);
            var certUri = AcmeHelper.BuildCertificateUrl(baseUrl, certId);
            var order = await _config.GetAcmeOrderByCertificateUri(certUri);
            if (order == null)
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Malformed, "Invalid or unknown certId");
            }

            if (!IsOrderOwnedBySigningAccount(order, payload, nameof(DownloadCertificate)))
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Unauthorized, "Invalid or unknown certId");
            }

            var managedCert = await _mgmtAPI.GetManagedCertificate(_hubInstanceId, order.ManagedCertificateId, CurrentAuthContext);
            var result = await _mgmtAPI.ExportCertificate(_hubInstanceId, order.ManagedCertificateId, "pem_fullchain", strictExport: false, CurrentAuthContext);

            if (result?.Result == null)
            {
                _logger.LogError("Failed to export certificate for order {OrderId}", order.Id);
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.ServerInternal, "Failed to export certificate");
            }

            var certPEM = System.Text.Encoding.UTF8.GetString(result.Result);

            // delete order and temp managed cert after successful export
            order.HubInstanceId ??= _hubInstanceId;
            await AcmeBackgroundTaskService.CleanupOrderAsync(_config, _mgmtAPI, order, CurrentAuthContext, _logger, _hubInstanceId);


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
                return AcmeErrorResponseService.CreateAcmeErrorForException(ex, "Invalid JWS payload");
            }

            var order = await _config.GetAcmeOrder(orderId);

            if (order == null)
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.OrderNotFound, "Order not found");
            }

            if (!IsOrderOwnedBySigningAccount(order, payload, nameof(GetOrder)))
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Unauthorized, "Order not found");
            }


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
                AddRetryAfterHeader(10);
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
                return AcmeErrorResponseService.CreateAcmeErrorForException(ex, "Invalid JWS payload");
            }

            var authorization = await _config.GetAcmeAuthorization(authId);
            if (authorization == null)
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.AuthorizationNotFound, "Authorization not found");
            }

            if (!IsOwnedBySigningAccount(authorization.AccountKid, payload, nameof(GetAuthorization)))
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Unauthorized, "Authorization not found");
            }

            return Ok(authorization);
        }

        /// <summary>
        /// Checks the order was created by the account which signed the current request.
        /// </summary>
        private bool IsOrderOwnedBySigningAccount(AcmeOrder order, JwsPayload payload, string context)
            => IsOwnedBySigningAccount(order?.AccountKid, payload, context);

        /// <summary>
        /// Validate the identifiers requested by a finalization CSR. Returns an ACME error result when the CSR
        /// should be rejected, or null when it is acceptable.
        ///
        /// The CSR must not request any identifier absent from the order (RFC 8555 Section 7.4), otherwise an
        /// account could authorize a permitted name and then have a certificate issued for arbitrary others.
        /// Requesting a subset of the order's identifiers is allowed. The identifiers are then re-checked against
        /// the account's role scope, so restrictions narrowed after the order was created still apply.
        /// </summary>
        private async Task<IActionResult?> ValidateFinalizeCsrIdentifiers(AcmeOrder order, string? csr)
        {
            if (string.IsNullOrWhiteSpace(csr))
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Malformed, "A CSR is required to finalize an order");
            }

            List<string> csrIdentifiers;

            try
            {
                var csrBytes = Certify.Management.Util.FromUrlSafeBase64String(csr);

                csrIdentifiers = CSRUtils.DecodeCsrSubjects(csrBytes)
                    .Where(i => !string.IsNullOrWhiteSpace(i))
                    .Select(i => i.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not decode finalization CSR for order {OrderId}", order.Id);
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.BadCSR, "CSR could not be decoded");
            }

            if (csrIdentifiers.Count == 0)
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.BadCSR, "CSR does not request any identifiers");
            }

            var orderIdentifiers = (order.Identifiers ?? [])
                .Select(i => i?.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var unauthorized = csrIdentifiers.FirstOrDefault(i => !orderIdentifiers.Contains(i));

            if (unauthorized != null)
            {
                _logger.LogWarning(
                    "Finalization rejected for order {OrderId}, CSR requests identifier '{Identifier}' which is not on the order",
                    order.Id,
                    unauthorized);

                return AcmeErrorResponseService.CreateAcmeError(
                    AcmeErrorResponseService.AcmeErrorTypes.BadCSR,
                    $"CSR requests identifier '{unauthorized}' which is not present on this order");
            }

            // re-check role scope, the account's domain restrictions may have been narrowed since the order was placed
            var account = await _config.GetAccount(order.AccountKid);

            if (account == null || account.Status != AccountStatus.Valid)
            {
                return AcmeErrorResponseService.CreateAcmeError(AcmeErrorResponseService.AcmeErrorTypes.Unauthorized, "Account is unknown or not valid");
            }

            var scopeCheck = await _managedChallengeScopeService.AuthorizeIdentifiersForPrincipal(
                account.SecurityPrincipalId,
                csrIdentifiers,
                account.ScopedAssignedRoles,
                StandardResourceActions.ManagedAcmePerformOrder);

            if (!scopeCheck.IsAuthorized)
            {
                _logger.LogWarning(
                    "Finalization rejected for order {OrderId}: {Reason}",
                    order.Id,
                    scopeCheck.FailureReason);

                return AcmeErrorResponseService.CreateAcmeError(
                    AcmeErrorResponseService.AcmeErrorTypes.Unauthorized,
                    scopeCheck.FailureReason ?? "The identifiers requested by this CSR are not permitted for this account");
            }

            return null;
        }

        /// <summary>
        /// Checks the supplied owning account KID matches the account which signed the current request.
        /// Fails closed if ownership was never recorded (resources created before ownership tracking).
        /// </summary>
        private bool IsOwnedBySigningAccount(string? ownerAccountKid, JwsPayload payload, string context)
        {
            string signingAccountKid;

            try
            {
                signingAccountKid = AcmeJwsValidator.GetAccountKidFromJwsPayload(payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not determine signing account for {Context}", context);
                return false;
            }

            if (string.IsNullOrEmpty(ownerAccountKid))
            {
                _logger.LogWarning("Denied {Context}, resource has no recorded owning account", context);
                return false;
            }

            if (!string.Equals(ownerAccountKid, signingAccountKid, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Denied {Context}, signing account does not own the requested resource", context);
                return false;
            }

            return true;
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

