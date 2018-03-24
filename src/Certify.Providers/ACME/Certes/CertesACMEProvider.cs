using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Certes.Json;
using Certes.Jws;
using Certes.Pkcs;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Models.Shared;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto.Digests;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Providers.Certes
{
    public class CertesSettings
    {
        public string AccountEmail { get; set; }
    }

    public class CertesACMEProvider : ActionLogCollector, IACMEClientProvider, IVaultProvider
    {
        private AcmeContext _acme;
#if !DEBUG
        private Uri _serviceUri = WellKnownServers.LetsEncryptStagingV2;
#else
        private Uri _serviceUri = WellKnownServers.LetsEncryptV2;
#endif
        private string _settingsFolder = @"c:\programdata\certify\certes\";
        private CertesSettings _settings = null;
        private Dictionary<string, IOrderContext> _currentOrders;
        private IdnMapping _idnMapping = new IdnMapping();

        public CertesACMEProvider(string settingsPath)
        {
            _settingsFolder = settingsPath;
            LoadSettings();

            if (!LoadAccountKey())
            {
                // save new account key
                _acme = new AcmeContext(_serviceUri);
                SaveAccountKey();
            }

            _currentOrders = new Dictionary<string, IOrderContext>();
        }

        public string GetProviderName()
        {
            return "Certes";
        }

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

        private void SaveSettings()
        {
            System.IO.File.WriteAllText(_settingsFolder + "\\c-settings.json", Newtonsoft.Json.JsonConvert.SerializeObject(_settings));
        }

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

        private bool SaveAccountKey()
        {
            System.IO.File.WriteAllText(_settingsFolder + "\\c-acc.key", _acme.AccountKey.ToPem());
            return true;
        }

        public void SetAccountKey(string pem)
        {
            var accountkey = KeyFactory.FromPem(pem);

            _acme = new AcmeContext(_serviceUri, accountkey);
        }

        public async Task<bool> AddNewAccountAndAcceptTOS(string email)
        {
            try
            {
                var account = await _acme.NewAccount(email, true);
                //store account key
                SaveAccountKey();
                _settings.AccountEmail = email;
                SaveSettings();
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        private static readonly JsonSerializerSettings thumbprintSettings = JsonUtil.CreateSettings();

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

        public async Task<List<PendingAuthorization>> BeginRegistrationAndValidation(CertRequestConfig config, string domainIdentifierId, string challengeType, string domain)
        {
            // prepare a list of all pending authorization we need to complete, or those we have
            // already satisfied
            List<PendingAuthorization> authzList = new List<PendingAuthorization>();

            //if no alternative domain specified, use the primary domain as the subject
            List<String> domainOrders = new List<string>();

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
                var order = await _acme.NewOrder(domainOrders);

                // track order in memory, keyed in primary domain
                if (_currentOrders.Keys.Contains(config.PrimaryDomain))
                {
                    _currentOrders.Remove(config.PrimaryDomain);
                }

                _currentOrders.Add(config.PrimaryDomain, order);

                // get all required pending (or already valid) authorizations for this order
                var orderAuthorizations = await order.Authorizations();

                foreach (IAuthorizationContext authz in orderAuthorizations)
                {
                    var allChallenges = await authz.Challenges();
                    var res = await authz.Resource();
                    string authzDomain = res.Identifier.Value;
                    if (res.Wildcard == true) authzDomain = "*." + authzDomain;

                    List<AuthorizationChallengeItem> challenges = new List<AuthorizationChallengeItem>();

                    // add http challenge (if any)
                    var httpChallenge = await authz.Http();
                    if (httpChallenge != null)
                    {
                        var httpChallengeStatus = await httpChallenge.Resource();
                        if (httpChallengeStatus.Status == ChallengeStatus.Invalid)
                        {
                            // we need to start a new http challenge

                            //retry order
                            //return await BeginRegistrationAndValidation(config, domainIdentifierId, challengeType, domain);
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

                        if (dnsChallengeStatus.Status == ChallengeStatus.Invalid)
                        {
                            // we need to start a new http challenge
                            //await authz.Deactivate();

                            //retry order
                            //return await BeginRegistrationAndValidation(config, domainIdentifierId, challengeType, domain);
                        }

                        var dnsValue = ComputeDnsValue(dnsChallenge, _acme.AccountKey);
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
                         IsValidated = challenges.Any(c => c.IsValidated)
                     });
                }

                return authzList;
            }
            catch (Exception exp)
            {
                // failed to register the domain identifier with LE (invalid, rate limit or CAA fail?)
                LogAction("NewOrder [" + config.PrimaryDomain + "]", exp.Message);

                return new List<PendingAuthorization> {
                    new PendingAuthorization
                {
                    AuthorizationError = exp.Message
                } };
            }
        }

        public async Task<StatusMessage> SubmitChallenge(string domainIdentifierId, string challengeType, AuthorizationChallengeItem attemptedChallenge)
        {
            // if not already validate, ask ACME server to validate we have answered the required
            // challenge correctly
            if (!attemptedChallenge.IsValidated)
            {
                IChallengeContext challenge = (IChallengeContext)attemptedChallenge.ChallengeData;
                try
                {
                    var result = await challenge.Validate();

                    if (result.Status == ChallengeStatus.Valid || result.Status == ChallengeStatus.Pending)
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
                            Message = challengeError.ToString()
                        };
                    }
                }
                catch (Exception exp)
                {
                    LogAction("SubmitChallenge failed. ", exp.Message);

                    var challengeError = await challenge.Resource();

                    return new StatusMessage
                    {
                        IsOK = false,
                        Message = challengeError.ToString()
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

        public async Task<PendingAuthorization> CheckValidationCompleted(string alias, PendingAuthorization pendingAuthorization)
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

                // TODO:  return ACME error

                pendingAuthorization.Identifier.ValidationError = "Failed";
                pendingAuthorization.Identifier.ValidationErrorType = "Error";
                pendingAuthorization.IsValidated = false;
            }
            return pendingAuthorization;
        }

        public string ComputeDomainIdentifierId(string domain)
        {
            return "ident" + Guid.NewGuid().ToString().Substring(0, 8).Replace("-", "");
        }

        public string GetAcmeBaseURI()
        {
            return _acme.DirectoryUri.ToString();
        }

        public List<string> GetActionSummary()
        {
            throw new System.NotImplementedException();
        }

        public PendingAuthorization PerformAutomatedChallengeResponse(ICertifiedServer iisManager, ManagedCertificate managedCertificate, PendingAuthorization pendingAuth)
        {
            throw new System.NotImplementedException();
        }

        public async Task<ProcessStepResult> PerformCertificateRequestProcess(string primaryDnsIdentifier, string[] alternativeDnsIdentifiers, CertRequestConfig config)
        {
            // create our new certificate
            var orderContext = _currentOrders[config.PrimaryDomain];

            //update order status
            var order = await orderContext.Resource();

            // order.Generate()

            // generate temp keypair for signing CSR var csrKey = KeyFactory.NewKey(KeyAlgorithm.RS256);
            var keyAlg = KeyAlgorithm.RS256;
            if (!String.IsNullOrEmpty(config.CSRKeyAlg))
            {
                if (config.CSRKeyAlg == "RS256") keyAlg = KeyAlgorithm.RS256;
                if (config.CSRKeyAlg == "ECDSA256") keyAlg = KeyAlgorithm.ES256;
                if (config.CSRKeyAlg == "ECDSA384") keyAlg = KeyAlgorithm.ES384;
            }

            var csrKey = KeyFactory.NewKey(keyAlg);

            var csr = new CsrInfo
            {
                CommonName = _idnMapping.GetAscii(config.PrimaryDomain)
            };

            //alternative to certes IOrderContextExtension.Finalize
            var builder = new CertificationRequestBuilder(csrKey);

            foreach (var identifier in order.Identifiers)
            {
                if (!builder.SubjectAlternativeNames.Contains(identifier.Value))
                {
                    if (config.PrimaryDomain != $"*.{identifier.Value}")
                    {
                        //only add domain to SAN if it is not derived from a wildcard domain eg test.com from *.test.com
                        builder.SubjectAlternativeNames.Add(identifier.Value);
                    }
                }
            }

            // if main request is for a wildcard domain, add that to SAN list
            if (config.PrimaryDomain.StartsWith("*."))
            {
                //add wildcard domain to san
                builder.SubjectAlternativeNames.Add(_idnMapping.GetAscii(config.PrimaryDomain));
            }
            builder.AddName("CN", _idnMapping.GetAscii(config.PrimaryDomain));

            var csrBytes = builder.Generate();

            // send our Certificate Signing Request

            Order certOrder = null;

            try
            {
                certOrder = await orderContext.Finalize(csrBytes);
            }
            catch (Exception)
            {
                // can get error on finalize if the order is already valid
                certOrder = await orderContext.Resource();

                LogAction("Order.Finalize", $"Failed to finalize order. Status: {certOrder.Status} ");
            }

            //TODO: should be iterate here until valid or invalid?

            if (certOrder.Status == OrderStatus.Valid)
            {
                // fetch our certificate info
                var certificateChain = await orderContext.Download();

                var certFriendlyName = config.PrimaryDomain + "[Certify]";
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
            else
            {
                // TODO: is cert pending or failed?
                return new ProcessStepResult { IsSuccess = false, ErrorMessage = "Certificate signing request not completed" };
            }
        }

        public Task<StatusMessage> RevokeCertificate(ManagedCertificate managedCertificate)
        {
            throw new System.NotImplementedException();
        }

        public Task<StatusMessage> TestChallengeResponse(ICertifiedServer iisManager, ManagedCertificate managedCertificate, bool isPreviewMode)
        {
            throw new System.NotImplementedException();
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

        public List<IdentifierItem> GetDomainIdentifiers()
        {
            throw new NotImplementedException();
        }

        public List<CertificateItem> GetCertificates()
        {
            throw new NotImplementedException();
        }

        public bool HasRegisteredContacts()
        {
            throw new NotImplementedException();
        }

        public void DeleteContactRegistration(string id)
        {
            // do nothing for this provider
        }

        public string GetVaultSummary()
        {
            throw new NotImplementedException();
        }

        public void EnableSensitiveFileEncryption()
        {
            throw new NotImplementedException();
        }

        public void PerformVaultCleanup()
        {
            throw new NotImplementedException();
        }
    }
}