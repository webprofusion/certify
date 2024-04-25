using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.ACME.Anvil;
using Certify.ACME.Anvil.Acme;
using Certify.ACME.Anvil.Acme.Resource;
using Certify.ACME.Anvil.Jws;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Models.Shared;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.X509;

namespace Certify.Providers.ACME.Anvil
{

    public class AnvilACMEProviderSettings
    {
        /// <summary>
        /// ACME Directory URI
        /// </summary>
        public string AcmeBaseUri { get; set; }

        /// <summary>
        /// Base path for general service settings
        /// </summary>
        public string ServiceSettingsBasePath { get; set; }

        /// <summary>
        /// Directory path for legacy provider settings
        /// </summary>
        public string LegacySettingsPath { get; set; }

        /// <summary>
        /// User Agent name to use in http requests
        /// </summary>
        public string UserAgentName { get; set; } = "Certify Certificate Manager";

        /// <summary>
        /// Optionally allow ACME service to use an untrusted TLS certificate, e.g. internal CAs
        /// </summary>
        public bool AllowUntrustedTls { get; set; } = false;

        /// <summary>
        /// If set, customizes the ACME retry interval for operations such as polling order status where Retry After not supported by CA
        /// </summary>
        public int DefaultACMERetryIntervalSeconds { get; set; }

        /// <summary>
        /// If true, known/trusted issuers are loaded from system and may be used in cert chain build
        /// </summary>
        public bool EnableIssuerCache { get; set; } = false;

        /// <summary>
        /// If false, cert chain build will require known/trusted roots
        /// </summary>
        public bool AllowUnknownCARoots { get; set; } = true;

    }

    /// <summary>
    /// ACME Provider using Anvil https://github.com/webprofusion/anvil which is a fork based on https://github.com/fszlin/certes
    /// </summary>
    public class AnvilACMEProvider : IACMEClientProvider
    {
        private AcmeContext _acme;

        private Uri _serviceUri = null;

        private AnvilSettings _settings = null;
        private ConcurrentDictionary<string, IOrderContext> _currentOrders;
        private IdnMapping _idnMapping = new IdnMapping();
        private DateTimeOffset _lastInitDateTime = new DateTimeOffset();
        private readonly bool _newContactUseCurrentAccountKey = false;

        private AcmeHttpClient _httpClient;
        private LoggingHandler _loggingHandler;

        private ILog _log = null;

        private List<byte[]> _issuerCertCache = new List<byte[]>();

        private ACMECompatibilityMode _compatibilityMode = ACMECompatibilityMode.Standard;

        /// <summary>
        /// Standard ms to wait before attempting to check for an attempted challenge to be validated etc (e.g. an HTTP check or DNS lookup)
        /// </summary>

        private int _operationRetryWaitMS = 3000;

        /// <summary>
        /// Default output when finalizing a certificate download: pfx (single file container), pem (multiple files), all (pfx, pem etc)
        /// </summary>
        public string DefaultCertificateFormat { get; set; } = "pfx";

        /// <summary>
        /// Cache for last retrieved copy of ACME drectory info
        /// </summary>
        private AcmeDirectoryInfo _dir;

        private AnvilACMEProviderSettings _providerSettings = null;

        public AnvilACMEProvider(AnvilACMEProviderSettings providerSettings)
        {
            _providerSettings = providerSettings;
            _serviceUri = new Uri(providerSettings.AcmeBaseUri);

            // optionally 
            if (providerSettings.DefaultACMERetryIntervalSeconds > 0)
            {
                _operationRetryWaitMS = providerSettings.DefaultACMERetryIntervalSeconds * 1000;
                if (_operationRetryWaitMS < 1000)
                {
                    _operationRetryWaitMS = 1000;
                }
                else if (_operationRetryWaitMS > 20000)
                {
                    _operationRetryWaitMS = 20000;
                }
            }
        }

        public string GetProviderName() => "Anvil";

        public string GetAcmeBaseURI() => _serviceUri?.ToString();

        /// <summary>
        /// setup the basic settings before we init the acme context
        /// </summary>
        /// <param name="acmeDirectoryUrl"></param>
        private void PreInitAcmeContext()
        {
            _lastInitDateTime = DateTimeOffset.UtcNow;

            var httpHandler = new HttpClientHandler();

            if (_providerSettings.AllowUntrustedTls)
            {
                httpHandler.ServerCertificateCustomValidationCallback = (message, certificate, chain, sslPolicyErrors) => true;
            }

            _loggingHandler = new LoggingHandler(httpHandler, _log);
            var customHttpClient = new System.Net.Http.HttpClient(_loggingHandler);

            customHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_providerSettings.UserAgentName);

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

