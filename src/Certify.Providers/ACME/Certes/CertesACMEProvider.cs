using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Shared.Core.Utils.PKI;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.X509;

namespace Certify.Providers.ACME.Certes
{
    /// <summary>
    /// Certes Provider settings for serialization
    /// </summary>
    public class CertesSettings
    {
        public string AccountEmail { get; set; }
        public string AccountUri { get; set; }
        public string AccountKey { get; set; }
    }

    public class DiagEcKey
    {
        public string kty { get; set; }
        public string crv { get; set; }
        public string x { get; set; }
        public string y { get; set; }
    }

    // used to diagnose account key faults
    public class DiagAccountInfo
    {
        public int ID { get; set; }
        public DiagEcKey Key { get; set; }
    }

    public class LoggingHandler : DelegatingHandler
    {
        public DiagAccountInfo DiagAccountInfo { get; set; }
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
                _log.Debug($"Http Request: {request}");
                if (request.Content != null)
                {
                    _log.Debug(await request.Content.ReadAsStringAsync());
                }
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (_log != null)
            {
                _log.Debug($"Http Response: {response}");

                if (response.Content != null)
                {
                    _log.Debug(await response.Content.ReadAsStringAsync());
                }
            }

            return response;
        }
    }

    /// <summary>
    /// ACME Provider using certes https://github.com/fszlin/certes
    /// </summary>
    public class CertesACMEProvider : IACMEClientProvider
    {
        private AcmeContext _acme;

        private Uri _serviceUri = null;

        private readonly string _settingsFolder = null;
        private readonly string _settingsBaseFolder = null;

        private CertesSettings _settings = null;
        private Dictionary<string, IOrderContext> _currentOrders;
        private IdnMapping _idnMapping = new IdnMapping();
        private DateTime _lastInitDateTime = new DateTime();
        private readonly bool _newContactUseCurrentAccountKey = false;

        private AcmeHttpClient _httpClient;
        private LoggingHandler _loggingHandler;

        private readonly string _userAgentName = "Certify SSL Manager";
        private ILog _log = null;

        private List<byte[]> _issuerCertCache = new List<byte[]>();

        private ACMECompatibilityMode _compatibilityMode = ACMECompatibilityMode.Standard;
        private bool _allowInvalidTls = false;

        public bool EnableUnknownCARoots { get; set; } = true;

        /// <summary>
        /// Default output when finalizing a certificate download: pfx (single file container), pem (multiple files), all (pfx, pem etc)
        /// </summary>
        public string DefaultCertificateFormat { get; set; } = "pfx";

        public CertesACMEProvider(string acmeBaseUri, string settingsBasePath, string settingsPath, string userAgentName, bool allowInvalidTls = false)
        {
            _settingsFolder = settingsPath;
            _settingsBaseFolder = settingsBasePath;

            var certesAssembly = typeof(AcmeContext).Assembly.GetName();

            _userAgentName = $"{userAgentName}";

            _serviceUri = new Uri(acmeBaseUri);

            _allowInvalidTls = allowInvalidTls;

#pragma warning disable SCS0004 // Certificate Validation has been disabled
            if (allowInvalidTls)
            {
                ServicePointManager.ServerCertificateValidationCallback += (obj, cert, chain, errors) =>
                {
                    // ignore all cert errors when validating URL response
                    return true;
                };
            }
#pragma warning restore SCS0004 // Certificate Validation has been disabled

            RefreshIssuerCertCache();
        }

        public string GetProviderName() => "Certes";

        public string GetAcmeBaseURI() => _serviceUri?.ToString();

        public async Task<Uri> GetAcmeTermsOfService()
        {

            if (_acme == null)
            {
                // no acme context setup yet (account not yet initialised), create a temporary context
                PreInitAcmeContext();
                _acme = new AcmeContext(_serviceUri, null, _httpClient);
            }

            return await _acme.TermsOfService();
        }

        /// <summary>
        /// setup the basic settings before we init the acme context
        /// </summary>
        /// <param name="acmeDirectoryUrl"></param>
        private void PreInitAcmeContext()
        {
            _lastInitDateTime = DateTime.Now;

            var httpHandler = new HttpClientHandler();

#if NETSTANDARD2_1_OR_GREATER
            if (_allowInvalidTls)
            {
                httpHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }
#endif

            _loggingHandler = new LoggingHandler(httpHandler, _log);
            var customHttpClient = new System.Net.Http.HttpClient(_loggingHandler);

            customHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgentName);

            _httpClient = new AcmeHttpClient(_serviceUri, customHttpClient);
        }

        /// <summary>
        /// Initialise provider settings, loading current account key if present
        /// </summary>
        public async Task<bool> InitProvider(ILog log = null, AccountDetails account = null)
        {
            if (log != null)
            {
                _log = log;
            }

            PreInitAcmeContext();

            if (_settings == null)
            {
                if (account == null)
                {
                    // if initalising without a known account, attempt to load details from storage
                    var settingsFilePath = Path.Combine(_settingsFolder, "c-settings.json");
                    if (File.Exists(settingsFilePath))
                    {
                        var json = System.IO.File.ReadAllText(settingsFilePath);
                        _settings = Newtonsoft.Json.JsonConvert.DeserializeObject<CertesSettings>(json);
                    }
                    else
                    {
                        _settings = new CertesSettings();
                    }

                    if (!string.IsNullOrEmpty(_settings.AccountKey))
                    {
                        if (System.IO.File.Exists(Path.Combine(_settingsFolder, "c-acc.key")))
                        {
                            //remove legacy key info
                            System.IO.File.Delete(Path.Combine(_settingsFolder, "c-acc.key"));
                        }
                        SetAcmeContextAccountKey(_settings.AccountKey);
                    }
                    else
                    {
                        // no account key in settings, check .key (legacy key file)
                        if (System.IO.File.Exists(Path.Combine(_settingsFolder, "c-acc.key")))
                        {
                            var pem = System.IO.File.ReadAllText(Path.Combine(_settingsFolder, "c-acc.key"));
                            SetAcmeContextAccountKey(pem);
                        }
                    }
                }
                else
                {
                    _settings = new CertesSettings
                    {
                        AccountEmail = account.Email,
                        AccountKey = account.AccountKey,
                        AccountUri = account.AccountURI
                    };
                    SetAcmeContextAccountKey(_settings.AccountKey);
                }
            }
            else
            {
                SetAcmeContextAccountKey(_settings.AccountKey);
            }

            _currentOrders = new Dictionary<string, IOrderContext>();

            return await Task.FromResult(true);
        }

        private async Task<string> CheckAcmeAccount()
        {
            // check our current account ID and key match the values LE expects
            if (_acme == null)
            {
                return "none";
            }

            try
            {
                var accountContext = await _acme.Account();
                var account = await accountContext.Resource();

                if (account.Status == AccountStatus.Valid)
                {
                    if (account.TermsOfServiceAgreed == false)
                    {
                        return "tos-required";
                    }
                    else
                    {
                        // all good
                        return "ok";
                    }
                }
                else
                {
                    if (account.Status == AccountStatus.Revoked)
                    {
                        return "account-revoked";
                    }

                    if (account.Status == AccountStatus.Deactivated)
                    {
                        return "account-deactivated";
                    }
                }

                return "unknown";
            }
            catch (AcmeRequestException exp)
            {
                if (exp.Error.Type == "urn:ietf:params:acme:error:accountDoesNotExist")
                {
                    return "account-doesnotexist";
                }
                else
                {
                    return "account-error";
                }
            }
            catch (Exception)
            {
                // we failed to check the account status, probably because of connectivity. Assume OK
                return "ok";
            }
        }

        public async Task<bool> DeactivateAccount(ILog log)
        {
            var acc = await _acme.Account();
            var result = await acc.Deactivate();

            if (result.Status == AccountStatus.Deactivated)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public async Task<ActionResult<AccountDetails>> UpdateAccount(ILog log, string email, bool termsAgreed)
        {

            var acc = await _acme.Account();

            log?.Information($"Updating account {email} with certificate authority");

            var results = await acc.Update(new string[] { (email.StartsWith("mailto:") ? email : "mailto:" + email) }, termsAgreed);
            if (results.Status == AccountStatus.Valid)
            {

                log?.Information($"Account updated.");
                await PopulateSettingsFromCurrentAccount();
                _settings.AccountEmail = email;

                return new ActionResult<AccountDetails>
                {
                    IsSuccess = true,
                    Result = new AccountDetails
                    {
                        AccountKey = _settings.AccountKey,
                        Email = _settings.AccountEmail,
                        AccountURI = _settings.AccountUri,
                        ID = _settings.AccountUri.Split('/').Last()
                    }
                };
            }
            else
            {
                var msg = $"Failed to update account contact. {results.Status}";
                log?.Warning(msg);
                return new ActionResult<AccountDetails> { IsSuccess = false, Message = msg };
            }
        }

        public async Task<bool> ChangeAccountKey(ILog log)
        {
            if (_acme == null)
            {
                log?.Error("No account context. Cannot update account key.");

                return false;
            }
            else
            {
                // allocate new key and inform LE of key change
                // same default key type as certes
                var newKey = KeyFactory.NewKey(KeyAlgorithm.ES256);

                await _acme.ChangeKey(newKey);

                await PopulateSettingsFromCurrentAccount();

                return true;
            }
        }

        private async Task PopulateSettingsFromCurrentAccount()
        {
            var pem = _acme.AccountKey.ToPem();

            _settings.AccountKey = pem;
            _settings.AccountUri = (await _acme.Account()).Location.ToString();
        }

        /// <summary>
        /// Determine if we have a currently registered account with the ACME CA (e.g. Let's Encrypt)
        /// </summary>
        /// <returns>  </returns>
        public bool IsAccountRegistered()
        {
            if (!string.IsNullOrEmpty(_settings.AccountEmail))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Set a new account key from PEM encoded text
        /// </summary>
        /// <param name="pem">  </param>
        private void SetAcmeContextAccountKey(string pem)
        {
            var accountkey = KeyFactory.FromPem(pem);

            _acme = new AcmeContext(_serviceUri, accountkey, _httpClient);

            if (_settings.AccountKey != pem)
            {
                _settings.AccountKey = pem;
            }
        }

        public AccountDetails GetCurrentAcmeAccount()
        {
            if (!string.IsNullOrEmpty(_settings.AccountUri))
            {
                return new AccountDetails
                {
                    ID = _settings.AccountUri.Split('/').Last(),
                    AccountKey = _settings.AccountKey,
                    AccountURI = _settings.AccountUri,
                    Email = _settings.AccountEmail
                };
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Register a new account with the ACME CA (e.g. Let's Encrypt), accepting terms and conditions
        /// </summary>
        /// <param name="log">  </param>
        /// <param name="email">  </param>
        /// <returns>  </returns>
        public async Task<ActionResult<AccountDetails>> AddNewAccountAndAcceptTOS(ILog log, string email, string eabKeyId, string eabKey, string eabKeyAlg)
        {

            try
            {
                IKey accKey = null;

                if (_newContactUseCurrentAccountKey && !string.IsNullOrEmpty(_settings.AccountKey))
                {
                    accKey = KeyFactory.FromPem(_settings.AccountKey);
                }

                // start new account context, create new account (with new key, if not enabled)
                _acme = new AcmeContext(_serviceUri, accKey, _httpClient);

                try
                {
                    _ = await _acme.GetDirectory(throwOnError: true);
                }
                catch
                {
                    return new ActionResult<AccountDetails>("Failed to communicate with the Certificate Authority. Check their status page for service announcements and ensure your system can make outgoing https requests.", false);
                }

                var account = await _acme.NewAccount(email, true, eabKeyId, eabKey, eabKeyAlg);

                _settings.AccountEmail = email;

                await PopulateSettingsFromCurrentAccount();

                log?.Information($"Registering account {email} with certificate authority");

                return new ActionResult<AccountDetails>
                {
                    IsSuccess = true,
                    Result = new AccountDetails
                    {
                        AccountKey = _settings.AccountKey,
                        Email = _settings.AccountEmail,
                        AccountURI = _settings.AccountUri,
                        ID = _settings.AccountUri.Split('/').Last()
                    }
                };

            }
            catch (Exception exp)
            {
                log.Error($"Failed to register account with certificate authority: {exp.Message}");
                return new ActionResult<AccountDetails> { IsSuccess = false, Message = $"Failed to register account with certificate authority: {exp.Message}" };
            }
        }

        /// <summary>
        /// Begin order for new certificate for one or more domains, fetching the required challenges
        /// to complete
        /// </summary>
        /// <param name="log">  </param>
        /// <param name="config">  </param>
        /// <param name="orderUri"> Uri of existing order to resume </param>
        /// <returns>  </returns>
        public async Task<PendingOrder> BeginCertificateOrder(ILog log, CertRequestConfig config, string orderUri = null)
        {
            if (DateTime.Now.Subtract(_lastInitDateTime).TotalMinutes > 30)
            {
                // our acme context is stale, start a new one
                await InitProvider(_log);
            }

            var pendingOrder = new PendingOrder { IsPendingAuthorizations = true };

            // prepare a list of all pending authorization we need to complete, or those we have
            // already satisfied
            var authzList = new List<PendingAuthorization>();

            //if no alternative domain specified, use the primary domain as the subject
            var domainOrders = new List<string>();

            if (!string.IsNullOrEmpty(config.PrimaryDomain))
            {
                // order all of the distinct domains in the config (primary + SAN).
                domainOrders.Add(_idnMapping.GetAscii(config.PrimaryDomain));
            }

            if (config.SubjectAlternativeNames != null)
            {
                foreach (var s in config.SubjectAlternativeNames)
                {
                    if (!domainOrders.Contains(s))
                    {
                        domainOrders.Add(_idnMapping.GetAscii(s));
                    }
                }
            }

            var certificateIdentifiers = new List<Identifier>();

            // prepare list of identifiers on certs, which may be domains or ip addresses
            foreach (var d in domainOrders.Where(i => i != null))
            {
                certificateIdentifiers.Add(new Identifier { Type = IdentifierType.Dns, Value = d });
            }

            if (config.SubjectIPAddresses?.Any() == true)
            {
                foreach (var i in config.SubjectIPAddresses)
                {
                    certificateIdentifiers.Add(new Identifier { Type = IdentifierType.Ip, Value = i });
                }
            }

            try
            {
                IOrderContext order = null;
                var remainingAttempts = 3;
                var orderCreated = false;
                object lastException = null;
                var orderErrorMsg = "";

                try
                {
                    // first check we can access the ACME API
                    try
                    {
                        _ = await _acme.GetDirectory(throwOnError: true);
                    }
                    catch (AcmeException exp)
                    {
                        var msg = exp.Message;
                        log.Error(exp.Message);
                        return new PendingOrder(msg);
                    }

                    // attempt to start our certificate order
                    while (!orderCreated && remainingAttempts > 0)
                    {
                        try
                        {
                            remainingAttempts--;

                            log.Information($"BeginCertificateOrder: creating/retrieving order. Retries remaining:{remainingAttempts} ");

                            if (orderUri != null)
                            {
                                order = _acme.Order(new Uri(orderUri));
                            }
                            else
                            {
                                // begin new order, with optional preference for the expiry
                                var notAfter = (config.PreferredExpiryDays != null ? (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays((float)config.PreferredExpiryDays) : null);
                                order = await _acme.NewOrder(certificateIdentifiers, notAfter: notAfter);
                            }

                            if (order != null)
                            {
                                orderCreated = true;
                            }
                        }
                        catch (Exception exp)
                        {
                            log.Error(exp.ToString());

                            orderErrorMsg = exp.Message;

                            if (exp is TaskCanceledException)
                            {
                                log.Warning($"BeginCertificateOrder: timeout while communicating with the ACME API");
                            }

                            if (exp is AcmeRequestException)
                            {
                                var err = (exp as AcmeRequestException).Error;

                                // e.g. urn:ietf:params:acme:error:userActionRequired

                                orderErrorMsg = err?.Detail ?? orderErrorMsg;

                                if ((int)err.Status == 429)
                                {
                                    // hit an ACME API rate limit 

                                    log.Warning($"BeginCertificateOrder: encountered a rate limit while communicating with the ACME API");

                                    return new PendingOrder(orderErrorMsg);
                                }

                                if (err.Type?.EndsWith("accountDoesNotExist") == true)
                                {
                                    // wrong account details, probably used staging for prod or vice versa
                                    log.Warning($"BeginCertificateOrder: attempted to use invalid account details with the ACME API");

                                    return new PendingOrder(orderErrorMsg);

                                }

                            }
                            else if (exp.InnerException != null && exp.InnerException is AcmeRequestException)
                            {
                                orderErrorMsg = (exp.InnerException as AcmeRequestException).Error?.Detail ?? orderErrorMsg;
                            }

                            remainingAttempts--;

                            log.Error($"BeginCertificateOrder: error creating order. Retries remaining:{remainingAttempts} :: {orderErrorMsg} ");

                            lastException = exp;

                            if (remainingAttempts == 0)
                            {
                                // all attempts to create order failed
                                throw;
                            }
                            else
                            {
                                await Task.Delay(1000);
                            }
                        }
                    }
                }
                catch (NullReferenceException exp)
                {
                    var msg = $"Failed to begin certificate order (account problem or API is not currently available): {exp.Message}";

                    log.Error(msg);

                    return new PendingOrder(msg);
                }

                if (order == null || order.Location == null)
                {

                    var msg = "Failed to begin certificate order.";

                    if (lastException is AcmeRequestException)
                    {
                        var err = (lastException as AcmeRequestException).Error;

                        msg = err?.Detail ?? msg;
                        if (lastException != null && (lastException as Exception).InnerException is AcmeRequestException)
                        {
                            msg = ((lastException as Exception).InnerException as AcmeRequestException).Error?.Detail ?? msg;
                        }
                    }
                    else
                    {
                        if (lastException is Exception)
                        {
                            msg += "::" + (lastException as Exception).ToString();
                        }
                    }

                    return new PendingOrder("Error creating Order with Certificate Authority: " + msg);

                }

                orderUri = order.Location.ToString();

                pendingOrder.OrderUri = orderUri;

                log.Information($"Created ACME Order: {orderUri}");

                // track order in memory, keyed on order Uri
                if (_currentOrders.Keys.Contains(orderUri))
                {
                    _currentOrders.Remove(orderUri);
                }

                _currentOrders.Add(orderUri, order);

                // handle order status 'Ready' if all authorizations are already valid
                var requireAuthzFetch = true;
                var orderDetails = await order.Resource();

                if (orderDetails.Status == OrderStatus.Ready)
                {
                    pendingOrder.IsPendingAuthorizations = false;
                    requireAuthzFetch = true;
                }

                if (_compatibilityMode == ACMECompatibilityMode.Standard)
                {
                    if (orderDetails.Status == OrderStatus.Valid)
                    {
                        pendingOrder.IsPendingAuthorizations = false;
                        requireAuthzFetch = true;
                    }
                }

                if (requireAuthzFetch)
                {
                    // get all required pending (or already valid) authorizations for this order

                    log.Information($"Fetching Authorizations.");

                    var orderAuthorizations = await order.Authorizations();

                    var useAuthzForChallengeStatus = true;

                    // get the challenges for each authorization
                    foreach (var authContext in orderAuthorizations)
                    {

                        string authzDomain = null;
                        var authIdentifierType = IdentifierType.Dns;

                        log.Debug($"Fetching Authz Challenges: {authContext.Location}");

                        IList<Challenge> allIdentifierChallenges;

                        try
                        {
                            var res = await authContext.Resource();

                            authzDomain = res?.Identifier.Value;

                            if (res.Wildcard == true)
                            {
                                authzDomain = "*." + authzDomain;
                            }

                            authIdentifierType = res.Identifier.Type;
                            allIdentifierChallenges = res.Challenges;
                        }
                        catch
                        {
                            log.Error("Failed to fetch auth challenge details from ACME API.");
                            break;
                        }

                        var challenges = new List<AuthorizationChallengeItem>();

                        // determine if we are interested in each challenge type before fetching the challenge details, some APIs hang when you fetch a validated auth
                        var includeHttp01 = true;
                        var includeDns01 = true;

                        if (_compatibilityMode == ACMECompatibilityMode.AltProvider1)
                        {
                            if (config.Challenges?.Any(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP) != true)
                            {
                                includeHttp01 = false;
                            }

                            if (config.Challenges?.Any(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS) != true)
                            {
                                includeDns01 = false;
                            }

                            if (includeDns01 == false && includeHttp01 == false)
                            {
                                // if neither challenge is enabled, use both
                                includeHttp01 = true;
                                includeDns01 = true;
                            }
                        }

                        // add http challenge (if any)
                        if (includeHttp01)
                        {
                            IChallengeContext httpChallenge = null;
                            try
                            {
                                httpChallenge = await authContext.Http();
                            }
                            catch (Exception)
                            {
                                log.Information("Could not fetch an http-01 challenge for this identifier: " + authzDomain);
                            }

                            if (httpChallenge != null)
                            {
                                try
                                {
                                    Challenge httpChallengeStatus;
                                    if (useAuthzForChallengeStatus)
                                    {
                                        // some ACME providers don't support /challenge/ to get challenge status so retrieve all from the /authz/ instead
                                        httpChallengeStatus = allIdentifierChallenges.FirstOrDefault(c => c.Type == ChallengeTypes.Http01);
                                    }
                                    else
                                    {
                                        // fetch status directly from /challenge/ api
                                        httpChallengeStatus = await httpChallenge.Resource();
                                    }

                                    log.Information($"Got http-01 challenge {httpChallengeStatus.Url}");

                                    if (httpChallengeStatus.Status == ChallengeStatus.Invalid)
                                    {
                                        log.Error($"HTTP challenge has an invalid status");
                                    }

                                    challenges.Add(new AuthorizationChallengeItem
                                    {
                                        ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP,
                                        Key = httpChallenge.Token,
                                        Value = httpChallenge.KeyAuthz,
                                        ChallengeData = httpChallenge,
                                        ResourceUri = $"http://{authzDomain.Replace("*.", "")}/.well-known/acme-challenge/{httpChallenge.Token}",
                                        ResourcePath = $".well-known\\acme-challenge\\{httpChallenge.Token}",
                                        IsValidated = (httpChallengeStatus.Status == ChallengeStatus.Valid)
                                    });
                                }
                                catch (Exception exp)
                                {
                                    var msg = $"Could fetch http-01 challenge details from ACME server (timeout) : {exp.Message}";

                                    log.Error(msg);

                                    return new PendingOrder(msg);
                                }
                            }
                        }

                        // add dns challenge (if any)
                        if (includeDns01)
                        {
                            IChallengeContext dnsChallenge = null;
                            try
                            {
                                dnsChallenge = await authContext.Dns();
                            }
                            catch (Exception)
                            {
                                log.Information("Could not fetch a dns-01 challenge for this identifier: " + authzDomain);
                            }

                            if (dnsChallenge != null)
                            {
                                Challenge dnsChallengeStatus;
                                if (useAuthzForChallengeStatus)
                                {
                                    // some ACME providers don't support /challenge/ to get challenge status so retrieve all from the /authz/ instead
                                    dnsChallengeStatus = allIdentifierChallenges.FirstOrDefault(c => c.Type == ChallengeTypes.Dns01);
                                }
                                else
                                {
                                    // fetch status directly from /challenge/ api
                                    dnsChallengeStatus = await dnsChallenge.Resource();
                                }

                                log.Information($"Got dns-01 challenge {dnsChallengeStatus.Url}");

                                if (dnsChallengeStatus.Status == ChallengeStatus.Invalid)
                                {
                                    log.Error($"DNS challenge has an invalid status");
                                }

                                var dnsValue = _acme.AccountKey.DnsTxt(dnsChallenge.Token); //ComputeDnsValue(dnsChallenge, _acme.AccountKey);
                                var dnsKey = $"_acme-challenge.{authzDomain}".Replace("*.", "");

                                challenges.Add(new AuthorizationChallengeItem
                                {
                                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                                    Key = dnsKey,
                                    Value = dnsValue,
                                    ChallengeData = dnsChallenge,
                                    IsValidated = (dnsChallengeStatus.Status == ChallengeStatus.Valid)
                                });
                            }
                        }

                        // report back on the challenges we now may need to attempt
                        authzList.Add(
                         new PendingAuthorization
                         {
                             Challenges = challenges,
                             Identifier = new IdentifierItem
                             {
                                 Dns = authzDomain,
                                 ItemType = authIdentifierType == IdentifierType.Ip ? "ip" : "dns",
                                 IsAuthorizationPending = !challenges.Any(c => c.IsValidated) //auth is pending if we have no challenges already validated
                             },
                             AuthorizationContext = authContext,
                             IsValidated = challenges.Any(c => c.IsValidated),
                             OrderUri = orderUri
                         });
                    }

                    pendingOrder.Authorizations = authzList;
                }

                return pendingOrder;
            }
            catch (AcmeRequestException exp)
            {
                // failed to register one or more domain identifier with LE (invalid, rate limit or
                // CAA fail?)

                var msg = $"Could not begin certificate order: {exp.Error?.Detail}";

                log.Error(msg);

                return new PendingOrder(msg);
            }
        }

        /// <summary>
        /// if not already validate, ask ACME CA to check we have answered the nominated challenges correctly
        /// </summary>
        /// <param name="log">  </param>
        /// <param name="challengeType">  </param>
        /// <param name="attemptedChallenge">  </param>
        /// <returns>  </returns>
        public async Task<StatusMessage> SubmitChallenge(ILog log, string challengeType, PendingAuthorization pendingAuthorization)
        {
            if (pendingAuthorization.AttemptedChallenge == null)
            {
                return new StatusMessage
                {
                    IsOK = false,
                    Message = "Challenge could not be submitted. No matching attempted challenge."
                };
            }

            if (!pendingAuthorization.AttemptedChallenge.IsValidated)
            {
                try
                {
                    await _acme.HttpClient.ConsumeNonce();
                }
                catch (Exception)
                {
                    return new StatusMessage
                    {
                        IsOK = false,
                        Message = "Failed to resume communication with Certificate Authority API. Try again later."
                    };
                }

                var challenge = (IChallengeContext)pendingAuthorization.AttemptedChallenge.ChallengeData;
                try
                {
                    // submit challenge to ACME CA to validate
                    var result = await challenge.Validate();

                    return new StatusMessage
                    {
                        IsOK = result.Status != ChallengeStatus.Invalid,
                        Message = "Challenge Submitted for Validation"
                    };
                }
                catch (AcmeRequestException exp)
                {
                    var msg = $"Submit Challenge failed: {exp.Error?.Detail}";

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

        /// <summary>
        /// After we have asked the CA to check we have responded to the required challenges, check
        /// the result to see if they are now valid
        /// </summary>
        /// <param name="log">  </param>
        /// <param name="challengeType">  </param>
        /// <param name="pendingAuthorization">  </param>
        /// <returns>  </returns>
        public async Task<PendingAuthorization> CheckValidationCompleted(ILog log, string challengeType, PendingAuthorization pendingAuthorization)
        {
            var authz = (IAuthorizationContext)pendingAuthorization.AuthorizationContext;

            await _acme.HttpClient.ConsumeNonce();

            var res = await authz.Resource();

            var attempts = 20;
            while (attempts > 0 && (res.Status != AuthorizationStatus.Valid && res.Status != AuthorizationStatus.Invalid))
            {
                res = await authz.Resource();

                attempts--;

                // if status is not yet valid or invalid, wait a sec and try again
                if (res.Status != AuthorizationStatus.Valid && res.Status != AuthorizationStatus.Invalid)
                {
                    await Task.Delay(1000);
                }
            }

            if (res.Status == AuthorizationStatus.Valid)
            {
                pendingAuthorization.Identifier.IsAuthorizationPending = false;
                pendingAuthorization.Identifier.Status = "valid";
                pendingAuthorization.IsValidated = true;
            }
            else
            {
                pendingAuthorization.Identifier.Status = "invalid";

                //determine error
                try
                {
                    var challenge = res.Challenges.FirstOrDefault(c => c.Type == challengeType);
                    if (challenge != null)
                    {
                        if (challenge.Error != null)
                        {

                            pendingAuthorization.AuthorizationError = $"{challenge.Error.Detail} {challenge.Error.Status} {challenge.Error.Type}";
                        }

                    }
                }
                catch
                {
                    log.Warning("Failed to determine error message for failed authorization.");
                }
                pendingAuthorization.Identifier.ValidationError = "Failed";
                pendingAuthorization.Identifier.ValidationErrorType = "Error";
                pendingAuthorization.IsValidated = false;
            }
            return pendingAuthorization;
        }

        /// <summary>
        /// Once validation has completed for our requested domains we can complete the certificate
        /// request by submitting a Certificate Signing Request (CSR) to the CA
        /// </summary>
        /// <param name="log">  </param>
        /// <param name="primaryDnsIdentifier">  </param>
        /// <param name="alternativeDnsIdentifiers">  </param>
        /// <param name="config">  </param>
        /// <returns>  </returns>
        public async Task<ProcessStepResult> CompleteCertificateRequest(ILog log, CertRequestConfig config, string orderId, string pwd, string preferredChain)
        {
            var orderContext = _currentOrders[orderId];

            // check order status, if it's not 'ready' then try a few more times before giving up
            var order = await orderContext.Resource();

            var attempts = 5;
            while (attempts > 0 && (order?.Status != OrderStatus.Ready && order?.Status != OrderStatus.Valid))
            {
                await Task.Delay(2000);
                order = await orderContext.Resource();
                attempts--;
            }

            if (order?.Status != OrderStatus.Ready && order?.Status != OrderStatus.Valid)
            {
                return new ProcessStepResult { IsSuccess = false, ErrorMessage = "Certificate Request did not complete. Order did not reach Ready status in the time allowed.", Result = order };
            }

            // generate temp keypair for signing CSR
            var keyAlg = KeyAlgorithm.RS256;
            var keySize = 2048;

            if (!string.IsNullOrEmpty(config.CSRKeyAlg))
            {
                if (config.CSRKeyAlg == StandardKeyTypes.RSA256)
                {
                    keyAlg = KeyAlgorithm.RS256;
                }
                else if (config.CSRKeyAlg == StandardKeyTypes.RSA256_3072)
                {
                    keyAlg = KeyAlgorithm.RS256;
                    keySize = 3072;
                }
                else if (config.CSRKeyAlg == StandardKeyTypes.RSA256_4096)
                {
                    keyAlg = KeyAlgorithm.RS256;
                    keySize = 4096;
                }
                else if (config.CSRKeyAlg == StandardKeyTypes.ECDSA256)
                {
                    keyAlg = KeyAlgorithm.ES256;
                }
                else if (config.CSRKeyAlg == StandardKeyTypes.ECDSA384)
                {
                    keyAlg = KeyAlgorithm.ES384;
                }
                else if (config.CSRKeyAlg == StandardKeyTypes.ECDSA521)
                {
                    keyAlg = KeyAlgorithm.ES512;
                }
            }

            var csrKey = KeyFactory.NewKey(keyAlg, keySize);

            if (!string.IsNullOrEmpty(config.CustomPrivateKey))
            {
                csrKey = KeyFactory.FromPem(config.CustomPrivateKey);
            }
            else if (config.ReusePrivateKey)
            {
                // check if we have a stored private key already and want to use it again
                var savedKey = LoadSavedPrivateKey(config);

                if (savedKey != null)
                {
                    csrKey = savedKey;
                }
            }

            var certFriendlyName = $"{config.PrimaryDomain} [Certify] ";

            // generate cert
            CertificateChain certificateChain = null;
            DateTime? certExpiration = null;

            try
            {
                if (order.Status == OrderStatus.Valid)
                {
                    // download existing cert
                    certificateChain = await orderContext.Download(preferredChain);
                }
                else
                {
                    if (!string.IsNullOrEmpty(config.CustomCSR))
                    {

                        // read custom CSR as pem, convert to bytes/der
                        var pemString = string.Join("",
                                config.CustomCSR
                                .Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                .Where(s => !s.Contains("BEGIN ") && !s.Contains("END ")).ToArray()
                                );

                        var csrBytes = Convert.FromBase64String(pemString);

                        order = await orderContext.Finalize(csrBytes);
                    }
                    else
                    {
                        order = await orderContext.Finalize(new CsrInfo
                        {
                            CommonName = _idnMapping.GetAscii(config.PrimaryDomain),
                            RequireOcspMustStaple = config.RequireOcspMustStaple
                        }, csrKey);

                    }

                    //TODO: we can remove this as certes now provides this functionality, so we shouldn't hit the Processing state.
                    if (order.Status == OrderStatus.Processing)
                    {
                        // some CAs enter the processing state while they generate the final certificate, so we may need to check the status a few times
                        // https://tools.ietf.org/html/rfc8555#section-7.1.6

                        attempts = 5;

                        while (attempts > 0 && order.Status == OrderStatus.Processing)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(Math.Max(orderContext.RetryAfter, 2)));
                            order = await orderContext.Resource();
                            attempts--;
                        }
                    }

                    if (order.Status != OrderStatus.Valid)
                    {
                        throw new AcmeException("Failed to finalise certificate order. Final order status was " + order.Status.ToString());
                    }

                    certificateChain = await orderContext.Download(preferredChain);

                }

                var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(certificateChain.Certificate.ToDer());

                certExpiration = cert.NotAfter;
                certFriendlyName += $"{ cert.GetEffectiveDateString()} to {cert.GetExpirationDateString()}";
            }
            catch (AcmeRequestException exp)
            {
                var msg = $"Failed to finalize certificate order:  {exp.Error?.Detail}";
                log.Error(msg);

                var subproblems = "";
                if (exp.Error.Subproblems?.Any() == true)
                {
                    foreach (var sub in exp.Error.Subproblems)
                    {
                        subproblems += $"[{sub.Type}] {sub.Detail}; ";
                    }
                    msg += " [Subproblem Details] " + subproblems;
                }
                return new ProcessStepResult { ErrorMessage = msg, IsSuccess = false, Result = exp.Error };
            }
            catch (TaskCanceledException exp)
            {
                var msg = "Failed to complete certificate request because the request to the CAs ACME API timed out. API may be temporarily unavailable or inaccessible.";
                log.Error(msg);
                return new ProcessStepResult { ErrorMessage = msg, IsSuccess = false, Result = exp };
            }

            // file will be named as {expiration yyyyMMdd}_{guid} e.g. 20290301_4fd1b2ea-7b6e-4dca-b5d9-e0e7254e568b
            var certId = certExpiration.Value.ToString("yyyyMMdd") + "_" + Guid.NewGuid().ToString().Substring(0, 8);

            var domainAsPath = GetDomainAsPath(config.PrimaryDomain);

            if (config.ReusePrivateKey)
            {
                SavePrivateKey(config, csrKey);
            }

            var primaryCertOutputFile = string.Empty;

            if (DefaultCertificateFormat == "pfx" || DefaultCertificateFormat == "all")
            {
                primaryCertOutputFile = ExportFullCertPFX(certFriendlyName, pwd, csrKey, certificateChain, certId, domainAsPath, includeCleanup: true);
            }

            if (DefaultCertificateFormat == "pem" || DefaultCertificateFormat == "all")
            {
                var pemOutputFile = ExportFullCertPEM(csrKey, certificateChain, certId, domainAsPath);
                if (string.IsNullOrEmpty(primaryCertOutputFile))
                {
                    primaryCertOutputFile = pemOutputFile;
                }
            }

            return new ProcessStepResult
            {
                IsSuccess = true,
                Result = primaryCertOutputFile
            };
        }

        private string GetDomainAsPath(string domain) => domain.Replace("*", "_");

        private byte[] GetCACertsFromStore(System.Security.Cryptography.X509Certificates.StoreName storeName)
        {
            // get list of known CAs as Issuer certs from cert store
            // derived from PR idea by @pkiguy https://github.com/webprofusion/certify/pull/340

            try
            {
                var store = new System.Security.Cryptography.X509Certificates.X509Store(
                    storeName,
                    System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);

                store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
                var allCACerts = store.Certificates;

                using (var writer = new StringWriter())
                {
                    var pemWriter = new PemWriter(writer);
                    var certParser = new X509CertificateParser();

                    var certAdded = false;
                    foreach (var c in allCACerts)
                    {
                        try
                        {
                            var parsedCert = certParser.ReadCertificate(c.GetRawCertData());
                            pemWriter.WriteObject(parsedCert);
                            certAdded = true;
                        }
                        catch (Exception exp)
                        {
                            // failed to parse a cert
                            _log?.Error($"Failed to parse CA or intermediate cert: {c.FriendlyName} :: {exp}");
                        }
                    }

                    writer.Flush();

                    if (certAdded)
                    {
                        return System.Text.ASCIIEncoding.ASCII.GetBytes(writer.ToString());
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            catch (Exception exp)
            {
                _log?.Error($"CertesACMEProvider: failed to prepare CA issuer cache: {exp}");
                return null;
            }
        }

        private byte[] GetCustomCaCertsFromFileStore()
        {
            try
            {
                var customCertPemPath = Path.Combine(_settingsBaseFolder, "custom_ca_certs", "pem");
                var customCertDerPath = Path.Combine(_settingsBaseFolder, "custom_ca_certs", "der");

                var x509CertificateParser = new X509CertificateParser();

                var discoveredCerts = new List<Org.BouncyCastle.X509.X509Certificate>();

                if (System.IO.Directory.Exists(customCertPemPath))
                {

                    var files = System.IO.Directory.GetFiles(customCertPemPath);
                    foreach (var f in files)
                    {
                        try
                        {

                            var cert = x509CertificateParser.ReadCertificate(File.ReadAllBytes(f));
                            discoveredCerts.Add(cert);
                        }
                        catch
                        {
                            // failed to parse this file as a cert
                            _log?.Warning("Invalid Custom CA Cert file found in " + customCertPemPath);
                        }
                    }
                }

                if (System.IO.Directory.Exists(customCertDerPath))
                {

                    var files = System.IO.Directory.GetFiles(customCertDerPath);
                    foreach (var f in files)
                    {
                        try
                        {

                            var cert = x509CertificateParser.ReadCertificate(File.ReadAllBytes(f));
                            discoveredCerts.Add(cert);
                        }
                        catch
                        {
                            // failed to parse this file as a cert
                            _log?.Warning("Invalid Custom CA Cert file found in " + customCertDerPath);
                        }
                    }
                }

                // attempt to parse and add each cert
                using (var writer = new StringWriter())
                {
                    var pemWriter = new PemWriter(writer);

                    var certsAdded = false;
                    foreach (var c in discoveredCerts)
                    {
                        pemWriter.WriteObject(c);
                        certsAdded = true;
                    }

                    writer.Flush();

                    if (certsAdded)
                    {
                        return System.Text.Encoding.ASCII.GetBytes(writer.ToString());
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            catch (Exception exp)
            {
                _log?.Error("GetcustomCACerts: Failed to read custom ca certs. " + exp);
                return null;
            }
        }

        /// <summary>
        /// Compile cache of root and intermediate CAs which may be in use to sign certs
        /// </summary>
        private void RefreshIssuerCertCache()
        {
            try
            {
                _issuerCertCache = new List<byte[]>();

                var rootCAs = GetCACertsFromStore(System.Security.Cryptography.X509Certificates.StoreName.Root);
                if (rootCAs != null)
                {
                    _issuerCertCache.Add(rootCAs);
                }

                var intermediates = GetCACertsFromStore(System.Security.Cryptography.X509Certificates.StoreName.CertificateAuthority);
                if (intermediates != null)
                {
                    _issuerCertCache.Add(intermediates);
                }

                // custom CA roots
                var customCARoots = GetCustomCaCertsFromFileStore();
                if (customCARoots != null)
                {
                    _issuerCertCache.Add(customCARoots);
                }

                // well known CA certs
                var knownCAs = Certify.Models.CertificateAuthority.CoreCertificateAuthorities;
                foreach (var ca in knownCAs)
                {
                    foreach (var cert in ca.TrustedRoots)
                    {

                        using (TextReader textReader = new StringReader(cert.Value))
                        {
                            var pemReader = new PemReader(textReader);

                            var pemObj = pemReader.ReadPemObject();

                            var certBytes = pemObj.Content;
                            _issuerCertCache.Add(certBytes);
                        }

                    }
                }

            }
            catch (Exception)
            {
                //TODO: log
                System.Diagnostics.Debug.WriteLine("Failed to properly cache issuer certs.");
            }
        }

        private IKey LoadSavedPrivateKey(CertRequestConfig config)
        {
            try
            {
                var domainAsPath = GetDomainAsPath(config.PrimaryDomain);

                var storedPrivateKey = Path.GetFullPath(Path.Combine(new string[] { _settingsFolder, "..", "assets", domainAsPath, "privkey.pem" }));

                if (File.Exists(storedPrivateKey))
                {
                    var pem = File.ReadAllText(storedPrivateKey);
                    var key = KeyFactory.FromPem(pem);

                    return key;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                _log?.Error(ex, "Failed to load saved private key. File may have been removed, access denied or is invalid PEM");
                return null;
            }
        }

        private bool SavePrivateKey(CertRequestConfig config, IKey key)
        {
            try
            {
                var domainAsPath = GetDomainAsPath(config.PrimaryDomain);

                var storedPrivateKey = Path.GetFullPath(Path.Combine(new string[] { _settingsFolder, "..", "assets", domainAsPath, "privkey.pem" }));

                if (!File.Exists(storedPrivateKey))
                {
                    var pem = key.ToPem();
                    File.WriteAllText(storedPrivateKey, pem);

                    return true;
                }
                else
                {
                    _log?.Information("Saved private key file already exists, key file will not be modified.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log?.Error(ex, "Failed to save private key.");
                return false;
            }
        }

        private string ExportFullCertPFX(string certFriendlyName, string pwd, IKey csrKey, CertificateChain certificateChain, string certId, string primaryDomainPath, bool includeCleanup = true)
        {
            var storePath = Path.GetFullPath(Path.Combine(new string[] { _settingsFolder, "..", "assets", primaryDomainPath }));

            if (!System.IO.Directory.Exists(storePath))
            {
                System.IO.Directory.CreateDirectory(storePath);
            }
            else
            {
                // attempt old pfx asset cleanup
                if (includeCleanup)
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(storePath);

                        var pfxFiles = dirInfo.GetFiles("*.pfx")
                                             .Where(p => p.Extension == ".pfx")
                                             .OrderByDescending(f => f.CreationTimeUtc)
                                             .ToArray();

                        // keep last 3 pfx in order of most recent first
                        var pfxToKeep = pfxFiles.Take(3);

                        foreach (var file in pfxFiles)
                        {
                            // remove pfx assets not in top 3 list
                            if (!pfxToKeep.Any(f => f.FullName == file.FullName))
                            {
                                try
                                {
                                    File.Delete(file.FullName);
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }

            var pfxFile = certId + ".pfx";
            var pfxPath = Path.Combine(storePath, pfxFile);
            var failedBuildMsg = "Failed to build certificate as PFX. Check system date/time is correct and that the issuing CA is a trusted root CA on this machine (or in custom_ca_certs). :";

            byte[] pfxBytes;
            try
            {
                var pfx = certificateChain.ToPfx(csrKey);

                // attempt to build pfx cert chain using known issuers and known roots, if this fails it throws an AcmeException
                pfxBytes = pfx.Build(certFriendlyName, pwd);
                System.IO.File.WriteAllBytes(pfxPath, pfxBytes);
            }
            catch (Exception)
            {

                if (EnableUnknownCARoots)
                {
                    // unknown CA roots allowed, attempt PFX build without.
                    try
                    {
                        //pfxPath = ExportPFX_AnyRoot(certFriendlyName, pwd, csrKey, certificateChain, certId, primaryDomainPath);
                        var pfxNoChain = certificateChain.ToPfx(csrKey);
                        pfxNoChain.FullChain = false;
                        // attempt to build pfx cert chain using known issuers and known roots, if this fails it throws an AcmeException
                        pfxBytes = pfxNoChain.Build(certFriendlyName, pwd);
                        System.IO.File.WriteAllBytes(pfxPath, pfxBytes);
                        return pfxPath;
                    }
                    catch
                    {
                        // failed to build using unknown root, proceed with normal strategy for refreshing known issuer cache
                    }
                }
                // if build failed, try refreshing issuer certs
                RefreshIssuerCertCache();

                var pfx = certificateChain.ToPfx(csrKey);

                if (_issuerCertCache.Any())
                {
                    foreach (var c in _issuerCertCache)
                    {
                        pfx.AddIssuers(c);
                    }
                }

                try
                {
                    pfxBytes = pfx.Build(certFriendlyName, pwd);
                    System.IO.File.WriteAllBytes(pfxPath, pfxBytes);
                }
                catch (Exception ex)
                {
                    throw new Exception(failedBuildMsg + ex.Message);
                }
            }

            return pfxPath;

        }

        /*  private string ExportPFX_AnyRoot(string certFriendlyName, string pwd, IKey csrKey, CertificateChain certificateChain, string certId, string primaryDomainPath)
          {
              var storePath = Path.GetFullPath(Path.Combine(new string[] { _settingsFolder, "..", "assets", primaryDomainPath }));

              if (!System.IO.Directory.Exists(storePath))
              {
                  System.IO.Directory.CreateDirectory(storePath);
              }

              var pfxFile = certId + ".pfx";
              var pfxPath = Path.Combine(storePath, pfxFile);

              KeyAlgorithmProvider signatureAlgorithmProvider = new KeyAlgorithmProvider();
              var (_, keyPair) = signatureAlgorithmProvider.GetKeyPair(csrKey.ToDer());
              var certParser = new X509CertificateParser();
              var certificate = certParser.ReadCertificate(certificateChain.Certificate.ToDer());

              var store = new Org.BouncyCastle.Pkcs.Pkcs12StoreBuilder().Build();

              var entry = new Org.BouncyCastle.Pkcs.X509CertificateEntry(certificate);
              store.SetCertificateEntry(certFriendlyName, entry);

              store.SetKeyEntry(certFriendlyName, new Org.BouncyCastle.Pkcs.AsymmetricKeyEntry(keyPair.Private), new[] { entry });

              byte[] pfxBytes;
              using (var buffer = new MemoryStream())
              {
                  store.Save(buffer, pwd.ToCharArray(), new Org.BouncyCastle.Security.SecureRandom());
                  pfxBytes = buffer.ToArray();
              }

              try
              {

                  System.IO.File.WriteAllBytes(pfxPath, pfxBytes);
                  return pfxPath;
              }
              catch (Exception ex)
              {
                  throw new Exception(ex.Message);
              }
          }*/

        private string ExportFullCertPEM(IKey csrKey, CertificateChain certificateChain, string certId, string primaryDomainPath)
        {
            var storePath = Path.GetFullPath(Path.Combine(new string[] { _settingsFolder, "..", "assets", primaryDomainPath }));

            if (!System.IO.Directory.Exists(storePath))
            {
                System.IO.Directory.CreateDirectory(storePath);
            }

            if (!System.IO.Directory.Exists(storePath))
            {
                System.IO.Directory.CreateDirectory(storePath);
            }

            // output standard components: privkey.pem, fullchain.pem

            // privkey.pem
            var privateKeyPath = Path.GetFullPath(Path.Combine(new string[] { storePath, "privkey.pem" }));

            if (!File.Exists(privateKeyPath))
            {
                System.IO.File.WriteAllText(privateKeyPath, csrKey.ToPem());
            }

            // fullchain.pem - full chain without key
            var fullchainPath = Path.GetFullPath(Path.Combine(new string[] { storePath, "fullchain.pem" }));
            System.IO.File.WriteAllText(csrKey.ToPem(), certificateChain.ToPem());

            return fullchainPath;
        }

        public async Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate)
        {
            // get current PFX, extract DER bytes
            try
            {
                var pkcs = new Org.BouncyCastle.Pkcs.Pkcs12Store(File.Open(managedCertificate.CertificatePath, FileMode.Open, FileAccess.Read), "".ToCharArray());

                var certAliases = pkcs.Aliases.GetEnumerator();
                certAliases.MoveNext();

                var certEntry = pkcs.GetCertificate(certAliases.Current.ToString());
                var certificate = certEntry.Certificate;

                // revoke certificate
                var der = certificate.GetEncoded();

                try
                {
                    await _acme.HttpClient.ConsumeNonce();
                }
                catch (Exception)
                {
                    return new StatusMessage
                    {
                        IsOK = false,
                        Message = "Failed to resume communication with Certificate Authority API. Try again later."
                    };
                }

                await _acme.RevokeCertificate(der, RevocationReason.Unspecified, null);
            }
            catch (Exception exp)
            {
                return new StatusMessage { IsOK = false, Message = $"Failed to revoke certificate: {exp.Message}" };
            }

            return new StatusMessage { IsOK = true, Message = "Certificate revoked" };
        }

        public List<RegistrationItem> GetContactRegistrations()
        {
            var list = new List<RegistrationItem>();
            if (IsAccountRegistered())
            {
                list.Add(new RegistrationItem { Name = _settings.AccountEmail });
            }
            return list;
        }

        public void EnableSensitiveFileEncryption()
        {
            //FIXME: not implemented
        }

        public Task<string> GetAcmeAccountStatus() => throw new NotImplementedException();

    }
}
