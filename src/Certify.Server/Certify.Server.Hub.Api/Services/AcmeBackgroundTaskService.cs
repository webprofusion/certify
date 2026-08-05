using System.Threading.Channels;
using Certify.Client;
using Certify.Models;
using Certify.Server.Hub.Api.Models.Acme;
using Certify.Server.Hub.Api.SignalR.ManagementHub;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Background service for processing ACME order tasks and sweeping stale orders.
    /// </summary>
    public class AcmeBackgroundTaskService : BackgroundService
    {
        /// <summary>
        /// Orders older than this age are eligible for automatic cleanup.
        /// </summary>
        public static readonly TimeSpan OrderMaxAge = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);

        private readonly ILogger<AcmeBackgroundTaskService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly Channel<AcmeOrderTask> _taskQueue;
        private readonly ChannelWriter<AcmeOrderTask> _writer;
        private readonly ChannelReader<AcmeOrderTask> _reader;

        public AcmeBackgroundTaskService(ILogger<AcmeBackgroundTaskService> logger, IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;

            var options = new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };

            _taskQueue = Channel.CreateBounded<AcmeOrderTask>(options);
            _writer = _taskQueue.Writer;
            _reader = _taskQueue.Reader;
        }

        /// <summary>
        /// Enqueue a new ACME order task for background processing
        /// </summary>
        public Task<bool> EnqueueOrderProcessingTask(string orderId, string managedCertificateId, AuthContext authContext, string hubInstanceId)
        {
            var task = new AcmeOrderTask
            {
                Type = AcmeTaskType.ProcessOrder,
                OrderId = orderId,
                ManagedCertificateId = managedCertificateId,
                AuthContext = authContext,
                HubInstanceId = hubInstanceId,
                CreatedAt = DateTime.UtcNow
            };

            return Task.FromResult(_writer.TryWrite(task));
        }

        /// <summary>
        /// Enqueue a new ACME order finalization task for background processing
        /// </summary>
        public Task<bool> EnqueueOrderFinalizationTask(string orderId, string csr, string baseUrl, AuthContext authContext, string hubInstanceId)
        {
            var task = new AcmeOrderTask
            {
                Type = AcmeTaskType.FinalizeOrder,
                OrderId = orderId,
                Csr = csr,
                BaseUrl = baseUrl,
                AuthContext = authContext,
                HubInstanceId = hubInstanceId,
                CreatedAt = DateTime.UtcNow
            };

            return Task.FromResult(_writer.TryWrite(task));
        }

        /// <summary>
        /// Removes an ACME order, its authorizations, and any associated temporary managed certificate.
        /// </summary>
        public static async Task CleanupOrderAsync(
            AcmeServerConfig configService,
            ManagementAPI mgmtApi,
            AcmeOrder order,
            AuthContext? authContext,
            ILogger logger,
            string? hubInstanceIdFallback = null)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.Id))
            {
                return;
            }

            var hubInstanceId = !string.IsNullOrWhiteSpace(order.HubInstanceId)
                ? order.HubInstanceId
                : hubInstanceIdFallback;

            if (!string.IsNullOrWhiteSpace(order.ManagedCertificateId) && !string.IsNullOrWhiteSpace(hubInstanceId))
            {
                try
                {
                    var result = await mgmtApi.RemoveManagedCertificate(hubInstanceId, order.ManagedCertificateId, authContext);
                    if (!result.IsSuccess)
                    {
                        logger.LogWarning("Failed to remove temporary managed certificate {ManagedCertificateId} for ACME order {OrderId}: {Message}", order.ManagedCertificateId, order.Id, result.Message);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to remove temporary managed certificate {ManagedCertificateId} for ACME order {OrderId}", order.ManagedCertificateId, order.Id);
                    return;
                }
            }

            await configService.RemoveAcmeOrder(order.Id);
            logger.LogInformation("Cleaned up ACME order {OrderId}", order.Id);
        }

        /// <summary>
        /// Executes queue processing and periodic stale-order cleanup until cancelled.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ACME Background Task Service is starting");

            var queueTask = ProcessQueueAsync(stoppingToken);
            var cleanupTask = RunCleanupLoopAsync(stoppingToken);

            await Task.WhenAll(queueTask, cleanupTask);
        }

        private async Task ProcessQueueAsync(CancellationToken stoppingToken)
        {
            await foreach (var task in _reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessTask(task, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing ACME task {TaskType} for order {OrderId}", task.Type, task.OrderId);
                }
            }
        }

        private async Task RunCleanupLoopAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(CleanupInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await CleanupStaleOrdersAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // expected on shutdown
            }
        }

        private async Task CleanupStaleOrdersAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<AcmeServerConfig>();
            var mgmtApi = scope.ServiceProvider.GetRequiredService<ManagementAPI>();
            var stateProvider = scope.ServiceProvider.GetService<IInstanceManagementStateProvider>();

            var hubInstanceIdFallback = stateProvider?.GetManagementHubInstanceId();
            var staleOrders = configService.GetStaleAcmeOrders(OrderMaxAge);

            if (staleOrders.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Cleaning up {Count} stale ACME order(s) older than {MaxAge}", staleOrders.Count, OrderMaxAge);

            foreach (var order in staleOrders)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await CleanupOrderAsync(configService, mgmtApi, order, authContext: null, _logger, hubInstanceIdFallback);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed stale cleanup for ACME order {OrderId}", order.Id);
                }
            }
        }

        private async Task ProcessTask(AcmeOrderTask task, CancellationToken cancellationToken)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<AcmeServerConfig>();
            var mgmtApi = scope.ServiceProvider.GetRequiredService<ManagementAPI>();

            switch (task.Type)
            {
                case AcmeTaskType.ProcessOrder:
                    await ProcessOrderTask(task, configService, mgmtApi, cancellationToken);
                    break;
                case AcmeTaskType.FinalizeOrder:
                    await ProcessFinalizationTask(task, configService, mgmtApi, cancellationToken);
                    break;
            }
        }

        private async Task ProcessOrderTask(AcmeOrderTask task, AcmeServerConfig configService, ManagementAPI mgmtApi, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Processing ACME order {OrderId}", task.OrderId);

            try
            {
                await mgmtApi.PerformManagedCertificateRequest(task.HubInstanceId, task.ManagedCertificateId, task.AuthContext);

                var itemStatus = await mgmtApi.GetManagedCertificate(task.HubInstanceId, task.ManagedCertificateId, task.AuthContext);
                var orderDetails = await configService.GetAcmeOrder(task.OrderId);

                if (orderDetails == null)
                {
                    _logger.LogWarning("ACME order {OrderId} no longer exists after processing", task.OrderId);
                    return;
                }

                // Ensure cleanup metadata is retained even if the order was stored before these fields existed.
                orderDetails.ManagedCertificateId ??= task.ManagedCertificateId;
                orderDetails.HubInstanceId ??= task.HubInstanceId;

                if (itemStatus?.LastRenewalStatus == RequestState.Paused)
                {
                    orderDetails.Status = OrderStatus.ReadyForInternalFinalization;
                    await configService.StoreAcmeOrder(task.OrderId, orderDetails);
                    _logger.LogInformation("ACME order {OrderId} is ready for finalization", task.OrderId);
                    return;
                }

                _logger.LogWarning("ACME order {OrderId} failed during processing", task.OrderId);
                await CleanupOrderAsync(configService, mgmtApi, orderDetails, task.AuthContext, _logger, task.HubInstanceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process ACME order {OrderId}", task.OrderId);

                var orderDetails = await configService.GetAcmeOrder(task.OrderId);
                if (orderDetails != null)
                {
                    orderDetails.ManagedCertificateId ??= task.ManagedCertificateId;
                    orderDetails.HubInstanceId ??= task.HubInstanceId;
                    await CleanupOrderAsync(configService, mgmtApi, orderDetails, task.AuthContext, _logger, task.HubInstanceId);
                }
                else if (!string.IsNullOrWhiteSpace(task.ManagedCertificateId))
                {
                    try
                    {
                        await mgmtApi.RemoveManagedCertificate(task.HubInstanceId, task.ManagedCertificateId, task.AuthContext);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning(cleanupEx, "Failed to remove temporary managed certificate {ManagedCertificateId} after missing order {OrderId}", task.ManagedCertificateId, task.OrderId);
                    }
                }
            }
        }

        private async Task ProcessFinalizationTask(AcmeOrderTask task, AcmeServerConfig configService, ManagementAPI mgmtApi, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Processing ACME order finalization {OrderId}", task.OrderId);

            AcmeOrder? updatedOrder = null;

            try
            {
                var maxWaitTime = TimeSpan.FromMinutes(5);
                var startTime = DateTime.UtcNow;

                updatedOrder = await configService.GetAcmeOrder(task.OrderId);
                if (updatedOrder == null)
                {
                    _logger.LogError("Order {OrderId} not found for finalization", task.OrderId);
                    return;
                }

                updatedOrder.HubInstanceId ??= task.HubInstanceId;

                // Wait for order to be ready for finalization
                while (updatedOrder.Status != OrderStatus.ReadyForInternalFinalization &&
                       DateTime.UtcNow - startTime < maxWaitTime &&
                       !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, cancellationToken);
                    updatedOrder = await configService.GetAcmeOrder(task.OrderId);
                    if (updatedOrder == null)
                    {
                        _logger.LogError("Order {OrderId} disappeared while waiting for finalization", task.OrderId);
                        return;
                    }

                    updatedOrder.HubInstanceId ??= task.HubInstanceId;
                }

                if (updatedOrder.Status != OrderStatus.ReadyForInternalFinalization)
                {
                    _logger.LogError("Order {OrderId} not ready for finalization after timeout", task.OrderId);
                    await CleanupOrderAsync(configService, mgmtApi, updatedOrder, task.AuthContext, _logger, task.HubInstanceId);
                    return;
                }

                updatedOrder.Status = OrderStatus.InternalFinalizationInProgress;
                await configService.StoreAcmeOrder(task.OrderId, updatedOrder);

                var managedCertId = !string.IsNullOrWhiteSpace(updatedOrder.ManagedCertificateId)
                    ? updatedOrder.ManagedCertificateId
                    : task.ManagedCertificateId;

                if (string.IsNullOrWhiteSpace(managedCertId))
                {
                    _logger.LogError("Order {OrderId} has no managed certificate id for finalization", task.OrderId);
                    await CleanupOrderAsync(configService, mgmtApi, updatedOrder, task.AuthContext, _logger, task.HubInstanceId);
                    return;
                }

                var hubInstanceId = !string.IsNullOrWhiteSpace(updatedOrder.HubInstanceId)
                    ? updatedOrder.HubInstanceId
                    : task.HubInstanceId;

                var managedCert = await mgmtApi.GetManagedCertificate(hubInstanceId, managedCertId, task.AuthContext);
                if (managedCert == null)
                {
                    _logger.LogError("Managed certificate {ManagedCertificateId} not found for order {OrderId}", managedCertId, task.OrderId);
                    await CleanupOrderAsync(configService, mgmtApi, updatedOrder, task.AuthContext, _logger, hubInstanceId);
                    return;
                }

                managedCert.RequestConfig.CustomCSR = FormatCsrPem(task.Csr);
                await mgmtApi.UpdateManagedCertificate(hubInstanceId, managedCert, task.AuthContext);

                await mgmtApi.PerformManagedCertificateRequest(hubInstanceId, managedCertId, task.AuthContext);

                managedCert = await mgmtApi.GetManagedCertificate(hubInstanceId, managedCertId, task.AuthContext);

                if (managedCert?.LastRenewalStatus == RequestState.Success)
                {
                    var certId = Guid.NewGuid().ToString("N");
                    updatedOrder.Certificate = $"{task.BaseUrl}/cert/{certId}";
                    updatedOrder.Status = OrderStatus.Valid;
                    updatedOrder.ManagedCertificateId = managedCertId;
                    updatedOrder.HubInstanceId = hubInstanceId;

                    await configService.StoreAcmeOrder(task.OrderId, updatedOrder);

                    _logger.LogInformation("ACME order {OrderId} finalization completed successfully", task.OrderId);
                }
                else
                {
                    _logger.LogWarning("ACME order {OrderId} finalization failed.", task.OrderId);
                    updatedOrder.ManagedCertificateId = managedCertId;
                    updatedOrder.HubInstanceId = hubInstanceId;
                    await CleanupOrderAsync(configService, mgmtApi, updatedOrder, task.AuthContext, _logger, hubInstanceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to finalize ACME order {OrderId}", task.OrderId);

                updatedOrder ??= await configService.GetAcmeOrder(task.OrderId);
                if (updatedOrder != null)
                {
                    updatedOrder.ManagedCertificateId ??= task.ManagedCertificateId;
                    updatedOrder.HubInstanceId ??= task.HubInstanceId;
                    await CleanupOrderAsync(configService, mgmtApi, updatedOrder, task.AuthContext, _logger, task.HubInstanceId);
                }
                else if (!string.IsNullOrWhiteSpace(task.ManagedCertificateId))
                {
                    try
                    {
                        await mgmtApi.RemoveManagedCertificate(task.HubInstanceId, task.ManagedCertificateId, task.AuthContext);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning(cleanupEx, "Failed to remove temporary managed certificate {ManagedCertificateId} after finalization error for order {OrderId}", task.ManagedCertificateId, task.OrderId);
                    }
                }
            }
        }

        private static string FormatCsrPem(string csr)
        {
            return $"-----BEGIN CERTIFICATE REQUEST-----\n{Convert.ToBase64String(Certify.Management.Util.FromUrlSafeBase64String(csr), Base64FormattingOptions.InsertLineBreaks)}\n-----END CERTIFICATE REQUEST-----";
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ACME Background Task Service is stopping");
            _writer.Complete();
            await base.StopAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Represents a task to be processed by the ACME background service
    /// </summary>
    public class AcmeOrderTask
    {
        /// <summary>
        /// Gets or sets the type of ACME task to be processed.
        /// </summary>
        public AcmeTaskType Type { get; set; }
        /// <summary>
        /// Gets or sets the unique identifier for the ACME order.
        /// </summary>
        public string OrderId { get; set; } = string.Empty;
        public string ManagedCertificateId { get; set; } = string.Empty;
        public string Csr { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public AuthContext AuthContext { get; set; } = default!;
        public string HubInstanceId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Specifies the type of ACME task to be processed by the background service.
    /// </summary>
    public enum AcmeTaskType
    {
        ProcessOrder,
        FinalizeOrder
    }
}
