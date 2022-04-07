using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Newtonsoft.Json;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {

        /// <summary>
        /// Add identifiers to a managed cert e.g. certify add 89ccaf11-d7c4-427a-b491-9d3582835c48 test1.test.com;test2.test.com (optionally with --perform-request and 'new' instead of id)
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        internal async Task AddIdentifiers(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Not enough arguments");
                return;
            }

            var managedCertId = args[1];
            var domains = args[2]?.Split(";, ".ToCharArray());

            var performRequestNow = false;
            if (args.Contains("--perform-request"))
            {
                performRequestNow = true;
            }

            if (domains != null && domains.Any())
            {
                ManagedCertificate managedCert = null;

                if (managedCertId == "new")
                {
                    InitPlugins();

                    if (!IsRegistered())
                    {
                        Console.WriteLine("CLI automation is only available in the licensed version of this application.");
                        return;
                    }

                    // optional load a single managed certificate tempalte from json
                    ManagedCertificate templateCert = null;

                    var jsonArgIndex = Array.IndexOf(args, "--template");

                    if (jsonArgIndex != -1)
                    {

                        if (args.Length + 1 >= jsonArgIndex + 1)
                        {
                            var pathArg = args[jsonArgIndex + 1];

                            try
                            {
                                var jsonTemplate = System.IO.File.ReadAllText(pathArg);
                                templateCert = JsonConvert.DeserializeObject<ManagedCertificate>(jsonTemplate);

                            }
                            catch (Exception)
                            {
                                Console.WriteLine($"Failed to read or parse managed certificate template json. " + pathArg);
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Input file path argument is required for json template.");
                        }
                    }

                    // create a new managed cert with http validation and auto deployment
                    if (templateCert == null)
                    {
                        managedCert = new ManagedCertificate
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = domains[0],
                            IncludeInAutoRenew = true,
                            ItemType = ManagedCertificateType.SSL_ACME
                        };

                        managedCert.RequestConfig.Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>(
                                        new List<CertRequestChallengeConfig> {
                                        new CertRequestChallengeConfig {
                                            ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP
                                        }
                                    });

                        managedCert.RequestConfig.DeploymentSiteOption = DeploymentOption.Auto;
                    }
                    else
                    {
                        managedCert = templateCert.CopyAsTemplate();

                        // if no managed cert name specifed, use first domain
                        if (string.IsNullOrEmpty(managedCert.Name))
                        {
                            managedCert.Name = domains[0];
                        }
                    }
                }
                else
                {
                    // update an existing managed cert
                    managedCert = await _certifyClient.GetManagedCertificate(managedCertId);
                }

                foreach (var d in domains.Where(i => !string.IsNullOrEmpty(i)).Select(i => i.Trim().ToLower()))
                {
                    var domainOption = managedCert.DomainOptions.FirstOrDefault(o => o.Domain == d);
                    if (domainOption != null)
                    {
                        domainOption.IsSelected = true;
                    }
                    else
                    {
                        managedCert.DomainOptions.Add(new DomainOption { Domain = d, IsManualEntry = true, IsSelected = true, Type = "dns" });
                    }

                    if (!managedCert.RequestConfig.SubjectAlternativeNames.Contains(d))
                    {
                        var newSanList = managedCert.RequestConfig.SubjectAlternativeNames.ToList();
                        newSanList.Add(d);
                        managedCert.RequestConfig.SubjectAlternativeNames = newSanList.ToArray();
                    }

                }

                // check we still have a primary domain, if not selected a default one
                if (!managedCert.DomainOptions.Any(o => o.IsPrimaryDomain))
                {
                    var defaultIdentifier = managedCert.DomainOptions.FirstOrDefault(o => o.IsSelected);
                    if (defaultIdentifier != null)
                    {
                        defaultIdentifier.IsPrimaryDomain = true;
                        managedCert.RequestConfig.PrimaryDomain = defaultIdentifier.Domain;
                    }
                }

                var updatedManagedCert = await _certifyClient.UpdateManagedCertificate(managedCert);

                if (updatedManagedCert != null && performRequestNow)
                {
                    await _certifyClient.BeginCertificateRequest(updatedManagedCert.Id, true, false);
                }

            }
        }

        /// <summary>
        /// Remove identifiers from a managed cert e.g. certify remove 89ccaf11-d7c4-427a-b491-9d3582835c48 test1.test.com;test2.test.com (optionally with --perform-request)
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        internal async Task RemoveIdentifiers(string[] args)
        {

            if (args.Length < 3)
            {
                Console.WriteLine("Not enough arguments");
                return;
            }

            var managedCertId = args[1];
            var domains = args[2]?.Split(";, ".ToCharArray());

            var performRequestNow = false;
            if (args.Contains("--perform-request"))
            {
                performRequestNow = true;
            }
            var managedCert = await _certifyClient.GetManagedCertificate(managedCertId);
            if (domains != null && domains.Any())
            {
                foreach (var d in domains.Where(i => !string.IsNullOrEmpty(i)).Select(i => i.Trim().ToLower()))
                {
                    var domainOption = managedCert.DomainOptions.FirstOrDefault(o => o.Domain == d);

                    if (domainOption != null)
                    {
                        managedCert.DomainOptions.Remove(domainOption);
                    }

                    if (managedCert.RequestConfig.SubjectAlternativeNames.Contains(d))
                    {
                        // remove domain from list of subject alternative names
                        managedCert.RequestConfig.SubjectAlternativeNames = managedCert.RequestConfig.SubjectAlternativeNames.Where(i => i != d).ToArray();
                    }

                }

                // check we still have a primary domain, if not selected a default one
                if (!managedCert.DomainOptions.Any(o => o.IsPrimaryDomain))
                {
                    var defaultIdentifier = managedCert.DomainOptions.FirstOrDefault(o => o.IsSelected);
                    if (defaultIdentifier != null)
                    {
                        defaultIdentifier.IsPrimaryDomain = true;
                        managedCert.RequestConfig.PrimaryDomain = defaultIdentifier.Domain;
                    }
                }

                if (!managedCert.DomainOptions.Any(d => d.IsSelected))
                {
                    // there are no domains selected on this certificate anymore
                    managedCert.RequestConfig.PrimaryDomain = null;
                }

                if (managedCert.GetCertificateDomains().Count() == 0)
                {
                    // this managed certificate has no domains anymore. Delete it.
                    await _certifyClient.DeleteManagedCertificate(managedCert.Id);
                    Console.WriteLine("Managed certificate has no more domains, deleted.");
                }
                else
                {
                    // update managed cert and optionally begin the request
                    var updatedManagedCert = await _certifyClient.UpdateManagedCertificate(managedCert);

                    if (updatedManagedCert != null && performRequestNow)
                    {
                        await _certifyClient.BeginCertificateRequest(updatedManagedCert.Id, true, true);
                    }
                }

            }

        }

    }
}
