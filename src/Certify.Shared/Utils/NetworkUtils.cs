using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Certify.Management;
#if NET8_0_OR_GREATER
using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Dns;
#endif
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Shared.Core.Utils
{
    public class NetworkUtils : IDisposable
    {
        private bool _enableValidationProxyAPI = true;
        private HttpClient _httpClient = null;
        private HttpClientHandler _httpClientHandler = null;

        public NetworkUtils(bool enableProxyValidationAPI)
        {
            _enableValidationProxyAPI = enableProxyValidationAPI;

            _httpClientHandler = new HttpClientHandler();
            _httpClientHandler.ServerCertificateCustomValidationCallback =
                 (message, certificate, chain, sslPolicyErrors) => true;

            _httpClient = new HttpClient(_httpClientHandler);
            _httpClient.Timeout = new TimeSpan(0, 0, 5);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", Certify.Management.Util.GetUserAgent());

        }

        public Action<string> Log = (message) => { };

        public async Task<bool> CheckSNI(string host, string sni, bool? useProxyAPI = null)
        {
            // if validation proxy enabled, access to the domain being validated is checked via our
            // remote API rather than directly on the servers
            var useProxy = useProxyAPI ?? _enableValidationProxyAPI;

            if (useProxy)
            {
                // TODO: check proxy here, needs server support. if successful "return true"; and "LogAction(...)"
                System.Diagnostics.Debug.WriteLine("ProxyAPI is not implemented for Checking SNI config, trying local");
                Log($"Proxy TLS SNI binding check error: {host}, {sni}");

                return await CheckSNI(host, sni, false); // proxy failed, try local
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

                var testHostEntries = new List<string> {
                    $"\n127.0.0.1\t{sni}",
                };

                var ip = Dns.GetHostEntry(host)?.AddressList?.FirstOrDefault();

                if (ip != null)
                {
                    testHostEntries.Add($"\n{ip}\t{sni}");
                }

                using (var writer = File.AppendText(hosts))
                {
                    foreach (var hostEntry in testHostEntries)
                    {
                        writer.Write(hostEntry);
                    }
                }

                await Task.Delay(250); // wait a bit for hosts file to take effect

                try
                {
                    var resp = await _httpClient.SendAsync(req);
                    // if the GET request succeeded, the Cert validation succeeded
                    Log($"Local TLS SNI binding check OK: {host}, {sni}");

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

        public async Task<bool> CheckURL(ILog log, string url, bool? useProxyAPI = null)
        {
            // if validation proxy enabled, access to the domain being validated is checked via our
            // remote API rather than directly on the servers
            var useProxy = useProxyAPI ?? _enableValidationProxyAPI;

            //check http request to test path works
            try
            {

                log.Information($"Checking URL is accessible: {url} [proxyAPI: {useProxy}, timeout: {_httpClient.Timeout.TotalMilliseconds}ms]");

                var requestUrl = useProxy ? Models.API.Config.APIBaseURI + "configcheck/testurl?url=" + url : url;

                var response = await _httpClient.GetAsync(requestUrl);

                //if checking via proxy, examine result
                if (useProxy)
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var jsonText = await response.Content.ReadAsStringAsync();

                        var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Models.API.URLCheckResult>(jsonText);

                        if (result.IsAccessible == true)
                        {
                            log.Information("URL is accessible. Check passed.");

                            return true;
                        }
                        else
                        {
                            log.Information($"(proxy api) URL is not accessible. Result: [{result.StatusCode}] {result.Message}");
                        }
                    }

                    //request failed using proxy api, request again using local http
                    return await CheckURL(log, url, false);
                }
                else
                {
                    if (response.IsSuccessStatusCode)
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
                    log.Warning($"Problem checking URL is accessible : {url} {exp.Message}");

                    // failed to call proxy API (maybe offline?), let's try a local check
                    return await CheckURL(log, url, false);
                }
                else
                {
                    // failed to check URL locally
                    log.Error(exp, $"Failed to confirm URL is accessible : {url} ");

                    return false;
                }
            }
        }

#if NET8_0_OR_GREATER
        public async Task<string> GetDNSRecordTXT(ILog log, string fullyQualifiedRecordName)
        {

            try
            {
                // check TXT
                var dn = DomainName.Parse(fullyQualifiedRecordName);

                var query = await DnsClient.Default.ResolveAsync(dn, RecordType.Txt);

                foreach (var txtRecord in query.AnswerRecords.Where(r => r.RecordType == RecordType.Txt))
                {
                    var r = ((TxtRecord)txtRecord);
                    if (r.Name.ToString() == fullyQualifiedRecordName)
                    {
                        return r.TextData;
                    }
                }
            }
            catch (Exception exp)
            {
                log.Error(exp, $"'{fullyQualifiedRecordName}' DNS error resolving TXT record ");
            }

            return null;
        }
#endif

        public async Task<ActionResult> CheckServiceConnection(string hostname, int port)
        {
            using (var tcpClient = new TcpClient())
            {
                try
                {
                    await tcpClient.ConnectAsync(hostname, port);

                    return new ActionResult
                    {
                        IsSuccess = true,
                        Message = $"CheckServiceConnection: '{hostname}' responded OK on port {port} "
                    };
                }
                catch (Exception exp)
                {
                    return new ActionResult
                    {
                        IsSuccess = true,
                        Message = $"CheckServiceConnection: Failed to connect to '{hostname}' on port {port} :{exp.Message} "
                    };
                }
            }
        }

        public async Task<List<ActionResult>> CheckDNS(ILog log, string domain, bool? useProxyAPI = null, bool includeIPCheck = true)
        {
            var results = new List<ActionResult>();
#if NET8_0_OR_GREATER
            log.Information("CheckDNS: performing DNS checks. This option can be disabled in Settings if required.");

            if (string.IsNullOrEmpty(domain))
            {
                results.Add(new ActionResult { IsSuccess = false, Message = "CheckDNS: Cannot check null or empty DNS name." });
                log.Error(results.Last().Message);
                return results;
            }

            // if validation proxy enabled, DNS for the domain being validated is checked via our
            // remote API rather than directly on the servers
            var useProxy = useProxyAPI ?? _enableValidationProxyAPI;

            if (useProxy)
            {
                // TODO: update proxy and implement proxy check here return (ok, message);
            }

            // check dns resolves to IP
            if (includeIPCheck)
            {
                try
                {
                    log.Information($"Checking DNS name resolves to IP: {domain}");

                    var result = await Dns.GetHostEntryAsync(domain); // this throws SocketException for bad DNS

                    results.Add(new ActionResult
                    {
                        IsSuccess = true,
                        Message = $"CheckDNS: '{domain}' resolved to an IP Address {result.AddressList[0]}. "
                    });
                }
                catch
                {
                    results.Add(new ActionResult
                    {
                        IsSuccess = false,
                        Message = $"CheckDNS: '{domain}' failed to resolve to an IP Address. "
                    });

                    log.Error(results.Last().Message);
                    return results;
                }
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
                log.Error(exp, $"'{domain}' DNS error resolving CAA : {exp.Message}");
            }

            if (caa_query == null || caa_query.ReturnCode != ReturnCode.NoError)
            {
                // dns lookup failed

                results.Add(new ActionResult
                {
                    IsSuccess = false,
                    Message = $"CheckDNS: '{domain}' failed to parse or resolve CAA. "
                });

                log.Error(results.Last().Message);
                return results;
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

                    results.Add(new ActionResult
                    {
                        IsSuccess = false,
                        Message = $"CheckDNS: '{domain}' DNS CAA verification failed - existing CAA record prevent issuance for letsencrypt.org CA."
                    });

                    log.Warning(results.Last().Message);
                    return results;
                }
            }

            // now either there were no CAA records returned (i.e. CAA is not configured) or the CAA
            // records are correctly configured

            // check DNSSEC
            var dnssec = new DnsSecRecursiveDnsResolver();
            try
            {
                log.Information("Checking DNSSEC resolution");

                var res = await dnssec.ResolveSecureAsync<ARecord>(dn);
                var isOk = res.ValidationResult != DnsSecValidationResult.Bogus;

                if (isOk)
                {
                    results.Add(new ActionResult
                    {
                        IsSuccess = true,
                        Message = $"CheckDNS: '{domain}' DNSSEC Check OK - Validation Result: {res.ValidationResult}"
                    });
                }
                else
                {
                    results.Add(new ActionResult
                    {
                        IsSuccess = isOk,
                        Message = $"CheckDNS: '{domain}'DNSSEC Check Failed - Validation Result: {res.ValidationResult}"
                    });
                }
            }
            catch (DnsSecValidationException exp)
            {
                // invalid dnssec
                results.Add(new ActionResult
                {
                    IsSuccess = false,
                    Message = $"CheckDNS: '{domain}'DNSSEC Check Failed - {exp.Message}"
                });
                log.Warning(results.Last().Message);
            }
            catch (Exception exp)
            {
                // domain failed to resolve from this machine
                results.Add(new ActionResult
                {
                    IsSuccess = false,
                    Message = $"CheckDNS: '{domain}' DNS error resolving DnsSecRecursiveDnsResolver - {exp.Message}"
                });
            }
#endif
            return await Task.FromResult(results);
        }

        public void Dispose()
        {
            _httpClientHandler?.Dispose();
            _httpClient?.Dispose();
        }
    }
}
