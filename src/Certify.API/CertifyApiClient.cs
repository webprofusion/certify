using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models;
using Certify.Models.Config;
using Newtonsoft.Json;

namespace Certify.API
{

    public class CreateRequest
    {
        public string Title { get; set; }
        public IEnumerable<string> Domains { get; set; }

        public bool IncludeInAutoRenew { get; set; } = true;
        public CreateRequest()
        {

        }
        public CreateRequest(string title, IEnumerable<string> domains)
        {
            Title = title;
            Domains = domains;
        }
    }

    public class CertifyApiClient
    {
        ICertifyClient _client;
        public CertifyApiClient()
        {
            _client = new Certify.Client.CertifyServiceClient();
        }


        public async Task<ActionResult<ManagedCertificate>> CreateManagedCertificate(CreateRequest request)
        {
            if (request.Domains == null || !request.Domains.Any())
            {
                return new ActionResult<ManagedCertificate>("Certificate request must contain one or more domains", false);
            }

            request.Domains = request.Domains.Select(d => d.ToLowerInvariant().Trim())
                .Distinct()
                .ToList();

            var primaryDomain = request.Domains.First();
            var domainOptions = new ObservableCollection<DomainOption>(
                    request.Domains
                        .Select(d => new DomainOption { Domain = d, IsSelected = true })
                );

            domainOptions.First().IsPrimaryDomain = true;

            var managedCertificate = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = request.Title,
                IncludeInAutoRenew = true,
                ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS,
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = request.Domains.First(),
                    SubjectAlternativeNames = request.Domains.ToArray(),
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                            new List<CertRequestChallengeConfig>
                            {
                                new CertRequestChallengeConfig{
                                    ChallengeType="http-01"
                                }
                            }),
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    PerformChallengeFileCopy = true,
                    PerformExtensionlessConfigChecks = false
                },
                DomainOptions = domainOptions
            };

            try
            {
                var result = await _client.UpdateManagedCertificate(managedCertificate);

                if (result != null)
                {
                    return new ActionResult<ManagedCertificate> { IsSuccess = true, Message = "OK", Result = result };
                }
                else
                {
                    return new ActionResult<ManagedCertificate> { IsSuccess = false, Message = "Failed to create managed certificate." };
                }
            }
            catch (Exception exp)
            {
                return new ActionResult<ManagedCertificate> { IsSuccess = false, Message = "Failed to create managed certificate: " + exp.ToString() };
            }
        }

        public async Task<ActionResult> DeleteManagedCertificate(string id)
        {
            try
            {
                if (await _client.DeleteManagedCertificate(id))
                {
                    return new ActionResult("OK", true);
                }
                else
                {
                    return new ActionResult("Not Found", false);
                }
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = false, Message = "Failed to perform delete operation: " + exp.ToString() };
            }
        }

        public async Task<ActionResult> RenewAll()
        {
            try
            {
                _ = await _client.BeginAutoRenewal(new RenewalSettings { AutoRenewalsOnly = true });

                return new ActionResult("In Progress", true);
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = false, Message = "Failed to perform renew all operation: " + exp.ToString() };
            }
        }

        public async Task<ActionResult> GetSystemVersion()
        {
            try
            {
                var result = await _client.GetAppVersion();

                return new ActionResult(result, true);
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = false, Message = "Failed to get system version: " + exp.ToString() };
            }
        }

    }
}