            if (_settings?.AccountKey == null)
            {
                if (account == null)
                {
                    // if initalising without a known account, attempt to load details from storage
                    var settingsFilePath = Path.Combine(_providerSettings.LegacySettingsPath, "c-settings.json");
                    if (File.Exists(settingsFilePath))
                    {
                        var json = File.ReadAllText(settingsFilePath);
                        _settings = Newtonsoft.Json.JsonConvert.DeserializeObject<AnvilSettings>(json);
                    }
                    else
                    {
                        _settings = new AnvilSettings();
                    }

                    if (!string.IsNullOrEmpty(_settings.AccountKey))
                    {
                        if (File.Exists(Path.Combine(_providerSettings.LegacySettingsPath, "c-acc.key")))
                        {
                            //remove legacy key info
                            File.Delete(Path.Combine(_providerSettings.LegacySettingsPath, "c-acc.key"));
                        }

                        SetAcmeContextAccountKey(_settings.AccountKey);
                    }
                    else
                    {
                        // no account key in settings, check .key (legacy key file)
                        if (File.Exists(Path.Combine(_providerSettings.LegacySettingsPath, "c-acc.key")))
                        {
                            var pem = File.ReadAllText(Path.Combine(_providerSettings.LegacySettingsPath, "c-acc.key"));
                            SetAcmeContextAccountKey(pem);
                        }
                    }
                }
                else
                {
                    _settings = new AnvilSettings
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

            if (!string.IsNullOrEmpty(_settings.AccountUri))
            {
                _acme.SetAccountUri(new Uri(_settings.AccountUri));
            }

            if (_currentOrders == null)
            {
                _currentOrders = new ConcurrentDictionary<string, IOrderContext>();
            }

            RefreshIssuerCertCache();

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
            log?.Information($"Updating account {email} with certificate authority");
            try
            {
                var acc = await _acme.Account();
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
                            ID = _settings.AccountUri.Split('/').Last(),
                            AccountFingerprint = GetAccountFingerprintHex(_acme.AccountKey)
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
            catch (Exception ex)
            {
                var (exceptionHandled, abandonRequest, message, unwrappedException) = HandleAndLogAcmeException(log, ex);
                return new ActionResult<AccountDetails> { IsSuccess = false, Message = message };
            }
        }

        public async Task<ActionResult<AccountDetails>> ChangeAccountKey(ILog log, string newKeyPEM = null)
        {
            try
            {

                // allocate new key and inform LE of key change
                var newKey = KeyFactory.NewKey(KeyAlgorithm.ES256);

                if (!string.IsNullOrEmpty(newKeyPEM))
                {
                    try
                    {
                        newKey = KeyFactory.FromPem(newKeyPEM);
                    }
                    catch
                    {
                        return new ActionResult<AccountDetails>("Failed to use provide key for account rollover", false);
                    }
                }

                await _acme.ChangeKey(newKey);

                await PopulateSettingsFromCurrentAccount();

                return new ActionResult<AccountDetails>
                {
                    IsSuccess = true,
                    Message = "Completed account key rollover",
                    Result = new AccountDetails
                    {
                        AccountKey = newKey.ToPem(),
                        AccountFingerprint = GetAccountFingerprintHex(_acme.AccountKey)
                    }
                };

            }
            catch (Exception exp)
            {
                return new ActionResult<AccountDetails>($"Failed to perform account key rollover. {exp.Message}", false);
            }
        }

        private async Task PopulateSettingsFromCurrentAccount()
        {
            var pem = _acme.AccountKey.ToPem();

            _settings.AccountKey = pem;
            _settings.AccountUri = (await _acme.GetAccountUri())?.ToString();
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
                    Email = _settings.AccountEmail,
                    AccountFingerprint = GetAccountFingerprintHex(_acme.AccountKey)
                };
            }
            else
            {
                return null;
            }
        }

        public async Task<AcmeDirectoryInfo> GetAcmeDirectory()
        {
            // if we have a valid cached copy, use that
            if (_dir != null && _dir.NewOrder != null)
            {
                return _dir;
            }

            try
            {
                var tempAcmeContext = new AcmeContext(_serviceUri, null, _httpClient);

                var dir = await tempAcmeContext.GetDirectory(true);

                _dir = new AcmeDirectoryInfo
                {
                    NewAccount = dir.NewAccount,
                    NewNonce = dir.NewNonce,
                    NewOrder = dir.NewOrder,
                    RevokeCert = dir.RevokeCert,
                    KeyChange = dir.KeyChange,
                    Meta = new AcmeDirectoryInfo.AcmeDirectoryMeta
                    {
                        TermsOfService = dir.Meta?.TermsOfService,
                        Website = dir.Meta?.Website,
                        CaaIdentities = dir.Meta?.CaaIdentities,
                        ExternalAccountRequired = dir.Meta?.ExternalAccountRequired
                    },
                    RenewalInfo = dir.RenewalInfo
                };

                return _dir;
            }
            catch
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
        public async Task<ActionResult<AccountDetails>> AddNewAccountAndAcceptTOS(ILog log, string email, string eabKeyId = null, string eabKey = null, string eabKeyAlg = null, string importAccountURI = null, string importAccountKey = null)
        {

            try
            {
                if (
                    (!string.IsNullOrEmpty(importAccountURI) && string.IsNullOrEmpty(importAccountKey))
                    ||
                    (string.IsNullOrEmpty(importAccountURI) && !string.IsNullOrEmpty(importAccountKey))
                    )
                {
                    return new ActionResult<AccountDetails>("To import account details both the existing account URI and account key in PEM format are required. ", false);
                }

                if (!string.IsNullOrEmpty(importAccountURI) && !string.IsNullOrEmpty(importAccountKey))
                {
                    // use imported account details

                    try
                    {
                        SetAcmeContextAccountKey(importAccountKey);
                    }
                    catch
                    {
                        return new ActionResult<AccountDetails>("The provided account key was invalid or not supported for import. A PEM (text) format RSA or ECDA private key is required.", false);
                    }

                    _settings.AccountUri = importAccountURI;
                    _settings.AccountEmail = email;

                    _acme.SetAccountUri(new Uri(importAccountURI));

                    return new ActionResult<AccountDetails>
                    {
                        IsSuccess = true,
                        Result = new AccountDetails
                        {
                            AccountKey = _settings.AccountKey,
                            Email = _settings.AccountEmail,
                            AccountURI = _settings.AccountUri,
                            ID = _settings.AccountUri.Split('/').Last(),
                            AccountFingerprint = GetAccountFingerprintHex(_acme.AccountKey)
                        }
                    };
                }
                else
                {
                    IKey accKey = null;

                    if (_newContactUseCurrentAccountKey && !string.IsNullOrEmpty(_settings.AccountKey))
                    {
                        accKey = KeyFactory.FromPem(_settings.AccountKey);
                    }

                    // start new account context, create new account (with new key, if not enabled)
                    _acme = new AcmeContext(_serviceUri, accKey, _httpClient, accountUri: _settings.AccountUri != null ? new Uri(_settings.AccountUri) : null);

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
                            ID = _settings.AccountUri.Split('/').Last(),
                            AccountFingerprint = GetAccountFingerprintHex(_acme.AccountKey)
                        }
                    };
                }
            }
            catch (Exception exp)
            {
                log?.Error($"Failed to register account with certificate authority: {exp.Message}");
                return new ActionResult<AccountDetails> { IsSuccess = false, Message = $"Failed to register account with certificate authority: {exp.Message}" };
            }
        }

