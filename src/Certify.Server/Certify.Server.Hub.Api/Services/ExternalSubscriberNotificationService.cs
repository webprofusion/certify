using Certify.Management;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.SignalR;

namespace Certify.Server.Hub.Api.Services
{
    public class ExternalSubscriberNotificationService
    {
        private readonly ICertifyManager _certifyManager;
        private readonly IInstanceManagementStateProvider _hubStateProvider;
        private readonly IHubContext<InstanceManagementHub, IInstanceManagementHub> _instanceManagementHubContext;
        private readonly ILogger<ExternalSubscriberNotificationService> _logger;

        public ExternalSubscriberNotificationService(
            ICertifyManager certifyManager,
            IInstanceManagementStateProvider hubStateProvider,
            IHubContext<InstanceManagementHub, IInstanceManagementHub> instanceManagementHubContext,
            ILogger<ExternalSubscriberNotificationService> logger)
        {
            _certifyManager = certifyManager;
            _hubStateProvider = hubStateProvider;
            _instanceManagementHubContext = instanceManagementHubContext;
            _logger = logger;
        }

        public bool HasManagedCertificateVersionChanged(ManagedCertificate? previousManagedCertificate, ManagedCertificate updatedManagedCertificate)
            => InstanceManagementHub.HasManagedCertificateVersionChanged(previousManagedCertificate, updatedManagedCertificate);

        public async Task NotifyExternalSubscribersOfManagedItemUpdateAsync(ManagedCertificate updatedManagedCertificate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(updatedManagedCertificate.InstanceId) || string.IsNullOrWhiteSpace(updatedManagedCertificate.Id))
                {
                    return;
                }

                var sourceVersion = updatedManagedCertificate.DateRenewed?.UtcDateTime.Ticks.ToString();
                var targets = InstanceManagementHub.GetExternalPushSubscriptionTargets(
                    updatedManagedCertificate.InstanceId,
                    updatedManagedCertificate,
                    _hubStateProvider.GetManagedInstanceItems());

                foreach (var target in targets)
                {
                    var payload = new SubscriptionUpdate
                    {
                        ManagedCertificateId = target.TargetManagedCertificateId,
                        SourceVersion = sourceVersion
                    };

                    var command = new InstanceCommandRequest(ManagementHubCommands.PushSubscriptionUpdate)
                    {
                        Value = System.Text.Json.JsonSerializer.Serialize(payload)
                    };

                    try
                    {
                        if (target.TargetInstanceId == _hubStateProvider.GetManagementHubInstanceId())
                        {
                            await _certifyManager.PerformHubCommandWithResult(command);
                        }
                        else
                        {
                            var connectionId = _hubStateProvider.GetConnectionIdForInstance(target.TargetInstanceId);
                            if (string.IsNullOrWhiteSpace(connectionId))
                            {
                                _logger.LogWarning("Failed to queue external certificate push update for target {targetInstanceId} item {targetItemId}; no active connection exists.", target.TargetInstanceId, target.TargetManagedCertificateId);
                                continue;
                            }

                            await _instanceManagementHubContext.Clients.Client(connectionId).SendCommandRequest(command);
                        }

                        _logger.LogInformation("Queued external certificate push update for target {targetInstanceId} item {targetItemId} from source {sourceInstanceId}/{sourceItemId}.", target.TargetInstanceId, target.TargetManagedCertificateId, updatedManagedCertificate.InstanceId, updatedManagedCertificate.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to queue external certificate push update for target {targetInstanceId} item {targetItemId} from source {sourceInstanceId}/{sourceItemId}.", target.TargetInstanceId, target.TargetManagedCertificateId, updatedManagedCertificate.InstanceId, updatedManagedCertificate.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NotifyExternalSubscribersOfManagedItemUpdateAsync failed for local managed item {sourceItemId}.", updatedManagedCertificate.Id);
            }
        }
    }
}
