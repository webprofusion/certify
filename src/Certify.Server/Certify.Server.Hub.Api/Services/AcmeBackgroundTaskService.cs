using System.Threading.Channels;
using Certify.Client;
using Certify.Models;
using Certify.Server.Hub.Api.Models.Acme;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Background service for processing ACME order tasks
    /// </summary>
    public class AcmeBackgroundTaskService : BackgroundService
    {
        private readonly ILogger<AcmeBackgroundTaskService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly Channel<AcmeOrderTask> _taskQueue;
        private readonly ChannelWriter<AcmeOrderTask> _writer;
        private readonly ChannelReader<AcmeOrderTask> _reader;

        public AcmeBackgroundTaskService(ILogger<AcmeBackgroundTaskService> logger, IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;

            // Create unbounded channel for task queue
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
        public async Task<bool> EnqueueOrderProcessingTask(string orderId, string managedCertificateId, AuthContext authContext, string hubInstanceId)
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

            return _writer.TryWrite(task);
        }

        /// <summary>
        /// Enqueue a new ACME order finalization task for background processing
        /// </summary>
        public async Task<bool> EnqueueOrderFinalizationTask(string orderId, string csr, string baseUrl, AuthContext authContext, string hubInstanceId)
        {
            var task = new AcmeOrderTask
            {
                Type = AcmeTaskType.FinalizeOrder,
                OrderId = orderId,
                Csr = csr,
                AuthContext = authContext,
                HubInstanceId = hubInstanceId,
                BaseUrl = baseUrl,
                CreatedAt = DateTime.UtcNow
            };

            return _writer.TryWrite(task);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ACME Background Task Service is starting");

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
                // Start the order processing
                await mgmtApi.PerformManagedCertificateRequest(task.HubInstanceId, task.ManagedCertificateId, task.AuthContext);

                // Check the status
                var itemStatus = await mgmtApi.GetManagedCertificate(task.HubInstanceId, task.ManagedCertificateId, task.AuthContext);
                var orderDetails = await configService.GetAcmeOrder(task.OrderId);

                if (orderDetails != null)
                {
                    if (itemStatus?.LastRenewalStatus == RequestState.Paused)
                    {
                        // Ready for finalization
                        orderDetails.Status = OrderStatus.ReadyForInternalFinalization;
                        _logger.LogInformation("ACME order {OrderId} is ready for finalization", task.OrderId);
                    }
                    else
                    {
                        orderDetails.Status = OrderStatus.Invalid;
                        _logger.LogWarning("ACME order {OrderId} failed during processing", task.OrderId);
                    }

                    await configService.StoreAcmeOrder(task.OrderId, orderDetails);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process ACME order {OrderId}", task.OrderId);

                // Mark order as failed
                var orderDetails = await configService.GetAcmeOrder(task.OrderId);
                if (orderDetails != null)
                {
                    orderDetails.Status = OrderStatus.Invalid;
                    await configService.StoreAcmeOrder(task.OrderId, orderDetails);
                }
            }
        }

        private async Task ProcessFinalizationTask(AcmeOrderTask task, AcmeServerConfig configService, ManagementAPI mgmtApi, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Processing ACME order finalization {OrderId}", task.OrderId);

            try
            {
                var maxWaitTime = TimeSpan.FromMinutes(5);
                var startTime = DateTime.UtcNow;

                var updatedOrder = await configService.GetAcmeOrder(task.OrderId);
                if (updatedOrder == null)
                {
                    _logger.LogError("Order {OrderId} not found for finalization", task.OrderId);
                    return;
                }

                // Wait for order to be ready for finalization
                while (updatedOrder.Status != OrderStatus.ReadyForInternalFinalization &&
                       DateTime.UtcNow - startTime < maxWaitTime &&
                       !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, cancellationToken);
                    updatedOrder = await configService.GetAcmeOrder(task.OrderId);
                }

                if (updatedOrder.Status != OrderStatus.ReadyForInternalFinalization)
                {
                    _logger.LogError("Order {OrderId} not ready for finalization after timeout", task.OrderId);
                    updatedOrder.Status = OrderStatus.Invalid;
                    await configService.StoreAcmeOrder(task.OrderId, updatedOrder);
                    return;
                }

                // Mark as in progress
                updatedOrder.Status = OrderStatus.InternalFinalizationInProgress;
                await configService.StoreAcmeOrder(task.OrderId, updatedOrder);

                // Apply CSR and finalize
                var managedCert = await mgmtApi.GetManagedCertificate(task.HubInstanceId, updatedOrder.ManagedCertificateId, task.AuthContext);
                managedCert.RequestConfig.CustomCSR = FormatCsrPem(task.Csr);
                await mgmtApi.UpdateManagedCertificate(task.HubInstanceId, managedCert, task.AuthContext);

                // Resume/finalize cert order
                await mgmtApi.PerformManagedCertificateRequest(task.HubInstanceId, updatedOrder.ManagedCertificateId, task.AuthContext);

                // Update order status
                var certId = Guid.NewGuid().ToString("N");

                updatedOrder.Certificate = $"{task.BaseUrl}/cert/{certId}";
                updatedOrder.Status = OrderStatus.Valid;

                await configService.StoreAcmeOrder(task.OrderId, updatedOrder);

                _logger.LogInformation("ACME order {OrderId} finalization completed successfully", task.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to finalize ACME order {OrderId}", task.OrderId);

                // Mark order as failed
                var orderDetails = await configService.GetAcmeOrder(task.OrderId);
                if (orderDetails != null)
                {
                    orderDetails.Status = OrderStatus.Invalid;
                    await configService.StoreAcmeOrder(task.OrderId, orderDetails);
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
        public AcmeTaskType Type { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public string ManagedCertificateId { get; set; } = string.Empty;
        public string Csr { get; set; } = string.Empty;
        public AuthContext AuthContext { get; set; } = default!;
        public string HubInstanceId { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public enum AcmeTaskType
    {
        ProcessOrder,
        FinalizeOrder
    }
}
