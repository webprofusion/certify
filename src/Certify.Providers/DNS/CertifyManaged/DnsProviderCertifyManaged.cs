using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Models.Util;
using Certify.Plugins;
using Certify.SharedUtils;
using Newtonsoft.Json;

/// <summary>
/// Certify Managed Challenge for DNS. Uses the Certify Management Hub API to perform pre-configured DNS challenges.
/// </summary>
namespace Certify.Providers.DNS.CertifyManaged
{
    public class DnsProviderCertifyManagedProvider : PluginProviderBase<IDnsProvider, ChallengeProviderDefinition>, IDnsProviderProviderPlugin { }

    public class DnsProviderCertifyManaged : IDnsProvider, IDisposable
    {
        private static readonly TimeSpan ManagedChallengeApiTimeout = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan ManagedChallengeOperationPollInterval = TimeSpan.FromSeconds(5);

        public static ChallengeProviderDefinition Definition
        {
            get
            {
                return new ChallengeProviderDefinition
                {
                    Id = "DNS01.API.CertifyManaged",
                    Title = "Certify Managed Challenge API",
                    Description = "Performs challenge responses via the Certify Management Hub API.",
                    HelpUrl = "https://docs.certifytheweb.com/",
                    PropagationDelaySeconds = 60,
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{ Key="api",Name="Management Hub API Url", IsRequired=false, IsCredential=false, IsPassword=false, Description="(leave blank to use current management hub API)" },
                        new ProviderParameter{ Key="authkey",Name="Client ID", IsRequired=true, IsCredential=true, IsPassword=false,  Description="API Auth Key" },
                        new ProviderParameter{ Key="authsecret",Name="Client Secret", IsRequired=true, IsCredential=true, IsPassword=true,  Description="API Auth Secret" }
                    },
                    IsTestModeSupported = false,
                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.CertifyManaged",
                    HandlerType = ChallengeHandlerType.INTERNAL
                };
            }
        }

        public DnsProviderCertifyManaged() : base()
        {
        }

        private Dictionary<string, string> _credentials;

        private ILog _log;

        private int? _customPropagationDelay = null;

        public int PropagationDelaySeconds => (_customPropagationDelay != null ? (int)_customPropagationDelay : Definition.PropagationDelaySeconds);

        public string ProviderId => Definition.Id;

        public string ProviderTitle => Definition.Title;

        public string ProviderDescription => Definition.Description;

        public string ProviderHelpUrl => Definition.HelpUrl;

        public bool IsTestModeSupported => Definition.IsTestModeSupported;

        public List<ProviderParameter> ProviderParameters => Definition.ProviderParameters;

        private HttpClient _client;

        private Dictionary<string, string> _parameters = new Dictionary<string, string>();

        private JsonSerializerSettings _serializerSettings;

        private Uri _apiBaseUri { get; set; }

        private string _hubAssignedInstanceId;

        public async Task<ActionResult> Test()
        {
            // TODO: dummy request to test API connection
            return await Task.FromResult(new ActionResult { IsSuccess = true, Message = "Test completed, but no zones returned." });
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            if (string.IsNullOrWhiteSpace(_apiBaseUri?.ToString()))
            {
                return new ActionResult { IsSuccess = false, Message = "Managed Challenge API URL not specified and default Management Hub URI not set. Cannot perform managed challenge." };
            }

            var update = CreateManagedChallengeRequest(request);
            var asyncApiUri = new Uri(_apiBaseUri, "/api/v1/managedchallenge/requestbegin");

            using (var asyncRequest = CreateApiRequest(asyncApiUri, update))
            {
                HttpResponseMessage result;
                try
                {
                    result = await _client.SendAsync(asyncRequest);
                }
                catch (TaskCanceledException exp)
                {
                    return CreateTimeoutFailureResult("Update", exp);
                }
                catch (HttpRequestException exp)
                {
                    return CreateTransportFailureResult("Update", exp);
                }

                using (result)
                {
                    if (ShouldFallbackToSync(result.StatusCode))
                    {
                        var syncApiUri = new Uri(_apiBaseUri, "/api/v1/managedchallenge/request");
                        return await SendManagedChallengeRequest(update, syncApiUri, "Update", $"Updated: {request.RecordName} :: {request.RecordValue}");
                    }

                    if (!result.IsSuccessStatusCode)
                    {
                        return await CreateApiFailureResult("Update", result, asyncApiUri);
                    }

                    var responseJson = await result.Content.ReadAsStringAsync();
                    var operation = JsonConvert.DeserializeObject<ManagedChallengeOperation>(responseJson);

                    if (operation == null || string.IsNullOrWhiteSpace(operation.Id))
                    {
                        return new ActionResult { IsSuccess = false, Message = "Update failed: Managed Challenge API did not return a valid operation id." };
                    }

                    return await PollManagedChallengeOperation(operation.Id, "Update", $"Updated: {request.RecordName} :: {request.RecordValue}");
                }
            }
        }

