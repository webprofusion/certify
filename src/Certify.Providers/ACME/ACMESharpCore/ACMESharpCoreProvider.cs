using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;

namespace ACMESharpCore
{
    public class ACMESharpCoreProvider : IACMEClientProvider, IVaultProvider
    {
        public Task<bool> InitProvider(ILog log = null)
        {
            throw new System.NotImplementedException();
        }

        public Task<bool> AddNewAccountAndAcceptTOS(ILog log, string email)
        {
            throw new System.NotImplementedException();
        }

        public Task<PendingOrder> BeginCertificateOrder(ILog log, CertRequestConfig config, string orderUri = null)
        {
            throw new System.NotImplementedException();
        }

        public Task<bool> ChangeAccountKey(ILog log)
        {
            throw new NotImplementedException();
        }

        public Task<PendingAuthorization> CheckValidationCompleted(ILog log, string challengeType, PendingAuthorization pendingAuthorization)
        {
            throw new System.NotImplementedException();
        }

        public Task<ProcessStepResult> CompleteCertificateRequest(ILog log, CertRequestConfig config, string orderId)
        {
            throw new System.NotImplementedException();
        }

        public void DeleteContactRegistration(string id)
        {
            throw new System.NotImplementedException();
        }

        public void EnableSensitiveFileEncryption()
        {
            throw new System.NotImplementedException();
        }

        public Task<string> GetAcmeAccountStatus()
        {
            throw new NotImplementedException();
        }

        public string GetAcmeBaseURI()
        {
            throw new System.NotImplementedException();
        }

        public Task<Uri> GetAcmeTermsOfService()
        {
            throw new NotImplementedException();
        }

        public List<RegistrationItem> GetContactRegistrations()
        {
            throw new System.NotImplementedException();
        }

        public string GetProviderName()
        {
            throw new System.NotImplementedException();
        }

        public Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate)
        {
            throw new System.NotImplementedException();
        }

        public Task<StatusMessage> SubmitChallenge(ILog log, string challengeType, AuthorizationChallengeItem attemptedChallenge)
        {
            throw new System.NotImplementedException();
        }
    }
}