        private string GetAccountFingerprintHex(IKey accKey)
        {
            // populate account SHA256 fingerprint as hex e.g. SHA256 56:3E:CF:AE:83:CA:4D:15:B0:29:FF:1B:71:D3:BA:B9:19:81:F8:50:9B:DF:4A:D4:39:72:E2:B1:F0:B9:38:E3
            // as used by some ACME extensions
            var accountFingerprintBytes = JwsConvert.FromBase64String(accKey.Thumbprint());
            var accountFingerprintHex = $"SHA256 {BitConverter.ToString(accountFingerprintBytes).Replace("-", ":").ToUpper()}";
            return accountFingerprintHex;
        }

        private (bool exceptionHandled, bool abandonRequest, string message, Exception unwrappedException) HandleAndLogAcmeException(ILog itemLog, Exception exp)
        {
            // unwrap actual exception if required 
            if (exp.InnerException != null)
            {
                itemLog.Verbose($"{exp.Message} [{exp.GetType().Name}] ");
                return HandleAndLogAcmeException(itemLog, exp.InnerException);
            }

            if (exp is System.Net.Sockets.SocketException)
            {
                var msg = $"Communication with the Certificate Authority API failed: [{exp.GetType().Name}] {exp.Message}";
                itemLog?.Error(msg);
                return (exceptionHandled: true, abandonRequest: true, message: msg, exp);
            }

            if (exp is TaskCanceledException)
            {
                itemLog.Warning($"{exp.GetType().Name} {exp.Message}");
                return (exceptionHandled: true, abandonRequest: true, message: "Timeout while communicating with the ACME API.", exp);
            }

            if (exp is AcmeRequestException)
            {
                var err = (exp as AcmeRequestException).Error;

                itemLog?.Warning(exp.Message);

                // log error detail

                // e.g. urn:ietf:params:acme:error:userActionRequired

                if (err != null)
                {
                    var orderErrorMsg = $"{err.Type} :: {err.Detail}";

                    if (err?.Subproblems?.Any() == true)
                    {
                        orderErrorMsg += string.Join("; ", err.Subproblems.Select(a => $" [Subproblem] {a.Type} :: {a.Detail}"));
                    }

                    // Add additional explanation for common error types
                    if ((int)err.Status == 429)
                    {
                        // hit an ACME API rate limit 
                        itemLog.Warning($"Encountered a rate limit while communicating with the ACME API");
                    }
                    else if (err.Type?.EndsWith("accountDoesNotExist") == true)
                    {
                        // wrong account details, probably used staging for prod or vice versa
                        itemLog.Warning($"Attempted to use invalid account details with the ACME API");
                    }

                    return (exceptionHandled: true, abandonRequest: true, message: orderErrorMsg, exp);
                }
                else
                {
                    itemLog?.Error($"Exception not handled: {exp}");
                    return (exceptionHandled: false, abandonRequest: false, message: "Exception not handled. Please report this to Certify The Web support.", exp);
                }
            }
            else
            {
                itemLog?.Error($"Exception not handled: {exp}");
                return (exceptionHandled: false, abandonRequest: false, message: "Exception not handled. Please report this to Certify The Web support.", exp);
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
            if (DateTimeOffset.UtcNow.Subtract(_lastInitDateTime).TotalMinutes > 30)
            {
                // our acme context is stale, start a new one
                // Note: this was originally added to avoid re-using stale replay nonce values and should no longer be required
                await InitProvider(_log);
            }

            var isResumedOrder = false;
            var pendingOrder = new PendingOrder { IsPendingAuthorizations = true };

            // prepare a list of all pending authorization we need to complete, or those we have already satisfied
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

            var certificateIdentifiers = config.GetCertificateIdentifiers()
                .Select(i => new Identifier
                {
                    Value = i.IdentifierType == CertIdentifierType.Dns ? _idnMapping.GetAscii(i.Value) : i.Value,
                    Type = i.IdentifierType == CertIdentifierType.Dns ? IdentifierType.Dns :
                        i.IdentifierType == CertIdentifierType.Ip ? IdentifierType.Ip :
                        i.IdentifierType == CertIdentifierType.TnAuthList ? IdentifierType.TNAuthList :
                        IdentifierType.Dns // default
                }).ToList();

            try
            {
                IOrderContext order = null;
                var remainingAttempts = 3;
                var orderCreated = false;
                var orderAttemptAbandoned = false;
                object lastException = null;

                try
                {
                    // first check we can access the ACME API
                    try
                    {
                        _ = await _acme.GetDirectory(throwOnError: true);

                    }
                    catch (Exception exp)
                    {
                        var (exceptionHandled, abandonRequest, message, unwrappedException) = HandleAndLogAcmeException(log, exp);

                        return new PendingOrder($"Failed to begin certificate order. The CA ACME directory was not accessible: {message}");
                    }

                    try
                    {
                        await _acme.HttpClient.ConsumeNonce();
                    }
                    catch (Exception ex)
                    {
                        log.Information($"Error consuming replay none during new order. {ex.Message}");
                    }

                    // attempt to start our certificate order
                    while (!orderCreated && !orderAttemptAbandoned && remainingAttempts >= 0)
                    {
                        try
                        {
                            log.Debug($"Creating/retrieving order. Attempts remaining:{remainingAttempts}");

                            if (orderUri != null)
                            {
                                order = _acme.Order(new Uri(orderUri));
                                isResumedOrder = true;
                            }
                            else
                            {
                                isResumedOrder = false;

                                // begin new order, with optional preference for the expiry

                                DateTimeOffset? notAfter = null;
                                if (config.PreferredExpiryDays > 0)
                                {
                                    // some CAs like BuyPass reject notAfter if it includes minutes (fractions of an hour).
                                    var notAfterDate = DateTime.UtcNow.AddDays(config.PreferredExpiryDays.Value);
                                    notAfter = new DateTimeOffset(notAfterDate.Year, notAfterDate.Month, notAfterDate.Day, notAfterDate.Hour, 0, 0, TimeSpan.Zero);
                                }

                                order = await _acme.NewOrder(identifiers: certificateIdentifiers, notAfter: notAfter);
                            }

                            if (order != null)
                            {
                                orderCreated = true;
                            }
                        }
                        catch (Exception exp)
                        {
                            var (exceptionHandled, abandonRequest, message, unwrappedException) = HandleAndLogAcmeException(log, exp);

                            lastException = unwrappedException;

                            if (abandonRequest || remainingAttempts == 0)
                            {
                                log?.Error(message);

                                if (abandonRequest)
                                {
                                    orderAttemptAbandoned = true;
                                }
                            }
                            else
                            {
                                log.Warning(message);

                                // pause before next attempt
                                await Task.Delay(_operationRetryWaitMS);
                            }
                        }

                        remainingAttempts--;
                    }
                }
                catch (NullReferenceException exp)
                {
                    var msg = $"Failed to begin certificate order (account problem or API is not currently available): {exp.Message}";

                    log?.Error(msg);

                    return new PendingOrder(msg);
                }

                // if our order failed, report the final error we encountered
                if (order == null || order.Location == null)
                {
                    var msg = "";

                    if (lastException is AcmeException)
                    {
                        if (lastException is AcmeRequestException)
                        {
                            var err = (lastException as AcmeRequestException).Error;

                            msg += $"{err.Type} :: {err.Detail}";
                        }
                        else
                        {
                            msg += $"{(lastException as AcmeException).Message}";
                        }
                    }

                    if (string.IsNullOrEmpty(msg))
                    {
                        msg = "Could not begin certificate order. The specific error is unknown.";
                    }

                    return new PendingOrder("Error creating Order with Certificate Authority: " + msg);
                }

                orderUri = order.Location.ToString();

                pendingOrder.OrderUri = orderUri;

                log.Information($"{(isResumedOrder ? "Resumed" : "Created")} ACME Order: {orderUri}");

                // track current order in memory, keyed on order Uri
                try
                {
                    _currentOrders.AddOrUpdate(orderUri, order, (key, oldValue) => order);
                }
                catch (Exception)
                {
                    pendingOrder.IsFailure = true;
                    pendingOrder.FailureMessage = $"Could not begin tracking order {orderUri}. Please retry. If running in parallel consider limiting items in batch.";

                    log.Warning(pendingOrder.FailureMessage);

                    _currentOrders.TryRemove(orderUri, out var existingOrderContext);

                    return pendingOrder;
                }

                // handle order status 'Ready' if all authorizations are already valid
                var requireAuthzFetch = true;
                Order orderDetails;

                try
                {
                    orderDetails = await order.Resource();

                    if (orderDetails.Status == OrderStatus.Ready)
                    {
                        pendingOrder.IsPendingAuthorizations = false;
                        requireAuthzFetch = false;
                        log?.Information("Order is ready and valid. Auth challenges will not be re-attempted.");
                    }
                    else if (orderDetails.Status == OrderStatus.Valid)
                    {
                        pendingOrder.IsPendingAuthorizations = false;
                        requireAuthzFetch = false;
                        log?.Information("Order is already valid and cert previously issued. Auth challenges will not be re-attempted.");
                    }

                    if (_compatibilityMode == ACMECompatibilityMode.Standard)
                    {
                        if (orderDetails.Status == OrderStatus.Valid)
                        {
                            pendingOrder.IsPendingAuthorizations = false;
                            requireAuthzFetch = false;
                        }
                    }
                }
                catch (AcmeRequestException ex)
                {
                    // order may no longer be valid
                    pendingOrder.IsFailure = true;
                    pendingOrder.FailureMessage = ex.Message;

                    log.Warning($"Order {orderUri} could not be retrieved. Order may have expired or the order URI is invalid for this account.");

                    _currentOrders.TryRemove(orderUri, out var existingOrderContext);
                    return pendingOrder;
                }

                if (requireAuthzFetch)
                {
                    // get all required pending (or already valid) authorizations for this order

                    log.Verbose($"Fetching Authorizations.");

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
                            log?.Error("Failed to fetch auth challenge details from ACME API.");
                            break;
                        }

                        var challenges = new List<AuthorizationChallengeItem>();

                        // determine if we are interested in each challenge type before fetching the challenge details, some APIs hang when you fetch a validated auth
                        var includeHttp01 = true;
                        var includeDns01 = true;
                        var includeTkAuth01 = true;

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

                            if (config.Challenges?.Any(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_TKAUTH) != true)
                            {
                                includeTkAuth01 = false;
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
                                        log?.Error($"HTTP challenge has an invalid status");
                                    }

                                    challenges.Add(new AuthorizationChallengeItem
                                    {
                                        ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP,
                                        Key = httpChallenge.Token,
                                        Value = httpChallenge.KeyAuthz,
                                        ChallengeContext = httpChallenge,
                                        ResourceUri = $"http://{authzDomain.Replace("*.", "")}/.well-known/acme-challenge/{httpChallenge.Token}",
                                        ResourcePath = $".well-known\\acme-challenge\\{httpChallenge.Token}",
                                        IsValidated = (httpChallengeStatus.Status == ChallengeStatus.Valid)
                                    });
                                }
                                catch (Exception exp)
                                {
                                    var msg = $"Could not fetch http-01 challenge details from ACME server (timeout) : {exp.Message}";

                                    log?.Error(msg);

                                    _currentOrders.TryRemove(orderUri, out var existingOrderContext);
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
                                    log?.Error($"DNS challenge has an invalid status");
                                }

                                var dnsValue = _acme.AccountKey.DnsTxt(dnsChallenge.Token);
                                var dnsKey = $"_acme-challenge.{authzDomain}".Replace("*.", "");

                                challenges.Add(new AuthorizationChallengeItem
                                {
                                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                                    Key = dnsKey,
                                    Value = dnsValue,
                                    ChallengeContext = dnsChallenge,
                                    IsValidated = (dnsChallengeStatus.Status == ChallengeStatus.Valid)
                                });
                            }
                        }

