using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Certes.Jws;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Org.BouncyCastle.Crypto.Digests;

namespace Certify.Providers.Certes
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

    /// <summary>
    /// ACME Provider using certes https://github.com/fszlin/certes
    /// </summary>
    public class CertesACMEProvider : IACMEClientProvider, IVaultProvider
    {
        private AcmeContext _acme;
#if DEBUG
        private Uri _serviceUri = WellKnownServers.LetsEncryptStagingV2;
#else
        private Uri _serviceUri = WellKnownServers.LetsEncryptV2;
#endif

        private string _settingsFolder = null;

        private CertesSettings _settings = null;
        private Dictionary<string, IOrderContext> _currentOrders;
        private IdnMapping _idnMapping = new IdnMapping();
        private DateTime lastInitDateTime = new DateTime();

        public CertesACMEProvider(string settingsPath)
        {
            _settingsFolder = settingsPath;

            InitProvider();
        }

        public string GetProviderName()
        {
            return "Certes";
        }

        public string GetAcmeBaseURI()
        {
            return _acme.DirectoryUri.ToString();
        }

        /// <summary>
        /// Initialise provider settings, allocating account key if required
        /// </summary>
        private void InitProvider()
        {
            LoadSettings();

            if (!LoadAccountKey())
            {
                // save new account key
                _acme = new AcmeContext(_serviceUri);
                SaveAccountKey();
            }

            _currentOrders = new Dictionary<string, IOrderContext>();
        }

        /// <summary>
        /// Load provider settings or create new
        /// </summary>
        private void LoadSettings()
        {
            if (!System.IO.Directory.Exists(_settingsFolder))
            {
                System.IO.Directory.CreateDirectory(_settingsFolder);
            }

            if (System.IO.File.Exists(_settingsFolder + "\\c-settings.json"))
            {
                string json = System.IO.File.ReadAllText(_settingsFolder + "\\c-settings.json");
                _settings = Newtonsoft.Json.JsonConvert.DeserializeObject<CertesSettings>(json);
            }
            else
            {
                _settings = new CertesSettings();
            }
        }

        /// <summary>
        /// Save current provider settings
        /// </summary>
        private void SaveSettings()
        {
            System.IO.File.WriteAllText(_settingsFolder + "\\c-settings.json", Newtonsoft.Json.JsonConvert.SerializeObject(_settings));
        }

        /// <summary>
        /// Load the current account key
        /// </summary>
        /// <returns>  </returns>
        private bool LoadAccountKey()
        {
            if (System.IO.File.Exists(_settingsFolder + "\\c-acc.key"))
            {
                string pem = System.IO.File.ReadAllText(_settingsFolder + "\\c-acc.key");
                var key = KeyFactory.FromPem(pem);
                _acme = new AcmeContext(_serviceUri, key);
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Save the current account key
        /// </summary>
        /// <returns>  </returns>
        private bool SaveAccountKey(IAccountContext accountContext = null)
        {
            string pem = _acme.AccountKey.ToPem();

            _settings.AccountKey = pem;

            System.IO.File.WriteAllText(_settingsFolder + "\\c-acc.key", pem);

            if (accountContext != null)
            {
                _settings.AccountUri = accountContext.Location.ToString();

                // archive account id history
                System.IO.File.AppendAllText(
                    _settingsFolder + "\\c-acc-archive",
                    _settings.AccountUri +
                    "\r\n" +
                    (_settings.AccountEmail ?? "") +
                    "\r\n" +
                    pem
                    );
            }

            return true;
        }

        /// <summary>
        /// Determine if we have a currently registered account with the ACME CA (Let's Encrypt)
        /// </summary>
        /// <returns>  </returns>
        public bool IsAccountRegistered()
        {
            if (!String.IsNullOrEmpty(_settings.AccountEmail))
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
        private void SetAccountKey(string pem)
        {
            var accountkey = KeyFactory.FromPem(pem);

            _acme = new AcmeContext(_serviceUri, accountkey);
        }

        /// <summary>
        /// Register a new account with the ACME CA (Let's Encrypt), accepting terms and conditions
        /// </summary>
        /// <param name="log">  </param>
        /// <param name="email">  </param>
        /// <returns>  </returns>
        public async Task<bool> AddNewAccountAndAcceptTOS(ILog log, string email)
        {
            try
            {
                var account = await _acme.NewAccount(email, true);

                _settings.AccountEmail = email;

                //store account key
                SaveAccountKey(account);

                SaveSettings();

                log.Information($"Registering account {email} with certificate authority");
                return true;
            }
            catch (Exception exp)
            {
                log.Error($"Failed to register account {email} with certificate authority: {exp.Message}");
                return false;
            }
        }

        private string ComputeKeyAuthorization(IChallengeContext challenge, IKey key)
        {
            // From Certes/Acme/Challenge.cs
            var jwkThumbprintEncoded = key.Thumbprint();
            var token = challenge.Token;
            return $"{token}.{jwkThumbprintEncoded}";
        }

        private string ComputeDnsValue(IChallengeContext challenge, IKey key)
        {
            // From Certes/Acme/Challenge.cs
            var keyAuthString = ComputeKeyAuthorization(challenge, key);
            var keyAuthBytes = Encoding.UTF8.GetBytes(keyAuthString);
            var sha256 = new Sha256Digest();
            var hashed = new byte[sha256.GetDigestSize()];

            sha256.BlockUpdate(keyAuthBytes, 0, keyAuthBytes.Length);
            sha256.DoFinal(hashed, 0);

            var dnsValue = JwsConvert.ToBase64String(hashed);
            return dnsValue;
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
            if (DateTime.Now.Subtract(lastInitDateTime).TotalMinutes > 30)
            {
                // our acme context nonce may have expired (which returns "JWS has an invalid
                // anti-replay nonce") so start a new one
                InitProvider();
            }

            var pendingOrder = new PendingOrder { IsPendingAuthorizations = true };

            // prepare a list of all pending authorization we need to complete, or those we have
            // already satisfied
            var authzList = new List<PendingAuthorization>();

            //if no alternative domain specified, use the primary domain as the subject
            var domainOrders = new List<string>();

            // order all of the distinct domains in the config (primary + SAN).
            domainOrders.Add(_idnMapping.GetAscii(config.PrimaryDomain));

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

            try
            {
                IOrderContext order;

                if (orderUri != null)
                {
                    order = _acme.Order(new Uri(orderUri));
                }
                else
                {
                    order = await _acme.NewOrder(domainOrders);
                }

                if (order == null) throw new Exception("Could not create certificate order.");

                orderUri = order.Location.ToString();

                pendingOrder.OrderUri = orderUri;

                log.Information($"Created ACME Order: {orderUri}");

                // track order in memory, keyed on order Uri
                if (_currentOrders.Keys.Contains(orderUri))
                {
                    _currentOrders.Remove(orderUri);
                }

                _currentOrders.Add(orderUri, order);

                // handle order status 'Ready' if all ahtorizations are already valid
                var orderDetails = await order.Resource();
                if (orderDetails.Status == OrderStatus.Ready)
                {
                    pendingOrder.IsPendingAuthorizations = false;
                }

                // get all required pending (or already valid) authorizations for this order

                log.Verbose($"Fetching Authorizations.");

                var orderAuthorizations = await order.Authorizations();

                // get the challenges for each authorization
                foreach (IAuthorizationContext authz in orderAuthorizations)
                {
                    log.Verbose($"Fetching Authz Challenges.");

                    var allChallenges = await authz.Challenges();
                    var res = await authz.Resource();
                    string authzDomain = res.Identifier.Value;
                    if (res.Wildcard == true) authzDomain = "*." + authzDomain;

                    var challenges = new List<AuthorizationChallengeItem>();

                    // add http challenge (if any)
                    var httpChallenge = await authz.Http();
                    if (httpChallenge != null)
                    {
                        var httpChallengeStatus = await httpChallenge.Resource();

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

                    // add dns challenge (if any)
                    var dnsChallenge = await authz.Dns();
                    if (dnsChallenge != null)
                    {
                        var dnsChallengeStatus = await dnsChallenge.Resource();

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

                    // add tls-sni-01 challenge (if any)
                    /* var tls01Challenge = await authz.Challenges(;
                     if (dnsChallenge != null)
                     {
                         var dnsChallengeStatus = await dnsChallenge.Resource();

                         challenges.Add(new AuthorizationChallengeItem
                         {
                             ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                             Key = dnsChallenge.Token,
                             Value = dnsChallenge.KeyAuthz,
                             ChallengeData = dnsChallenge,
                             IsValidated = (dnsChallengeStatus.Status == ChallengeStatus.Valid)
                         });
                     }*/

                    // report back on the challenges we now may need to attempt
                    authzList.Add(
                     new PendingAuthorization
                     {
                         Challenges = challenges,
                         Identifier = new IdentifierItem
                         {
                             Dns = authzDomain,
                             IsAuthorizationPending = !challenges.Any(c => c.IsValidated) //auth is pending if we have no challenges already validated
                         },
                         AuthorizationContext = authz,
                         IsValidated = challenges.Any(c => c.IsValidated),
                         OrderUri = orderUri
                     });
                }

                pendingOrder.Authorizations = authzList;

                return pendingOrder;
            }
            catch (AcmeRequestException exp)
            {
                // failed to register one or more domain identifier with LE (invalid, rate limit or
                // CAA fail?)

                var msg = $"Failed to begin certificate order: {exp.Error?.Detail}";

                log.Error(msg);

                pendingOrder.Authorizations =
                new List<PendingAuthorization> {
                    new PendingAuthorization
                    {
                        AuthorizationError = msg,
                        IsFailure=true
                    }
               };

                return pendingOrder;
            }
        }

        private string GetExceptionMessage(Exception exp)
        {
            string msg = exp.Message;

            if (exp.InnerException != null)
            {
                if (exp.InnerException is AcmeRequestException)
                {
                    msg += ":: " + ((AcmeRequestException)exp.InnerException).Error.Detail;
                }
                else
                {
                    msg += ":: " + exp.InnerException.Message;
                }
            }

            return msg;
        }

        /// <summary>
        /// if not already validate, ask ACME CA to check we have answered the nominated challenges correctly
        /// </summary>
        /// <param name="log">  </param>
        /// <param name="challengeType">  </param>
        /// <param name="attemptedChallenge">  </param>
        /// <returns>  </returns>
        public async Task<StatusMessage> SubmitChallenge(ILog log, string challengeType, AuthorizationChallengeItem attemptedChallenge)
        {
            if (!attemptedChallenge.IsValidated)
            {
                IChallengeContext challenge = (IChallengeContext)attemptedChallenge.ChallengeData;
                try
                {
                    Challenge result = await challenge.Validate();

                    int attempts = 10;

                    while (attempts > 0 && result.Status == ChallengeStatus.Pending || result.Status == ChallengeStatus.Processing)
                    {
                        result = await challenge.Resource();
                    }

                    if (result.Status == ChallengeStatus.Valid)
                    {
                        return new StatusMessage
                        {
                            IsOK = true,
                            Message = "Submitted"
                        };
                    }
                    else
                    {
                        var challengeError = await challenge.Resource();
                        return new StatusMessage
                        {
                            IsOK = false,
                            Message = challengeError.Error?.Detail
                        };
                    }
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
            IAuthorizationContext authz = (IAuthorizationContext)pendingAuthorization.AuthorizationContext;

            var res = await authz.Resource();
            while (res.Status != AuthorizationStatus.Valid && res.Status != AuthorizationStatus.Invalid)
            {
                res = await authz.Resource();
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
                        var r = await _acme.HttpClient.Get<AcmeResponse<Challenge>>(challenge.Url);

                        pendingAuthorization.AuthorizationError = $"{r.Resource.Error.Detail} {r.Resource.Error.Status} {r.Resource.Error.Type}";
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
        public async Task<ProcessStepResult> CompleteCertificateRequest(ILog log, CertRequestConfig config, string orderId)
        {
            var orderContext = _currentOrders[orderId];

            // generate temp keypair for signing CSR
            var keyAlg = KeyAlgorithm.RS256;

            if (!string.IsNullOrEmpty(config.CSRKeyAlg))
            {
                if (config.CSRKeyAlg == "RS256") keyAlg = KeyAlgorithm.RS256;
                if (config.CSRKeyAlg == "ECDSA256") keyAlg = KeyAlgorithm.ES256;
                if (config.CSRKeyAlg == "ECDSA384") keyAlg = KeyAlgorithm.ES384;
            }

            var csrKey = KeyFactory.NewKey(keyAlg);

            var certFriendlyName = $"{config.PrimaryDomain} [Certify] ";

            // generate cert
            CertificateChain certificateChain = null;
            try
            {
                certificateChain = await orderContext.Generate(new CsrInfo
                {
                    CommonName = _idnMapping.GetAscii(config.PrimaryDomain)
                }, csrKey);

                X509Certificate2 cert = new X509Certificate2(certificateChain.Certificate.ToDer());
                certFriendlyName += $"{cert.GetEffectiveDateString()} to {cert.GetExpirationDateString()}";
            }
            catch (AcmeRequestException exp)
            {
                var msg = $"Failed to finalize certificate order:  {exp.Error?.Detail}";
                log.Error(msg);

                return new ProcessStepResult { ErrorMessage = msg, IsSuccess = false, Result = exp.Error };
            }

            var certFolderPath = _settingsFolder + "\\assets\\pfx";

            if (!System.IO.Directory.Exists(certFolderPath))
            {
                System.IO.Directory.CreateDirectory(certFolderPath);
            }

            string certFile = Guid.NewGuid().ToString() + ".pfx";
            string pfxPath = certFolderPath + "\\" + certFile;

            var pfx = certificateChain.ToPfx(csrKey);
            var pfxBytes = pfx.Build(certFriendlyName, "");

            System.IO.File.WriteAllBytes(pfxPath, pfxBytes);

            return new ProcessStepResult { IsSuccess = true, Result = pfxPath };
        }

        public async Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate)
        {
            // get current PFX, extract DER bytes
            try
            {
                var pkcs = new Org.BouncyCastle.Pkcs.Pkcs12Store(File.Open(managedCertificate.CertificatePath, FileMode.Open), "".ToCharArray());

                var certAliases = pkcs.Aliases.GetEnumerator();
                certAliases.MoveNext();

                var certEntry = pkcs.GetCertificate(certAliases.Current.ToString());
                var certificate = certEntry.Certificate;

                // revoke certificat
                var der = certificate.GetEncoded();
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
            List<RegistrationItem> list = new List<RegistrationItem>();
            if (IsAccountRegistered())
            {
                list.Add(new RegistrationItem { Name = _settings.AccountEmail });
            }
            return list;
        }

        public void DeleteContactRegistration(string id)
        {
            // do nothing for this provider
        }

        public void EnableSensitiveFileEncryption()
        {
            //FIXME: not implemented
        }
    }
}
