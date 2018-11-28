using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Shared.Utils;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {
        public async Task ImportCSV(string[] args)
        {
            InitPlugins();

            if (!IsRegistered())
            {
                Console.WriteLine("Import is only available in the registered version of this application.");
                return;
            }

            var filename = args[args.Length - 1];

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Importing CSV: " + filename);

            var currentManagedCertificates = await _certifyClient.GetManagedCertificates(new ManagedCertificateFilter() { });
            var rows = System.IO.File.ReadAllLines(filename);
            var csvHasHeaders = false;
            int rowID = 0;

            // set default column index values
            int? siteIdIdx = 0,
                 nameIdx = 1,
                 domainsIdx = 2,
                 primaryDomainIdx = null,
                 includeInAutoRenewIdx = null,
                 performAutoConfigIdx = null,
                 performChallengeFileCopyIdx = null,
                 performExtensionlessConfigChecksIdx = null,
                 performTlsSniBindingConfigChecksIdx = null,
                 performAutomatedCertBindingIdx = null,
                 enableFailureNotificationsIdx = null,
                 preRequestPowerShellScriptIdx = null,
                 postRequestPowerShellScriptIdx = null,
                 webhookTriggerIdx = null,
                 webhookMethodIdx = null,
                 webhookUrlIdx = null,
                 webhookContentTypeIdx = null,
                 webhookContentBodyIdx = null;

            foreach (var row in rows)
            {
                try
                {
                    // does the first row contain headers in the csv file?
                    if ((rowID == 0) && row.Contains("siteid") && row.Contains("domains"))
                    {
                        csvHasHeaders = true;
                    }

                    // first row contains headers, we need to figure out the position of each column
                    if ((rowID == 0) && csvHasHeaders)
                    {
                        string[] columnTitles = row.Split(',');
                        int colID = 0;

                        foreach (var title in columnTitles)
                        {
                            // because we never know how people are going to put data in the csv,
                            // convert titles to lowercase before searching for the column index
                            var cleanTitle = title.Trim().ToLower();

                            // set the column ids
                            switch (cleanTitle)
                            {
                                case "siteid":
                                    siteIdIdx = colID;
                                    break;

                                case "name":
                                    nameIdx = colID;
                                    break;

                                case "domains":
                                    domainsIdx = colID;
                                    break;

                                case "primarydomain":
                                    primaryDomainIdx = colID;
                                    break;

                                case "includeinautorenew":
                                    includeInAutoRenewIdx = colID;
                                    break;

                                case "performautoconfig":
                                    performAutoConfigIdx = colID;
                                    break;

                                case "performchallengefilecopy":
                                    performChallengeFileCopyIdx = colID;
                                    break;

                                case "performextensionlessconfigchecks":
                                    performExtensionlessConfigChecksIdx = colID;
                                    break;

                                case "performtlssnibindingconfigchecks":
                                    performTlsSniBindingConfigChecksIdx = colID;
                                    break;

                                case "performautomatedcertbinding":
                                    performAutomatedCertBindingIdx = colID;
                                    break;

                                case "enablefailurenotifications":
                                    enableFailureNotificationsIdx = colID;
                                    break;

                                case "prerequestpowershellscript":
                                    preRequestPowerShellScriptIdx = colID;
                                    break;

                                case "postrequestpowershellscript":
                                    postRequestPowerShellScriptIdx = colID;
                                    break;

                                case "webhooktrigger":
                                    webhookTriggerIdx = colID;
                                    break;

                                case "webhookmethod":
                                    webhookMethodIdx = colID;
                                    break;

                                case "webhookurl":
                                    webhookUrlIdx = colID;
                                    break;

                                case "webhookcontenttype":
                                    webhookContentTypeIdx = colID;
                                    break;

                                case "webhookcontentbody":
                                    webhookContentBodyIdx = colID;
                                    break;
                            }

                            colID++;
                        }
                    }
                    else
                    {
                        // required fields SiteId, Name, Domain;Domain2;Domain3
                        string[] values = Regex.Split(row, @",(?![^\{]*\})"); // get all values separated by commas except those found between {}
                        string siteId = values[(int)siteIdIdx].Trim();
                        string siteName = values[(int)nameIdx].Trim();
                        string[] domains = values[(int)domainsIdx].Trim().Split(';');

                        // optional fields
                        bool IncludeInAutoRenew = true,
                             PerformAutoConfig = true,
                             PerformChallengeFileCopy = true,
                             PerformExtensionlessConfigChecks = true,
                             PerformTlsSniBindingConfigChecks = true,
                             PerformAutomatedCertBinding = true,
                             EnableFailureNotifications = true;
                        string primaryDomain = "",
                               PreRequestPowerShellScript = "",
                               PostRequestPowerShellScript = "",
                               WebhookTrigger = Webhook.ON_NONE,
                               WebhookMethod = "",
                               WebhookUrl = "",
                               WebhookContentType = "",
                               WebhookContentBody = "";

                        if (primaryDomainIdx != null) primaryDomain = values[(int)primaryDomainIdx].Trim();
                        if (includeInAutoRenewIdx != null) IncludeInAutoRenew = Convert.ToBoolean(values[(int)includeInAutoRenewIdx].Trim());
                        if (performAutoConfigIdx != null) PerformAutoConfig = Convert.ToBoolean(values[(int)performAutoConfigIdx].Trim());
                        if (performChallengeFileCopyIdx != null) PerformChallengeFileCopy = Convert.ToBoolean(values[(int)performChallengeFileCopyIdx].Trim());
                        if (performExtensionlessConfigChecksIdx != null) PerformExtensionlessConfigChecks = Convert.ToBoolean(values[(int)performExtensionlessConfigChecksIdx].Trim());
                        if (performTlsSniBindingConfigChecksIdx != null) PerformTlsSniBindingConfigChecks = Convert.ToBoolean(values[(int)performTlsSniBindingConfigChecksIdx].Trim());
                        if (performAutomatedCertBindingIdx != null) PerformAutomatedCertBinding = Convert.ToBoolean(values[(int)performAutomatedCertBindingIdx].Trim());
                        if (enableFailureNotificationsIdx != null) EnableFailureNotifications = Convert.ToBoolean(values[(int)enableFailureNotificationsIdx].Trim());
                        if (preRequestPowerShellScriptIdx != null) PreRequestPowerShellScript = values[(int)preRequestPowerShellScriptIdx].Trim();
                        if (postRequestPowerShellScriptIdx != null) PostRequestPowerShellScript = values[(int)postRequestPowerShellScriptIdx].Trim();
                        if (webhookTriggerIdx != null)
                        {
                            WebhookTrigger = values[(int)webhookTriggerIdx].Trim();

                            // the webhook trigger text is case sensitive
                            switch (WebhookTrigger.ToLower())
                            {
                                case "none":
                                    WebhookTrigger = Webhook.ON_NONE;
                                    break;

                                case "on success":
                                    WebhookTrigger = Webhook.ON_SUCCESS;
                                    break;

                                case "on error":
                                    WebhookTrigger = Webhook.ON_ERROR;
                                    break;

                                case "on success or error":
                                    WebhookTrigger = Webhook.ON_SUCCESS_OR_ERROR;
                                    break;
                            }

                            if (webhookMethodIdx != null)
                            {
                                var tmpWebhookMethod = values[(int)webhookMethodIdx].Trim();
                                WebhookMethod = tmpWebhookMethod.ToUpper();

                                if (WebhookMethod == "POST")
                                {
                                    if (webhookUrlIdx != null)
                                    {
                                        WebhookContentType = values[(int)webhookContentTypeIdx].Trim();
                                    }

                                    if (webhookContentBodyIdx != null)
                                    {
                                        WebhookContentBody = values[(int)webhookContentBodyIdx].Trim();

                                        // cleanup json values from csv conversion
                                        WebhookContentBody = Regex.Replace(WebhookContentBody, @"(""|'')|(""|'')", "");
                                        WebhookContentBody = WebhookContentBody.Replace("\"\"", "\"");
                                    }
                                }
                            }

                            if (webhookUrlIdx != null) WebhookUrl = values[(int)webhookUrlIdx].Trim();
                        }

                        var newManagedCertificate = new ManagedCertificate();
                        newManagedCertificate.Id = Guid.NewGuid().ToString();
                        newManagedCertificate.GroupId = siteId;
                        newManagedCertificate.Name = siteName;
                        newManagedCertificate.IncludeInAutoRenew = IncludeInAutoRenew;
                        newManagedCertificate.ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS;
                        newManagedCertificate.RequestConfig.Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>(
                            new List<CertRequestChallengeConfig> {
                                new CertRequestChallengeConfig {
                                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP
                            }
                        });
                        newManagedCertificate.RequestConfig.PerformAutoConfig = PerformAutoConfig;
                        newManagedCertificate.RequestConfig.PerformChallengeFileCopy = PerformChallengeFileCopy;
                        newManagedCertificate.RequestConfig.PerformExtensionlessConfigChecks = PerformExtensionlessConfigChecks;
                        newManagedCertificate.RequestConfig.PerformTlsSniBindingConfigChecks = PerformTlsSniBindingConfigChecks;
                        newManagedCertificate.RequestConfig.PerformAutomatedCertBinding = PerformAutomatedCertBinding;
                        newManagedCertificate.RequestConfig.EnableFailureNotifications = EnableFailureNotifications;
                        newManagedCertificate.RequestConfig.PreRequestPowerShellScript = PreRequestPowerShellScript;
                        newManagedCertificate.RequestConfig.PostRequestPowerShellScript = PostRequestPowerShellScript;
                        newManagedCertificate.RequestConfig.WebhookTrigger = WebhookTrigger;
                        newManagedCertificate.RequestConfig.WebhookMethod = WebhookMethod;
                        newManagedCertificate.RequestConfig.WebhookUrl = WebhookUrl;
                        newManagedCertificate.RequestConfig.WebhookContentType = WebhookContentType;
                        newManagedCertificate.RequestConfig.WebhookContentBody = WebhookContentBody;

                        bool isPrimaryDomain = true;

                        // if we have passed in a primary domain into the csv file, use that instead
                        // of the first domain in the list
                        if (primaryDomain != "")
                        {
                            isPrimaryDomain = false;
                        }

                        List<string> sans = new List<string>();
                        foreach (var d in domains)
                        {
                            if (!String.IsNullOrWhiteSpace(d))
                            {
                                var cleanDomainName = d.Trim();

                                if ((isPrimaryDomain) || (cleanDomainName == primaryDomain.Trim()))
                                {
                                    newManagedCertificate.RequestConfig.PrimaryDomain = cleanDomainName;
                                    isPrimaryDomain = true;
                                }

                                bool sanExists = false;

                                // check for existing SAN entry
                                foreach (var site in currentManagedCertificates)
                                {
                                    if (!sanExists)
                                    {
                                        var filtered = site.DomainOptions.Where(options => options.Domain == cleanDomainName);

                                        if (filtered.Count() > 0)
                                        {
                                            Console.WriteLine("Processing Row: " + rowID + " - Domain entry (" + cleanDomainName + ") already exists in certificate (" + site.Name + ")");
                                            sanExists = true;
                                        }
                                    }
                                }

                                // if the current san entry doesn't exist in our certificate list,
                                // let's add it
                                if (!sanExists)
                                {
                                    newManagedCertificate.DomainOptions.Add(new DomainOption { Domain = cleanDomainName, IsPrimaryDomain = isPrimaryDomain, IsSelected = true, Title = d });

                                    sans.Add(cleanDomainName);
                                }

                                isPrimaryDomain = false;
                            }
                        }

                        // if the new certificate to be imported has sans, then add the certificate
                        // request to the system
                        if (sans.Count() > 0)
                        {
                            newManagedCertificate.RequestConfig.SubjectAlternativeNames = sans.ToArray();

                            // add managed site
                            Console.WriteLine("Creating Managed Certificate: " + newManagedCertificate.Name);
                            await _certifyClient.UpdateManagedCertificate(newManagedCertificate);

                            // add the new certificate request to our in-memory list
                            currentManagedCertificates.Add(newManagedCertificate);
                        }
                    }
                }
                catch (Exception exp)
                {
                    Console.WriteLine("There was a problem importing row " + rowID + " - " + exp.ToString());
                }

                rowID++;
            }
        }
    }
}