                        // add TkAuth challenge (if any)
                        if (includeTkAuth01)
                        {
                            IChallengeContext tkAuthChallenge = null;

                            try
                            {
                                var c = await authContext.Challenges();
                                tkAuthChallenge = c.FirstOrDefault(ch => ch.Type == ChallengeTypes.TkAuth01);
                            }
                            catch (Exception)
                            {
                                log.Information("Could not fetch a tkauth-01 challenge for this identifier: " + authzDomain);
                            }

                            if (tkAuthChallenge != null)
                            {
                                Challenge authorityTokenChallenge;
                                if (useAuthzForChallengeStatus)
                                {
                                    // some ACME providers don't support /challenge/ to get challenge status so retrieve all from the /authz/ instead
                                    authorityTokenChallenge = allIdentifierChallenges.FirstOrDefault(c => c.Type == ChallengeTypes.TkAuth01);
                                }
                                else
                                {
                                    // fetch status directly from /challenge/ api
                                    authorityTokenChallenge = await tkAuthChallenge.Resource();
                                }

                                log.Information($"Got tkauth-01 challenge {authorityTokenChallenge.Url}");

                                if (authorityTokenChallenge.Status == ChallengeStatus.Invalid)
                                {
                                    log?.Error($"tkauth-01 challenge has an invalid status");
                                }

                                var authToken = config.AuthorityTokens.FirstOrDefault(a => CertRequestConfig.GetParsedAtc(a.Token).TkValue == authzDomain);

                                challenges.Add(new AuthorizationChallengeItem
                                {
                                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_TKAUTH,
                                    Key = authzDomain,
                                    Value = authToken?.Token,
                                    ChallengeContext = tkAuthChallenge,
                                    IsValidated = (authorityTokenChallenge.Status == ChallengeStatus.Valid)
                                });
                            }
                        }