        private ManagedChallengeRequest CreateManagedChallengeRequest(DnsRecord request)
        {
            var authKey = _credentials["authkey"];
            var authSecret = _credentials["authsecret"];

            return new ManagedChallengeRequest
            {
                ChallengeType = "dns-01",
                Identifier = request.TargetDomainName,
                ResponseKey = request.RecordName,
                ResponseValue = request.RecordValue,
                AuthKey = authKey,
                AuthSecret = authSecret
            };
        }

        private HttpRequestMessage CreateApiRequest(Uri apiUri, ManagedChallengeRequest update)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, apiUri);
            var json = JsonConvert.SerializeObject(update, _serializerSettings);
            req.Content = new StringContent(json, System.Text.UnicodeEncoding.UTF8, "application/json");

            if (!string.IsNullOrWhiteSpace(_hubAssignedInstanceId))
            {
                req.Headers.Add("X-Certify-HubAssignedId", _hubAssignedInstanceId);
            }

            return req;
        }

        private static bool ShouldFallbackToSync(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.NotFound || statusCode == HttpStatusCode.MethodNotAllowed || statusCode == HttpStatusCode.NotImplemented;
        }

        private ActionResult CreateTimeoutFailureResult(string action, TaskCanceledException exp)
        {
            return new ActionResult { IsSuccess = false, Message = $"{action} failed: Managed Challenge API request timed out after {ManagedChallengeApiTimeout.TotalMinutes:0} minutes. {exp.Message}" };
        }

        private ActionResult CreateTransportFailureResult(string action, HttpRequestException exp)
        {
            return new ActionResult { IsSuccess = false, Message = $"{action} failed: {exp.Message}" };
        }

        private async Task<ActionResult> SendManagedChallengeRequest(ManagedChallengeRequest update, Uri apiUri, string action, string successMessage)
        {
            using (var req = CreateApiRequest(apiUri, update))
            {
                HttpResponseMessage result;
                try
                {
                    result = await _client.SendAsync(req);
                }
                catch (TaskCanceledException exp)
                {
                    return CreateTimeoutFailureResult(action, exp);
                }
                catch (HttpRequestException exp)
                {
                    return CreateTransportFailureResult(action, exp);
                }

                using (result)
                {
                    if (!result.IsSuccessStatusCode)
                    {
                        return await CreateApiFailureResult(action, result, apiUri);
                    }

                    var responseJson = await result.Content.ReadAsStringAsync();
                    var updateResult = JsonConvert.DeserializeObject<ActionResult>(responseJson);
                    return updateResult ?? new ActionResult { IsSuccess = true, Message = successMessage };
                }
            }
        }

        private async Task<ActionResult> PollManagedChallengeOperation(string operationId, string action, string successMessage)
        {
            while (true)
            {
                var operationUri = new Uri(_apiBaseUri, $"/api/v1/managedchallenge/requeststatus/{Uri.EscapeDataString(operationId)}");

                using (var request = new HttpRequestMessage(HttpMethod.Get, operationUri))
                {
                    if (!string.IsNullOrWhiteSpace(_hubAssignedInstanceId))
                    {
                        request.Headers.Add("X-Certify-HubAssignedId", _hubAssignedInstanceId);
                    }

                    HttpResponseMessage result;
                    try
                    {
                        result = await _client.SendAsync(request);
                    }
                    catch (TaskCanceledException exp)
                    {
                        return CreateTimeoutFailureResult(action, exp);
                    }
                    catch (HttpRequestException exp)
                    {
                        return CreateTransportFailureResult(action, exp);
                    }

                    using (result)
                    {
                        if (result.StatusCode == HttpStatusCode.NotFound)
                        {
                            return new ActionResult { IsSuccess = false, Message = $"{action} failed: Managed challenge operation '{operationId}' was not found." };
                        }

                        if (!result.IsSuccessStatusCode)
                        {
                            return await CreateApiFailureResult($"{action} status", result, operationUri);
                        }

                        var responseJson = await result.Content.ReadAsStringAsync();
                        var operation = JsonConvert.DeserializeObject<ManagedChallengeOperation>(responseJson);

                        if (operation == null)
                        {
                            return new ActionResult { IsSuccess = false, Message = $"{action} failed: Managed Challenge API returned an invalid operation status response." };
                        }

                        if (!operation.IsCompleted)
                        {
                            await Task.Delay(ManagedChallengeOperationPollInterval);
                            continue;
                        }

                        return operation.Result ?? new ActionResult
                        {
                            IsSuccess = operation.IsSuccess,
                            Message = operation.IsSuccess ? successMessage : $"{action} failed: Managed challenge operation completed without a result."
                        };
                    }
                }
            }
        }

        private async Task<ActionResult> CreateApiFailureResult(string action, HttpResponseMessage result, Uri apiUri)
        {
            var responseJson = await result.Content.ReadAsStringAsync();

            try
            {
                var errorResult = JsonConvert.DeserializeObject<ProblemDetails>(responseJson);
                if (errorResult != null && !string.IsNullOrWhiteSpace(errorResult.Detail))
                {
                    return new ActionResult
                    {
                        IsSuccess = false,
                        Message = $"{action} failed [{result.StatusCode}]: {errorResult.Detail}"
                    };
                }
            }
            catch
            {
            }

            var errorMessage = string.IsNullOrWhiteSpace(responseJson)
                ? "No additional error details available"
                : responseJson;

            return new ActionResult
            {
                IsSuccess = false,
                Message = $"{action} failed [{result.StatusCode}]: {errorMessage}. Check API URL is valid [{apiUri}], auth credentials are correct and authorised for a matching managed challenge."
            };
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            if (string.IsNullOrWhiteSpace(_apiBaseUri?.ToString()))
            {
                return new ActionResult { IsSuccess = false, Message = "Managed Challenge API URL not specified and default Management Hub URI not set. Cannot perform managed challenge." };
            }

            var apiUri = new Uri(_apiBaseUri, "/api/v1/managedchallenge/cleanup");
            var update = CreateManagedChallengeRequest(request);
            return await SendManagedChallengeRequest(update, apiUri, "Cleanup", $"Cleanup: {request.RecordName} :: {request.RecordValue}");
        }

        public async Task<bool> InitProvider(Dictionary<string, string> credentials, Dictionary<string, string> parameters, IHttpClientProvider clientProvider, ILog log = null)
        {
            _credentials = credentials;
            _log = log;
            _parameters = parameters;

#if DEBUG
            _client = clientProvider.CreateClient($"Certify/{Definition.Id}", allowInvalidTls: true);
#else
            _client = clientProvider.CreateClient($"Certify/{Definition.Id}");
#endif

            _client.Timeout = ManagedChallengeApiTimeout;

            _serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore
            };

            if (parameters?.ContainsKey("propagationdelay") == true)
            {
                if (int.TryParse(parameters["propagationdelay"], out var customPropDelay))
                {
                    _customPropagationDelay = customPropDelay;
                }
            }

            if (parameters?.TryGetValue("hubassignedinstanceid", out var hubAssignedInstanceId) == true
                && !string.IsNullOrWhiteSpace(hubAssignedInstanceId))
            {
                _hubAssignedInstanceId = hubAssignedInstanceId;
            }

            var credentialsRequired = true;

            if (_parameters.TryGetValue("api", out var apiBase) && !string.IsNullOrWhiteSpace(apiBase))
            {
                // use specific API base URL if provided
                _apiBaseUri = new System.Uri(apiBase);

                if (!_apiBaseUri.ToString().EndsWith("/"))
                {
                    _apiBaseUri = new Uri($"{_apiBaseUri}/");
                }

                _client.BaseAddress = _apiBaseUri;
            }
            else
            {
                // use hub api URL from service config if not provided in parameters
                var svcConfig = ServiceConfigManager.GetAppServiceConfig();
                var mgmtHubAPI = svcConfig?.ManagementServerHubAPI;

                if (!string.IsNullOrWhiteSpace(mgmtHubAPI))
                {
                    _apiBaseUri = new System.Uri(mgmtHubAPI);
                    if (!_apiBaseUri.ToString().EndsWith("/"))
                    {
                        _apiBaseUri = new Uri($"{_apiBaseUri}/");
                    }

                    _client.BaseAddress = _apiBaseUri;
                }
                else
                {
                    _log?.Error("Certify Managed Challenge DNS Provider could not be created: managed challenge API URL not set.");
                    return false;
                }

                if (!(_credentials?.Any() == true))
                {
                    // no credentials supplied, use hub joining credentials if available and assume managed instance is authorized for managed challenges

                }
            }

            if (credentialsRequired && (_credentials == null || _credentials.Count == 0))
            {
                _log?.Error("Certify Managed Challenge DNS Provider could not be created: credentials missing or not set for managed challenge API.");
                return false;
            }

            return await Task.FromResult(true);
        }

        public Task<List<DnsZone>> GetZones()
        {
            return Task.FromResult(new List<DnsZone>());
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
