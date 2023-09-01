using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Certify.Config;
using Certify.Management;
using Certify.Models;
using Certify.Models.API;
using Certify.Models.Config;
using Certify.Models.Utils;
using Certify.Models.Reporting;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace Certify.Service.Controllers
{
    [ApiController]
    [Microsoft.AspNetCore.Cors.EnableCors()]
    [Route("api/managedcertificates")]
    public class ManagedCertificatesController : ControllerBase
    {
        private ICertifyManager _certifyManager;

        public ManagedCertificatesController(ICertifyManager manager)
        {
            _certifyManager = manager;
        }

        // Get List of Top N Managed Certificates, filtered by title
        [HttpPost, Route("search")]
        public async Task<List<ManagedCertificate>> Search(ManagedCertificateFilter filter)
        {
            DebugLog();

            return await _certifyManager.GetManagedCertificates(filter);
        }

        // Get List of Top N Managed Certificates, filtered by title, as a Search Result with total count
        [HttpPost, Route("results")]
        public async Task<ManagedCertificateSearchResult> GetResults(ManagedCertificateFilter filter)
        {
            DebugLog();
            return await _certifyManager.GetManagedCertificateResults(filter);
        }

        [HttpPost, Route("summary")]
        public async Task<Summary> GetSummary(ManagedCertificateFilter filter)
        {
            DebugLog();
            return await _certifyManager.GetManagedCertificateSummary(filter);
        }

        [HttpGet, Route("{id}")]
        public async Task<ManagedCertificate> GetById(string id)
        {
            DebugLog(id);

            return await _certifyManager.GetManagedCertificate(id);
        }

        //add or update managed site
        [HttpPost, Route("update")]
        public async Task<ManagedCertificate> Update(ManagedCertificate site)
        {
            DebugLog();

            return await _certifyManager.UpdateManagedCertificate(site);
        }

        [HttpDelete, Route("delete/{managedItemId}")]
        public async Task<bool> Delete(string managedItemId)
        {
            DebugLog();

            await _certifyManager.DeleteManagedCertificate(managedItemId);

            return true;
        }

        [HttpPost, Route("testconfig")]
        public async Task<List<StatusMessage>> TestChallengeResponse(ManagedCertificate managedCertificate)
        {
            DebugLog();

            var progressState = new RequestProgressState(RequestState.Running, "Starting Tests..", managedCertificate);

            var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);

            // perform challenge response test, log to string list and return in result
            var logList = new List<string>();
            using (var log = new LoggerConfiguration()

                     .WriteTo.Sink(new ProgressLogSink(progressIndicator, managedCertificate, _certifyManager))
                     .CreateLogger())
            {
                var theLog = new Loggy(log);
                var results = await _certifyManager.TestChallenge(theLog, managedCertificate, isPreviewMode: true, progress: progressIndicator);

                return results;
            }
        }

        [HttpPost, Route("challengecleanup")]
        public async Task<List<StatusMessage>> PerformChallengeCleanup(ManagedCertificate managedCertificate)
        {
            DebugLog();

            var progressState = new RequestProgressState(RequestState.Running, "Performing Challenge Cleanup..", managedCertificate);

            var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);

            // perform challenge response test, log to string list and return in result
            var logList = new List<string>();
            using (var log = new LoggerConfiguration()

                     .WriteTo.Sink(new ProgressLogSink(progressIndicator, managedCertificate, _certifyManager))
                     .CreateLogger())
            {
                var theLog = new Loggy(log);
                var results = await _certifyManager.PerformChallengeCleanup(theLog, managedCertificate, progress: progressIndicator);

                return results;
            }
        }

        [HttpPost, Route("preview")]
        public async Task<List<ActionStep>> PreviewActions(ManagedCertificate site)
        {
            DebugLog();

            return await _certifyManager.GeneratePreview(site);
        }

        [HttpGet, Route("performdeployment/{isPreviewOnly}/{managedCertificateId}/{taskId?}")]
        public async Task<List<ActionStep>> PerformDeploymentTasks(string managedCertificateId, bool isPreviewOnly, string taskId)
        {
            DebugLog();

            // perform deployment task for this managed certificate including deferred items
            return await _certifyManager.PerformDeploymentTask(null, managedCertificateId, taskId, isPreviewOnly, skipDeferredTasks: false, forceTaskExecution: false);
        }

        [HttpGet, Route("performforceddeployment/{isPreviewOnly}/{managedCertificateId}/{taskId?}")]
        public async Task<List<ActionStep>> PerformForcedDeploymentTasks(string managedCertificateId, bool isPreviewOnly, string taskId)
        {
            DebugLog();

            // perform deployment task for this managed certificate including deferred items
            return await _certifyManager.PerformDeploymentTask(null, managedCertificateId, taskId, isPreviewOnly, skipDeferredTasks: false, forceTaskExecution: true);
        }

        [HttpGet, Route("performdeployment/{isPreviewOnly}/{managedCertificateId}")]
        public async Task<List<ActionStep>> PerformDeploymentTask(string managedCertificateId, bool isPreviewOnly)
        {
            DebugLog();

            // perform all deployment tasks for this managed certificate including deferred items
            return await _certifyManager.PerformDeploymentTask(null, managedCertificateId, null, isPreviewOnly, skipDeferredTasks: false, forceTaskExecution: false);
        }

        [HttpPost, Route("validatedeploymenttask")]
        public async Task<List<Models.Config.ActionResult>> ValidateDeploymentTask(DeploymentTaskValidationInfo info)
        {
            DebugLog();
            return await _certifyManager.ValidateDeploymentTask(info.ManagedCertificate, info.TaskConfig);
        }

        /// <summary>
        /// Begin auto renew process and return list of included sites 
        /// </summary>
        /// <returns></returns>
        [HttpPost, Route("autorenew")]
        public async Task<List<CertificateRequestResult>> BeginAutoRenewal(RenewalSettings settings)
        {
            DebugLog();

            if (settings.AwaitResults)
            {
                return await _certifyManager.PerformRenewAll(settings);
            }
            else
            {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                _certifyManager.PerformRenewAll(settings);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                return await Task.FromResult(new List<CertificateRequestResult>());
            }
        }

        [HttpGet, Route("renewcert/{managedItemId}/{resumePaused}/{isInteractive}")]
        public async Task<CertificateRequestResult> BeginCertificateRequest(string managedItemId, bool resumePaused, bool isInteractive)
        {
            DebugLog();

            var managedCertificate = await _certifyManager.GetManagedCertificate(managedItemId);

            var progressState = new RequestProgressState(RequestState.Running, "Starting..", managedCertificate);

            var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);

            //begin request
            var result = await _certifyManager.PerformCertificateRequest(
                                                                           null,
                                                                            managedCertificate,
                                                                            progressIndicator,
                                                                            resumePaused,
                                                                            isInteractive: isInteractive
                                                                            );
            return result;
        }

        [HttpGet, Route("log/{managedItemId}/{limit}")]
        public async Task<LogItem[]> GetLog(string managedItemId, int limit)
        {
            DebugLog();
            return await _certifyManager.GetItemLog(managedItemId, limit);
        }

        [HttpGet, Route("revoke/{managedItemId}")]
        public async Task<StatusMessage> RevokeCertificate(string managedItemId)
        {
            DebugLog();

            var managedCertificate = await _certifyManager.GetManagedCertificate(managedItemId);
            var result = await _certifyManager.RevokeCertificate(
                  null,
                  managedCertificate
                  );
            return result;
        }

        [HttpGet, Route("reapply/{managedItemId}/{isPreviewOnly}/{includeDeploymentTasks}")]
        public async Task<CertificateRequestResult> RedeployCertificate(string managedItemId, bool isPreviewOnly, bool includeDeploymentTasks)
        {
            DebugLog();

            var filter = new ManagedCertificateFilter { };

            if (managedItemId != null)
            {
                filter.Id = managedItemId;

            }

            var result = await _certifyManager.RedeployManagedCertificates(filter, null, isPreviewOnly, includeDeploymentTasks);
            return result.FirstOrDefault();
        }

        private bool _redeployInProgress = false;
        [HttpGet, Route("redeploy/{isPreviewOnly}/{includeDeploymentTasks}")]
        public async Task<List<CertificateRequestResult>> RedeployManagedCertificate(bool isPreviewOnly, bool includeDeploymentTasks)
        {
            DebugLog();

            if (!_redeployInProgress)
            {
                var filter = new ManagedCertificateFilter { };

                _redeployInProgress = true;

                try
                {
                    var results = await _certifyManager.RedeployManagedCertificates(filter, null, isPreviewOnly, includeDeploymentTasks);

                    return results;
                }
                catch (Exception)
                {
                    return new List<CertificateRequestResult>();
                }
                finally
                {
                    _redeployInProgress = false;

                }
            }
            else
            {
                return new List<CertificateRequestResult>();
            }
        }

        [HttpGet, Route("deploymentproviders/")]
        public async Task<List<DeploymentProviderDefinition>> GetDeploymentProviderList() => await _certifyManager.GetDeploymentProviders();

        [HttpPost, Route("deploymentprovider/{id}")]
        public async Task<DeploymentProviderDefinition> GetDeploymentProviderDefinition(string id, DeploymentTaskConfig config)
        {
            return await _certifyManager.GetDeploymentProviderDefinition(id, config);
        }

        [HttpGet, Route("challengeapis/")]
        public async Task<List<ChallengeProviderDefinition>> GetChallengeAPIList() => await Core.Management.Challenges.ChallengeProviders.GetChallengeAPIProviders();

        [HttpGet, Route("currentchallenges/")]
        public async Task<List<SimpleAuthorizationChallengeItem>> GetCurrentChallenges() => await _certifyManager.GetCurrentChallengeResponses(SupportedChallengeTypes.CHALLENGE_TYPE_HTTP);

        [HttpGet, Route("currentchallenges/{type}/{key?}")]
        public async Task<List<SimpleAuthorizationChallengeItem>> GetCurrentChallenges(string type, string key)
        {
            return await _certifyManager.GetCurrentChallengeResponses(type, key);
        }

        [HttpGet, Route("dnszones/{providerTypeId}/{credentialId}")]
        public async Task<List<Models.Providers.DnsZone>> GetDnsProviderZones(string providerTypeId, string credentialId) => await _certifyManager.GetDnsProviderZones(providerTypeId, credentialId);

        [HttpGet, Route("maintenance/{id?}")]
        public async Task<List<Models.Config.ActionResult>> PerformCertMaintenanceTasks(string id = null)
        {
            DebugLog();

            return await _certifyManager.PerformCertificateMaintenanceTasks(id);
        }

        internal class ProgressLogSink : Serilog.Core.ILogEventSink
        {
            private IProgress<RequestProgressState> _progress;
            private ManagedCertificate _item;
            private ICertifyManager _certifyManager;

            public ProgressLogSink(IProgress<RequestProgressState> progress, ManagedCertificate item, ICertifyManager certifyManager)
            {
                _progress = progress;
                _item = item;
                _certifyManager = certifyManager;
            }

            public void Emit(Serilog.Events.LogEvent logEvent)
            {
                var message = logEvent.RenderMessage();

                _certifyManager.ReportProgress(_progress,
                    new RequestProgressState(RequestState.Running, message, _item, true),
                    logThisEvent: false
                    );
            }
        }
    }
}