                        // report back on the challenges we now may need to attempt
                        authzList.Add(
                         new PendingAuthorization
                         {
                             Challenges = challenges,
                             Identifier = new CertIdentifierItem
                             {
                                 Value = (authIdentifierType == IdentifierType.Dns) ? authzDomain.Trim().ToLowerInvariant() : authzDomain,
                                 IdentifierType = authIdentifierType.ToString(),
                                 IsAuthorizationPending = !challenges.Any(c => c.IsValidated) // auth is pending if we have no challenges already validated
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

                log?.Error(msg);

                _currentOrders.TryRemove(orderUri, out var existingOrderContext);
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

                var challenge = pendingAuthorization.AttemptedChallenge.ChallengeContext as IChallengeContext;
                try
                {
                    object payload = null;

                    // Authority Token challenge passes the original JWT as the payload
                    if (pendingAuthorization.AttemptedChallenge.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_TKAUTH)
                    {
                        payload = new { atc = pendingAuthorization.AttemptedChallenge.Value };
                    }

                    // submit challenge to ACME CA to validate
                    var result = await challenge.Validate(payload);

                    return new StatusMessage
                    {
                        IsOK = result.Status != ChallengeStatus.Invalid,
                        Message = "Challenge Submitted for Validation"
                    };
                }
                catch (AcmeRequestException exp)
                {
                    var msg = $"Submit Challenge failed: {exp.Error?.Detail}";

                    log?.Error(msg);

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

            // wait a couple of seconds to allow CA to complete validation
            await Task.Delay(_operationRetryWaitMS);

            var res = await authz.Resource();

            var attempts = 10;

            while (attempts > 0 && (res.Status != AuthorizationStatus.Valid && res.Status != AuthorizationStatus.Invalid))
            {
                var challenge = res.Challenges.FirstOrDefault(c => c.Type == challengeType);

                log.Information($"Waiting for the CA to validate the {challengeType} challenge response for: {res.Identifier.Value} [{challenge?.Url}]");

                res = await authz.Resource();

                attempts--;

                // if status is not yet valid or invalid, wait a sec and try again
                if (attempts > 0 && res.Status != AuthorizationStatus.Valid && res.Status != AuthorizationStatus.Invalid)
                {
                    log.Verbose("Validation not yet completed. Pausing..");
                    await Task.Delay(_operationRetryWaitMS);
                }
            }

            if (res.Status == AuthorizationStatus.Valid)
            {
                pendingAuthorization.Identifier.IsAuthorizationPending = false;
                pendingAuthorization.Identifier.Status = IdentifierStatus.Valid;
                pendingAuthorization.IsValidated = true;
            }
            else
            {

                if (attempts == 0)
                {
                    pendingAuthorization.AuthorizationError = "The CA did not respond with a valid status for this identifier authorization within the time allowed [" + res.Status.ToString() + "]";
                }

                pendingAuthorization.Identifier.Status = IdentifierStatus.Invalid;

                //determine error
                try
                {
                    var challenge = res.Challenges.FirstOrDefault(c => c.Type == challengeType);
                    if (challenge != null)
                    {
                        if (challenge.Error != null)
                        {
                            log.Verbose($"Failed auth: {challenge.Url}");
                            pendingAuthorization.AuthorizationError = $"Response from Certificate Authority: {challenge.Error.Detail} [{challenge.Error.Status} :: {challenge.Error.Type}]";
                        }
                    }
                }
                catch
                {
                    log.Warning("Failed to determine error message for failed authorization.");
                }

                pendingAuthorization.IsValidated = false;
            }

            return pendingAuthorization;
        }

        /// <summary>
        /// Once validation has completed for our requested domains we can complete the certificate
        /// request by submitting a Certificate Signing Request (CSR) to the CA
        /// </summary>
        /// <param name="log">  </param>
        /// <param name="config">  </param>
        /// <returns>  </returns>
        public async Task<ProcessStepResult> CompleteCertificateRequest(ILog log, string internalId, CertRequestConfig config, string orderId, string pwd, string preferredChain, string defaultKeyType, bool useModernPFXBuildAlgs)
        {
            if (!_currentOrders.TryGetValue(orderId, out var orderContext))
            {
                log.Warning($"Order context was not cached: {orderId}");
                // didn't have cached info
                orderContext = _acme.Order(new Uri(orderId));
            };

            // check order status, if it's not 'ready' then try a few more times before giving up
            var order = await orderContext.Resource();

            var attempts = 5;
            while (attempts > 0 && (order?.Status != OrderStatus.Ready && order?.Status != OrderStatus.Valid))
            {
                await Task.Delay(_operationRetryWaitMS);
                order = await orderContext.Resource();
                attempts--;
            }

            if (order?.Status != OrderStatus.Ready && order?.Status != OrderStatus.Valid)
            {
                return new ProcessStepResult { IsSuccess = false, ErrorMessage = "Certificate Request did not complete. Order did not reach Ready status in the time allowed.", Result = order };
            }

            // generate temp keypair for signing CSR, default to RSA 256, 2048. Popular Microsoft products such as Exchange do not support ECDSA keys
            var keyAlg = KeyAlgorithm.RS256;
            var rsaKeySize = 2048;

            // use item specific key type if set, or global default key type setting if present 
            var preferredKeyType = !string.IsNullOrEmpty(config.CSRKeyAlg) ? config.CSRKeyAlg : defaultKeyType;

            if (!string.IsNullOrEmpty(preferredKeyType))
            {
                if (preferredKeyType == StandardKeyTypes.RSA256)
                {
                    keyAlg = KeyAlgorithm.RS256;
                }
                else if (preferredKeyType == StandardKeyTypes.RSA256_3072)
                {
                    keyAlg = KeyAlgorithm.RS256;
                    rsaKeySize = 3072;
                }
                else if (preferredKeyType == StandardKeyTypes.RSA256_4096)
                {
                    keyAlg = KeyAlgorithm.RS256;
                    rsaKeySize = 4096;
                }
                else if (preferredKeyType == StandardKeyTypes.ECDSA256)
                {
                    keyAlg = KeyAlgorithm.ES256;
                }
                else if (preferredKeyType == StandardKeyTypes.ECDSA384)
                {
                    keyAlg = KeyAlgorithm.ES384;
                }
                else if (preferredKeyType == StandardKeyTypes.ECDSA521)
                {
                    keyAlg = KeyAlgorithm.ES512;
                }
            }

            IKey csrKey = null;

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

            if (csrKey == null)
            {
                csrKey = KeyFactory.NewKey(keyAlg, rsaKeySize);
            }

            var certFriendlyName = $"{config.PrimaryDomain} [Certify] ";

            // generate cert
            CertificateChain certificateChain = null;
            X509Certificate2 cert = null;

            DateTime? certExpiration = null;

            if (config.AuthorityTokens?.Any() == true)
            {
                DefaultCertificateFormat = "all";
            }

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
                        if (config.AuthorityTokens?.Any() == true)
                        {
                            // CSR is for TnAuthList
                            var csrBuilder = await orderContext.CreateCsr(csrKey);

                            foreach (var token in config.AuthorityTokens)
                            {
                                var parsedToken = CertRequestConfig.GetParsedAtc(token.Token);
                                var tokenValueBytes = CertRequestConfig.GetAuthorityTokenValueBytes(token);
                                if (tokenValueBytes != null)
                                {
                                    csrBuilder.CrlDistributionPoints.Add(new Uri(token.Crl));
                                    csrBuilder.TnAuthList.Add(tokenValueBytes);
                                }
                            }

                            var csrBytes = csrBuilder.Generate();

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
                    }

                    //Theoretically we can remove this as ACME lib now provides this functionality, so we shouldn't hit the Processing state, however we still do for some custom CAs
                    if (order.Status == OrderStatus.Processing)
                    {
                        // some CAs enter the processing state while they generate the final certificate, so we may need to check the status a few times
                        // https://tools.ietf.org/html/rfc8555#section-7.1.6

                        attempts = 10;

                        while (attempts > 0 && order.Status == OrderStatus.Processing)
                        {
                            var waitMS = _operationRetryWaitMS;
                            if (orderContext.RetryAfter > 0 && orderContext.RetryAfter < 60)
                            {
                                waitMS = orderContext.RetryAfter * 1000;
                            }

                            await Task.Delay(waitMS);

                            order = await orderContext.Resource();
                            attempts--;
                        }
                    }

                    if (order.Status != OrderStatus.Valid)
                    {
                        return new ProcessStepResult { ErrorMessage = $"The CA did not to finalise the certificate order in the allowed time. The final order status was \"{order.Status}\". Your CA can allow more time for order processing by setting the RetryAfter header in their service implementation.", IsSuccess = false };
                    }

                    certificateChain = await orderContext.Download(preferredChain);

                }

                cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(certificateChain.Certificate.ToDer());

                certExpiration = cert.NotAfter;
                certFriendlyName += $"{cert.GetEffectiveDateString()} to {cert.GetExpirationDateString()}";

            }
            catch (AcmeRequestException exp)
            {
                var msg = $"Failed to finalize certificate order:  {exp.Error?.Detail}";
                log?.Error(msg);

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
                log?.Error(msg);
                return new ProcessStepResult { ErrorMessage = msg, IsSuccess = false, Result = exp };
            }

            // file will be named as {expiration yyyyMMdd}_{guid} e.g. 20290301_4fd1b2ea-7b6e-4dca-b5d9-e0e7254e568b
            var certId = certExpiration.Value.ToString("yyyyMMdd") + "_" + Guid.NewGuid().ToString().Substring(0, 8);

            var domainAsPath = GetDomainAsPath(string.IsNullOrEmpty(config.PrimaryDomain) ? internalId : config.PrimaryDomain);

            if (config.ReusePrivateKey)
            {
                SavePrivateKey(config, csrKey);
            }

            var primaryCertOutputFile = string.Empty;

            if (DefaultCertificateFormat == "pem" || DefaultCertificateFormat == "all")
            {
                var pemOutputFile = ExportFullCertPEM(csrKey, certificateChain, domainAsPath);

                if (string.IsNullOrEmpty(primaryCertOutputFile))
                {
                    primaryCertOutputFile = pemOutputFile;
                }
            }

            try
            {
                if (DefaultCertificateFormat == "pfx" || DefaultCertificateFormat == "all")
                {
                    primaryCertOutputFile = ExportFullCertPFX(certFriendlyName, pwd, csrKey, certificateChain, certId, domainAsPath, includeCleanup: true, useModernKeyAlgorithms: useModernPFXBuildAlgs, itemLog: log);
                }
            }
            catch (Exception ex)
            {
                if (DefaultCertificateFormat == "all")
                {
                    // allow PFX build to fail but log it
                    log?.Warning($"The PFX build process failed but the overall request completed OK. This problem should be investigate and resolved if PFX output is required: {ex}");
                }
                else
                {
                    throw;
                }
            }

            return new ProcessStepResult
            {
                IsSuccess = true,
                Result = primaryCertOutputFile,
                SupportingData = cert
            };
        }

        private string GetDomainAsPath(string domain) => domain?.Replace("*", "_") ?? "";

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
                _log?.Error($"ACME Provider: failed to prepare CA issuer cache: {exp}");
                return null;
            }
        }

        private byte[] GetCustomCaCertsFromFileStore()
        {
            try
            {
                var customCertPemPath = Path.Combine(_providerSettings.ServiceSettingsBasePath, "custom_ca_certs", "pem");
                var customCertDerPath = Path.Combine(_providerSettings.ServiceSettingsBasePath, "custom_ca_certs", "der");

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
            if (_providerSettings.EnableIssuerCache)
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
                    var knownCAs = CertificateAuthority.CoreCertificateAuthorities;
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
        }

        private IKey LoadSavedPrivateKey(CertRequestConfig config)
        {
            try
            {
                var domainAsPath = GetDomainAsPath(config.PrimaryDomain);

                var storedPrivateKey = Path.GetFullPath(Path.Combine(new string[] { _providerSettings.ServiceSettingsBasePath, "assets", domainAsPath, "privkey.pem" }));

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

                var storedPrivateKey = Path.GetFullPath(Path.Combine(new string[] { _providerSettings.ServiceSettingsBasePath, "assets", domainAsPath, "privkey.pem" }));

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

        private string ExportFullCertPFX(string certFriendlyName, string pwd, IKey csrKey, CertificateChain certificateChain, string certId, string primaryDomainPath, bool includeCleanup = true, bool useModernKeyAlgorithms = false, ILog itemLog = null)
        {
            var storePath = Path.GetFullPath(Path.Combine(new string[] { _providerSettings.ServiceSettingsBasePath, "assets", primaryDomainPath }));

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

            if (useModernKeyAlgorithms)
            {
                _log?.Verbose("PFX Build: using modern algorithms");
            }
            else
            {
                _log?.Verbose("PFX Build: using legacy algorithms");
            }

            byte[] pfxBytes;

            try
            {
                var pfx = certificateChain.ToPfx(csrKey);

                // attempt to build pfx cert chain using known issuers and known roots, if this fails it throws an AcmeException
                pfxBytes = pfx.Build(certFriendlyName, pwd, useLegacyKeyAlgorithms: !useModernKeyAlgorithms, skipChainBuild: false);
                File.WriteAllBytes(pfxPath, pfxBytes);
            }
            catch (Exception)
            {
                // if build failed, try refreshing issuer certs and rebuild
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
                    pfxBytes = pfx.Build(certFriendlyName, pwd, useLegacyKeyAlgorithms: !useModernKeyAlgorithms, skipChainBuild: false);
                    File.WriteAllBytes(pfxPath, pfxBytes);
                }
                catch (Exception buildExp)
                {
                    itemLog?.Warning("Failed to build PFX using full chain, build will be attempted using end entity only: {exp}", buildExp.Message);

                    try
                    {
                        pfxBytes = pfx.Build(certFriendlyName, pwd, useLegacyKeyAlgorithms: !useModernKeyAlgorithms, skipChainBuild: true);
                        File.WriteAllBytes(pfxPath, pfxBytes);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"{failedBuildMsg} {ex.Message}");
                    }
                }
            }

            return pfxPath;

        }

        private string ExportFullCertPEM(IKey csrKey, CertificateChain certificateChain, string primaryDomainPath)
        {
            var storePath = Path.GetFullPath(Path.Combine(new string[] { _providerSettings.ServiceSettingsBasePath, "assets", primaryDomainPath }));

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
                File.WriteAllText(privateKeyPath, csrKey.ToPem());
            }

            // fullchain.pem - full chain without key
            var fullchainPath = Path.GetFullPath(Path.Combine(new string[] { storePath, "fullchain.pem" }));
            File.WriteAllText(fullchainPath, certificateChain.ToPem());

            return fullchainPath;
        }

        public async Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate)
        {
            // get current PFX, extract DER bytes
            try
            {
                var pkcs = new Org.BouncyCastle.Pkcs.Pkcs12StoreBuilder().Build();
                pkcs.Load(File.Open(managedCertificate.CertificatePath, FileMode.Open, FileAccess.Read), "".ToCharArray());

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

        public Task<string> GetAcmeAccountStatus() => throw new NotImplementedException();

        public async Task<RenewalInfo> GetRenewalInfo(string certificateId)
        {
            var info = await _acme.GetRenewalInfo(certificateId);

            if (info != null)
            {
                return new RenewalInfo
                {
                    ExplanationURL = info.ExplanationURL,
                    SuggestedWindow = new RenewalWindow
                    {
                        Start = info.SuggestedWindow.Start,
                        End = info.SuggestedWindow.End
                    }
                };
            }
            else
            {
                return null;
            }
        }

        public async Task UpdateRenewalInfo(string certificateId, bool replaced)
        {
            try
            {
                await _acme.UpdateRenewalInfo(certificateId, replaced);
            }
            catch (Exception ex)
            {
                // provider doesn't support ARI or update sent to CA failed, we fail silently because lack of ARI support is expected for many CAs
                // and the response doesn't matter to our system
#if DEBUG
                _log?.Warning($"ARI Update Renewal Info Failed [{certificateId}] {ex.Message}");
#else
                _log?.Debug($"ARI Update Renewal Info Failed [{certificateId}] {ex.Message}");
#endif
            }
        }
    }
}
