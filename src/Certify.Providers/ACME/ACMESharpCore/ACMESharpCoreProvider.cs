using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ACMESharp.Protocol;
using ACMESharp.Protocol.Resources;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;

namespace Certfy.Providers.ACME.ACMESharpCore
{

    public class LoggingHandler : DelegatingHandler
    {
        private ILog _log = null;

        public LoggingHandler(HttpMessageHandler innerHandler, ILog log)
            : base(innerHandler)
        {
            _log = log;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_log != null)
            {
                _log.Debug($"Http Request: {request.ToString()}");
                if (request.Content != null)
                {
                    _log.Debug(await request.Content.ReadAsStringAsync());
                }
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (_log != null)
            {
                _log.Debug($"Http Response: {response.ToString()}");

                if (response.Content != null)
                {
                    _log.Debug(await response.Content.ReadAsStringAsync());
                }
            }

            return response;
        }
    }

    public class ACMESharpCoreProvider : IACMEClientProvider, IVaultProvider
    {
        private ILog _log;
        private string _acmeBaseUri;
        private AcmeProtocolClient _client;
        private LoggingHandler _loggingHandler;
        private string _userAgentName = "Certify SSL Manager";
        private string _settingsFolder;
        private HttpClient _httpClient;

        public ACMESharpCoreProvider(string acmeBaseUri, string settingsPath, string userAgentName)
        {
            _settingsFolder = settingsPath;

            var assembly = typeof(AcmeProtocolClient).Assembly.GetName();

            _userAgentName = $"{userAgentName} {assembly.Name}/{assembly.Version.ToString()}";

            _acmeBaseUri = acmeBaseUri;

        }

        public async Task<bool> InitProvider(ILog log = null)
        {
            if (log != null)
            {
                _log = log;
            }

            _loggingHandler = new LoggingHandler(new HttpClientHandler(), _log);
            var customHttpClient = new System.Net.Http.HttpClient(_loggingHandler);
            customHttpClient.DefaultRequestHeaders.Add("User-Agent", _userAgentName);

            _httpClient = new HttpClient();

            await LoadSettings();

            _client = new AcmeProtocolClient(_httpClient);
            _client.Directory = await _client.GetDirectoryAsync();

            if (_client.Account == null)
            {
                throw new Exception("AcmeClient was unable to find or create an account");
            }

            return true;
        }

        private async Task LoadSettings()
        {
            // load settings related to this provider
            await _client.GetNonceAsync();
        }

        public async Task<ActionResult<Certify.Models.AccountDetails>> AddNewAccountAndAcceptTOS(ILog log, string email)
        {
            _log.Debug("Adding new account");

            try
            {
                var account = await _client.CreateAccountAsync(new string[] { email.Trim() }, termsOfServiceAgreed: true);
                return new ActionResult<Certify.Models.AccountDetails>
                {
                    IsSuccess = true,
                    Result = new Certify.Models.AccountDetails
                    {
                        ID = account.Payload.Id,
                        AccountKey = account.Payload.Key.ToString()
                    }
                };
            }
            catch (Exception exp)
            {
                return new ActionResult<Certify.Models.AccountDetails>
                {
                    IsSuccess = false,
                    Message = "Failed to add account. Check email address is valid and system can contact the ACME API. " + exp.ToString()
                };
            }
        }

        public async Task<PendingOrder> BeginCertificateOrder(ILog log, CertRequestConfig config, string orderUri = null)
        {
            var domains = new List<string>{
                config.PrimaryDomain
                    };

            domains.AddRange(config.SubjectAlternativeNames);

            var order = await _client.CreateOrderAsync(domains.Distinct());
            var authorizations = new List<PendingAuthorization>();

            foreach (var authz in order.Payload.Authorizations)
            {
                var pendingAuthz = new PendingAuthorization
                {
                    //TODO
                };
                authorizations.Add(pendingAuthz);
            }
            return new PendingOrder { OrderUri = order.OrderUrl, Authorizations = authorizations };
        }

