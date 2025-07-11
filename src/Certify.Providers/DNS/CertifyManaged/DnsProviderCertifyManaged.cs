using System;
using System.Collections.Generic;
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

    public class DnsProviderCertifyManaged : IDnsProvider
    {
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

#if DEBUG
            // allow invalid TLS
            var handler = new HttpClientHandler();
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ServerCertificateCustomValidationCallback =
                (httpRequestMessage, cert, cetChain, policyErrors) =>
                {
                    return true;
                };
            _client = new HttpClient(handler);

#else
            _client = new HttpClient();

#endif
            _client.DefaultRequestHeaders.Add("User-Agent", "Certify/DnsProviderCertifyManaged");

            _serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore
            };
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

        private string _settingsPath { get; set; }
        private Uri _apiBaseUri { get; set; }

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

            var apiUri = new Uri(_apiBaseUri, "/api/v1/managedchallenge/request");
            var req = new HttpRequestMessage(HttpMethod.Post, apiUri);

            var authKey = _credentials["authkey"];
            var authSecret = _credentials["authsecret"];

            var update = new ManagedChallengeRequest
            {
                ChallengeType = "dns-01",
                Identifier = request.TargetDomainName,
                ResponseKey = request.RecordName,
                ResponseValue = request.RecordValue,
                AuthKey = authKey,
                AuthSecret = authSecret
            };

            var json = JsonConvert.SerializeObject(update, _serializerSettings);

            req.Content = new StringContent(json, System.Text.UnicodeEncoding.UTF8, "application/json");

            var result = await _client.SendAsync(req);

            try
            {
                if (result.IsSuccessStatusCode)
                {
                    var responseJson = await result.Content.ReadAsStringAsync();
                    var updateResult = JsonConvert.DeserializeObject<ActionResult>(responseJson);

                    return new ActionResult { IsSuccess = true, Message = $"Updated: {request.RecordName} :: {request.RecordValue}" };
                }
                else
                {
                    var responseJson = await result.Content.ReadAsStringAsync();

                    // Try to parse as ActionResult first (if API returns structured error)
                    try
                    {
                        var errorResult = JsonConvert.DeserializeObject<ProblemDetails>(responseJson);
                        if (errorResult != null && !string.IsNullOrWhiteSpace(errorResult.Detail))
                        {
                            return new ActionResult
                            {
                                IsSuccess = false,
                                Message = $"Update failed [{result.StatusCode}]: {errorResult.Detail}"
                            };
                        }
                    }
                    catch
                    {
                        // If JSON parsing fails, fall back to raw response
                    }

                    // Fallback to including raw response content
                    var errorMessage = string.IsNullOrWhiteSpace(responseJson)
                        ? "No additional error details available"
                        : responseJson;

                    return new ActionResult
                    {
                        IsSuccess = false,
                        Message = $"Update failed [{result.StatusCode}]: {errorMessage}. Check API URL is valid [{apiUri}], auth credentials are correct and authorised for a matching managed challenge."
                    };
                }
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = false, Message = $"Update failed: {exp.Message}" };
            }
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            if (string.IsNullOrWhiteSpace(_apiBaseUri?.ToString()))
            {
                return new ActionResult { IsSuccess = false, Message = "Managed Challenge API URL not specified and default Management Hub URI not set. Cannot perform managed challenge." };
            }

            var apiUri = new Uri(_apiBaseUri, "/api/v1/managedchallenge/cleanup");
            var req = new HttpRequestMessage(HttpMethod.Post, apiUri);

            var authKey = _credentials["authkey"];
            var authSecret = _credentials["authsecret"];

            var update = new ManagedChallengeRequest
            {
                ChallengeType = "dns-01",
                Identifier = request.TargetDomainName,
                ResponseKey = request.RecordName,
                ResponseValue = request.RecordValue,
                AuthKey = authKey,
                AuthSecret = authSecret
            };

            var json = JsonConvert.SerializeObject(update, _serializerSettings);

            req.Content = new StringContent(json, System.Text.UnicodeEncoding.UTF8, "application/json");

            var result = await _client.SendAsync(req);

            try
            {
                if (result.IsSuccessStatusCode)
                {
                    var responseJson = await result.Content.ReadAsStringAsync();
                    var updateResult = JsonConvert.DeserializeObject<ActionResult>(responseJson);

                    return new ActionResult { IsSuccess = true, Message = $"Cleanup: {request.RecordName} :: {request.RecordValue}" };
                }
                else
                {
                    return new ActionResult { IsSuccess = false, Message = $"Cleanup failed [{result.StatusCode}] : check API URL is valid [{apiUri}], auth credentials are correct and authorised for a matching managed challenge." };
                }
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = false, Message = $"Cleanup failed: {exp.Message}" };
            }
        }

        public async Task<bool> InitProvider(Dictionary<string, string> credentials, Dictionary<string, string> parameters, ILog log = null)
        {
            _credentials = credentials;
            _log = log;
            _parameters = parameters;

            if (_credentials == null || _credentials.Count == 0)
            {
                _log.Error("Certify Managed Challenge DNS Provider could not be created: credentials missing or not set for managed challenge API.");
                return false;
            }

            if (parameters?.ContainsKey("propagationdelay") == true)
            {
                if (int.TryParse(parameters["propagationdelay"], out var customPropDelay))
                {
                    _customPropagationDelay = customPropDelay;
                }
            }

            if (_parameters.TryGetValue("api", out var apiBase) && !string.IsNullOrWhiteSpace(apiBase))
            {
                _apiBaseUri = new System.Uri(apiBase);

                if (!_apiBaseUri.ToString().EndsWith("/"))
                {
                    _apiBaseUri = new Uri($"{_apiBaseUri}/");
                }

                _client.BaseAddress = _apiBaseUri;
            }
            else
            {
                var svcConfig = ServiceConfigManager.GetAppServiceConfig();
                var mgmtHubAPI = svcConfig?.ManagementServerHubAPI;

                if (!string.IsNullOrWhiteSpace(mgmtHubAPI))
                {
                    // if we have a management hub API URL, use that
                    _apiBaseUri = new System.Uri(mgmtHubAPI);

                    if (!_apiBaseUri.ToString().EndsWith("/"))
                    {
                        _apiBaseUri = new Uri($"{_apiBaseUri}/");
                    }

                    _client.BaseAddress = _apiBaseUri;
                }
                else
                {
                    _log.Error("Certify Managed Challenge DNS Provider could not be created: managed challenge API URL not set.");
                    return false;
                }
            }

            return await Task.FromResult(true);
        }

        public Task<List<DnsZone>> GetZones()
        {
            return Task.FromResult(new List<DnsZone>());
        }
    }
}
