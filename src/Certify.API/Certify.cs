using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models;
using Certify.Models.Config;
using Newtonsoft.Json;

namespace Certify.API
{

    public class ManagedCertificateCreateOptions
    {
        public string Title { get; set; }
        public IEnumerable<string> Domains { get; set; }

        public bool IncludeInAutoRenew { get; set; } = true;
        public ManagedCertificateCreateOptions()
        {

        }
        public ManagedCertificateCreateOptions(string title, IEnumerable<string> domains)
        {
            Title = title;
            Domains = domains;
        }
    }

    public class CertifyServerClient
    {
        ICertifyInternalApiClient _client;
        public CertifyServerClient()
        {
            _client = new Certify.Client.CertifyApiClient();
        }

        /// <summary>
        /// Request creation of a new managed certificate. This only adds to the list of managed certificates and does not perform the certificate order or deployment etc.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public async Task<ActionResult<ManagedCertificate>> CreateManagedCertificate(ManagedCertificateCreateOptions item)
        {
            if (item.Domains == null || !item.Domains.Any())
            {
                return new ActionResult<ManagedCertificate>("Managed Certificate must contain one or more domains", false);
            }

            item.Domains = item.Domains.Select(d => d.ToLowerInvariant().Trim())
                .Distinct()
                .ToList();

            var primaryDomain = item.Domains.First();
            var domainOptions = new ObservableCollection<DomainOption>(
                    item.Domains
                        .Select(d => new DomainOption { Domain = d, IsSelected = true })
                );

            domainOptions.First().IsPrimaryDomain = true;

            var managedCertificate = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = item.Title,
                IncludeInAutoRenew = true,
                ItemType = ManagedCertificateType.SSL_ACME,
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = item.Domains.First(),
                    SubjectAlternativeNames = item.Domains.ToArray(),
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

        /// <summary>
        /// Request deletion of a managed certificate by Id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Perform all pending renewal (or new managed certificates not yet requested)
        /// </summary>
        /// <returns></returns>
        public async Task<ActionResult> RenewAll()
        {
            try
            {
                _ = await _client.BeginAutoRenewal(new RenewalSettings { Mode = RenewalMode.Auto });

                return new ActionResult("In Progress", true);
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = false, Message = "Failed to perform renew all operation: " + exp.ToString() };
            }
        }


        public async Task<List<ActionStep>> PerformDeployment(string managedCertificateId, string taskId = null, bool isPreview = false)
        {
            var managedCertificates = await _client.GetManagedCertificates(new ManagedCertificateFilter { Id = managedCertificateId });

            if (managedCertificates.Count == 1)
            {
                var managedCert = managedCertificates.Single();

                if (!string.IsNullOrEmpty(taskId))
                {
                    // identify specific task
                    var task = managedCert.PostRequestTasks.FirstOrDefault(t => t.Id.ToLowerInvariant().Trim() == taskId.ToLowerInvariant().Trim());

                    if (task != null)
                    {
                        return await _client.PerformDeployment(managedCert.Id, task.Id, isPreviewOnly: false);
                    }
                    else
                    {
                        // no match, nothing to do
                        return new List<ActionStep> { };
                    }
                }
                else
                {
                    // perform all deployment tasks
                    return await _client.PerformDeployment(managedCert.Id, null, isPreviewOnly: false);
                }
            }
            else
            {

                // no matches
                return new List<ActionStep> { new ActionStep { HasError = true, Description = "Managed Certificate not found for given Id. Deployment failed." } };
            }

        }

        /// <summary>
        /// Get the Certify system version string in ActionResult.Message
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Check the API server is contactable and available
        /// </summary>
        /// <returns></returns>
        public async Task<ActionResult> IsAPIAvailable()
        {
            var result = await GetSystemVersion();

            if (result.IsSuccess)
            {
                return new ActionResult("OK", true);
            }
            else
            {
                return new ActionResult("Failed to connect. Ensure API server is running and contactable.", false);
            }

        }

    }
}