        public Task<bool> ChangeAccountKey(ILog log)
        {
            throw new NotImplementedException();
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task<PendingAuthorization> CheckValidationCompleted(ILog log, string challengeType, PendingAuthorization pendingAuthorization)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            throw new System.NotImplementedException();
        }

        public async Task<ProcessStepResult> CompleteCertificateRequest(ILog log, CertRequestConfig config, string orderId)
        {
            var order = await _client.GetOrderDetailsAsync("");
            var certData = await _client.GetOrderCertificateAsync(order);

            return new ProcessStepResult { IsSuccess = true };
        }

        public async void DeleteContactRegistration(string id)
        {
            await _client.DeactivateAccountAsync();
        }

        public void EnableSensitiveFileEncryption()
        {
            throw new System.NotImplementedException();
        }

        public async Task<string> GetAcmeAccountStatus()
        {
            var account = await _client.CheckAccountAsync();
            return account.Kid;
        }

        public string GetAcmeBaseURI()
        {
            return _acmeBaseUri;
        }

        public async Task<Uri> GetAcmeTermsOfService()
        {
            var tos = await _client.GetDirectoryAsync();
            return new Uri(tos.Meta.TermsOfService);
        }

        public List<RegistrationItem> GetContactRegistrations()
        {
            throw new System.NotImplementedException();
        }

        public string GetProviderName()
        {
            return "ACMESharpCore";
        }

        public async Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate)
        {
            try
            {
                var pkcs = new Org.BouncyCastle.Pkcs.Pkcs12Store(File.Open(managedCertificate.CertificatePath, FileMode.Open), "".ToCharArray());

                var certAliases = pkcs.Aliases.GetEnumerator();
                certAliases.MoveNext();

                var certEntry = pkcs.GetCertificate(certAliases.Current.ToString());
                var certificate = certEntry.Certificate;

                // revoke certificate
                var der = certificate.GetEncoded();
                await _client.RevokeCertificateAsync(der, RevokeReason.Unspecified);

                return new StatusMessage { IsOK = true, Message = $"Certificate revoke completed." };
            }
            catch (Exception exp)
            {
                return new StatusMessage { IsOK = false, Message = $"Failed to revoke certificate: {exp.Message}" };
            }

        }

        public async Task<StatusMessage> SubmitChallenge(ILog log, string challengeType, AuthorizationChallengeItem attemptedChallenge)
        {

            if (attemptedChallenge == null)
            {
                return new StatusMessage
                {
                    IsOK = false,
                    Message = "Challenge could not be submitted. No matching attempted challenge."
                };
            }

            if (!attemptedChallenge.IsValidated)
            {
                //  IChallengeContext challenge = (IChallengeContext)attemptedChallenge.ChallengeData;
                try
                {
                    var result = await _client.AnswerChallengeAsync(attemptedChallenge.ResourceUri);

                    var attempts = 10;

                    while (attempts > 0 && result.Status == "pending" || result.Status == "processing")
                    {
                        result = await _client.GetChallengeDetailsAsync(attemptedChallenge.ResourceUri);
                    }

                    if (result.Status == "valid")
                    {
                        return new StatusMessage
                        {
                            IsOK = true,
                            Message = "Submitted"
                        };
                    }
                    else
                    {

                        return new StatusMessage
                        {
                            IsOK = false,
                            Message = result.Error.ToString()
                        };
                    }
                }
                catch (ACMESharp.Protocol.AcmeProtocolException exp)
                {
                    var msg = $"Submit Challenge failed: {exp.ProblemDetail}";

                    log.Error(msg);

                    return new StatusMessage
                    {
                        IsOK = false,
                        Message = msg
                    };
                }
            }
            else
            {
                return new StatusMessage
                {
                    IsOK = true,
                    Message = "Validated"
                };
            }
        }
    }
}
