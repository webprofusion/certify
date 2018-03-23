using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Dns;
using Certify.Locales;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Certify.Management
{
    public class NetworkUtils
    {
        private bool _enableValidationProxyAPI = true;

        public NetworkUtils(bool enableProxyValidationAPI)
        {
            _enableValidationProxyAPI = enableProxyValidationAPI;
        }

        public Action<string> Log = (message) => { };

        public bool CheckSNI(string host, string sni, bool? useProxyAPI = null)
        {
            // if validation proxy enabled, access to the domain being validated is checked via our
            // remote API rather than directly on the servers
            bool useProxy = useProxyAPI ?? _enableValidationProxyAPI;
            if (useProxy)
            {
                // TODO: check proxy here, needs server support. if successful "return true"; and "LogAction(...)"
                System.Diagnostics.Debug.WriteLine("ProxyAPI is not implemented for Checking SNI config, trying local");
                Log($"Proxy TLS SNI binding check error: {host}, {sni}");

                return CheckSNI(host, sni, false); // proxy failed, try local
            }
            var hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"https://{sni}");
                ServicePointManager.ServerCertificateValidationCallback = (obj, cert, chain, errors) =>
                {
                    // verify SNI-selected certificate is correctly configured
                    return CertificateManager.VerifyCertificateSAN(cert, sni);
                };

                // modify the hosts file so we can resolve this request locally: create an entry for
                // the primary IP address and also for 127.0.0.1 (where primary IP will not resolve
                // internally i.e. the default resolution is an external IP)

                List<string> testHostEntries = new List<string> {
                    $"\n127.0.0.1\t{sni}",
                };

                var ip = Dns.GetHostEntry(host)?.AddressList?.FirstOrDefault();
                if (ip != null)
                {
                    testHostEntries.Add($"\n{ip}\t{sni}");
                }

                using (StreamWriter writer = File.AppendText(hosts))
                {
                    foreach (var hostEntry in testHostEntries)
                    {
                        writer.Write(hostEntry);
                    }
                }
                Thread.Sleep(250); // wait a bit for hosts file to take effect

                try
                {
                    using (var client = new HttpClient())
                    {
                        var resp = client.SendAsync(req).Result;
                        // if the GET request succeeded, the Cert validation succeeded
                        Log($"Local TLS SNI binding check OK: {host}, {sni}"); ;
                    }
                }
                finally
                {
                    // clean up temp entries in hosts file
                    try
                    {
                        var txt = File.ReadAllText(hosts);
                        foreach (var hostEntry in testHostEntries)
                        {
                            //should we just remove all .acme.invalid entries instead of looking for our current entries?
                            txt = txt.Substring(0, txt.Length - hostEntry.Length);
                        }

                        File.WriteAllText(hosts, txt);
                    }
                    catch
                    {
                        // if this fails the user will have to clean up manually
                        Log($"Error cleaning up hosts file: {hosts}");
                        throw;
                    }
                }
                return true; // success!
            }
            catch (Exception ex)
            {
                // eat the error that HttpClient throws, either cert validation failed or the site is
                // inaccessible via https://host name
                Log($"Local TLS SNI binding check error: {host}, {sni}\n{ex.GetType()}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
            finally
            {
                // reset the callback for other http requests
                ServicePointManager.ServerCertificateValidationCallback = null;
            }
        }

        public async Task<bool> CheckURL(ILogger log, string url, bool? useProxyAPI = null)
        {
            // if validation proxy enabled, access to the domain being validated is checked via our
            // remote API rather than directly on the servers
            bool useProxy = useProxyAPI ?? _enableValidationProxyAPI;

            //check http request to test path works
            try
            {
                var request = WebRequest.Create(!useProxy ? url :
                    ConfigResources.APIBaseURI + "configcheck/testurl?url=" + url);

                request.Timeout = 5000;

                ServicePointManager.ServerCertificateValidationCallback = (obj, cert, chain, errors) =>
                {
                    // ignore all cert errors when validating URL response
                    return true;
                };

                log.Information("Checking URL is accessible: {url} proxyAPI: {proxyAPI}, timeoutMs: ", url, useProxy, request.Timeout);

                var response = (HttpWebResponse)await request.GetResponseAsync();

                //if checking via proxy, examine result
                if (useProxy)
                {
                    if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
                    {
                        var encoding = ASCIIEncoding.UTF8;
                        using (var reader = new System.IO.StreamReader(response.GetResponseStream(), encoding))
                        {
                            string jsonText = reader.ReadToEnd();

                            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Models.API.URLCheckResult>(jsonText);

                            if (result.IsAccessible == true)
                            {
                                log.Information("URL is accessible. Check passed.");

                                return true;
                            }
                            else
                            {
                                log.Information("(proxy api) URL is not accessible. Result: {result}", result);
                            }
                        }
                    }

                    //request failed using proxy api, request again using local http
                    return await CheckURL(log, url, false);
                }
                else
                {
                    if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
                    {
                        log.Information($"(local check) URL is accessible. Check passed. HTTP {response.StatusCode}");

                        return true;
                    }
                    else
                    {
                        log.Warning($"(local check) URL is not accessible. Check failed. HTTP {response.StatusCode}");

                        return false;
                    }
                }
            }
            catch (Exception exp)
            {
                if (useProxy)
                {
                    log.Warning(exp, "Problem checking URL is accessible : {url} ", url);

                    // failed to call proxy API (maybe offline?), let's try a local check
                    return await CheckURL(log, url, false);
                }
                else
                {
                    // failed to check URL locally
                    log.Error(exp, "Failed to confirm URL is accessible : {url} ", url);

                    return false;
                }
            }
            finally
            {
                // reset callback for other requests to validate using default behavior
                ServicePointManager.ServerCertificateValidationCallback = null;
            }
        }

        public bool CheckDNSRecordTXT(ILogger log, string domain, string record, string record_value)
        {
            bool recordExists = false;
            bool recordValueMatches = false;

            try
            {
                // check TXT
                var dn = DomainName.Parse(domain);

                var parentDomain = dn.GetParentName();
                var query = DnsClient.Default.Resolve(parentDomain, RecordType.Txt);

                if (query.AuthorityRecords[0].Name.ToString() != domain)
                {
                    //TODO: need to recursivley find authority zone name
                }

                foreach (var txtRecord in query.AnswerRecords.Where(r => r.RecordType == RecordType.Txt))
                {
                    var r = ((TxtRecord)txtRecord);
                    if (r.Name.ToString() == record)
                    {
                        recordExists = true;
                    }
                    if (String.IsNullOrEmpty(record_value))
                    {
                        recordValueMatches = true;
                    }
                    else
                    {
                        if (r.TextData == (string)record_value) recordValueMatches = true;
                    }

                    if (recordExists && recordValueMatches) return true;
                }
            }
            catch (Exception exp)
            {
                log.Error(exp, $"'{domain}' DNS Check error resolving TXT: ");
            }
            return false;
        }

        public (bool Ok, string Message) CheckDNS(ILogger log, string domain, bool? useProxyAPI = null)
        {
            // helper function to log the error then return the ValueTuple
            Func<string, (bool, string)> errorResponse = (msg) =>
            {
                msg = $"CheckDNS: {msg}\nDNS checks can be disabled in Settings if required.";
                log.Warning(msg);
                return (false, msg);
            };

            if (String.IsNullOrEmpty(domain))
            {
                return errorResponse("Cannot check null or empty DNS name.");
            }

            // if validation proxy enabled, DNS for the domain being validated is checked via our
            // remote API rather than directly on the servers
            bool useProxy = useProxyAPI ?? _enableValidationProxyAPI;

            if (useProxy)
            {
                // TODO: update proxy and implement proxy check here return (ok, message);
            }

            // check dns
            try
            {
                Dns.GetHostEntry(domain); // this throws SocketException for bad DNS
            }
            catch
            {
                return errorResponse($"'{domain}' failed to resolve to an IP Address.");
            }

            DnsMessage caa_query = null;
            DomainName dn = null;

            try
            {
                // check CAA
                dn = DomainName.Parse(domain);
                caa_query = DnsClient.Default.Resolve(dn, RecordType.CAA);
            }
            catch (Exception exp)
            {
                log.Error(exp, $"'{domain}' DNS error resolving CAA ");
            }

            if (caa_query == null || caa_query.ReturnCode != ReturnCode.NoError)
            {
                // dns lookup failed
                return errorResponse($"'{domain}' failed to parse or resolve CAA.");
            }

            if (caa_query.AnswerRecords.Where(r => r is CAARecord).Count() > 0)
            {
                // dns returned at least 1 CAA record, check for validity
                if (!caa_query.AnswerRecords.Where(r => r is CAARecord).Cast<CAARecord>()
                    .Any(r => (r.Tag == "issue" || r.Tag == "issuewild") &&
                        r.Value == "letsencrypt.org"))
                {
                    // there were no CAA records of "[flag] [tag] [value]" where [tag] = issue |
                    // issuewild and [value] = letsencrypt.org
                    // see: https://letsencrypt.org/docs/caa/
                    return errorResponse($"'{domain}' DNS CAA verification failed.");
                }
            }
            // now either there were no CAA records returned (i.e. CAA is not configured) or the CAA
            // records are correctly configured

            // note: this seems to need to run in a Task or it hangs forever when called from the WPF UI
            if (!Task.Run(async () =>
            {
                // check DNSSEC
                var dnssec = new DnsSecRecursiveDnsResolver();
                try
                {
                    var res = await dnssec.ResolveSecureAsync<ARecord>(dn);
                    return res.ValidationResult != DnsSecValidationResult.Bogus;
                }
                catch (DnsSecValidationException)
                {
                    // invalid dnssec
                    return false;
                }
                catch (Exception exp)
                {
                    // domain failed to resolve from this machine
                    log.Error(exp, $"'{domain}' DNS error resolving DnsSecRecursiveDnsResolver ");
                    return false;
                }
            }).Result)
            {
                return errorResponse($"'{domain}' DNSSEC verification failed.");
            }
            return (true, "");
        }
    }
}