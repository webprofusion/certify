using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Certify.Shared.Core.Utils
{
    public class RdapResult
    {
        public string Identifier { get; set; }
        public DateTimeOffset? DateRegistered { get; set; }
        public DateTimeOffset? DateExpiry { get; set; }
        public DateTimeOffset? DateLastChanged { get; set; }
        public string[] Nameservers { get; set; }
        public string Status { get; set; }
        public string Error { get; set; }
        public string RawJSON { get; set; }
        public RdapResponse Response { get; set; }
    }

    public class RdapResponse
    {
        public string LdhName { get; set; }
        public string[] Status { get; set; }
        public RdapEvent[] Events { get; set; }
        public RdapNameservers[] Nameservers { get; set; }
    }

    public class RdapEvent
    {
        public string EventAction { get; set; }
        public DateTimeOffset EventDate { get; set; }
    }

    public class RdapNameservers
    {
        public string ObjectClassName { get; set; }
        public string LdhName { get; set; }
    }

    public class RdapDnsRoot
    {
        public string Description { get; set; }
        public DateTime Publication { get; set; }
        public List<List<List<string>>> Services { get; set; }
        public string Version { get; set; }
    }

    public class RdapService
    {
        private List<string> _publicSuffixList = new List<string>();

        private Hashtable _dnsRootConfig = new Hashtable();

        public async Task Init()
        {
            var list = System.IO.File.ReadAllLines("Assets\\public_suffix_list.dat");
            foreach (var line in list)
            {
                if (line.StartsWith("// ===END ICANN DOMAINS==="))
                {
                    // we don't currently use custom domains
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("//"))
                {
                    _publicSuffixList.Add(line);
                }
            }

            var rdapDnsConfig = System.IO.File.ReadAllText("Assets\\rdap_dns.json");

            var dnsRootConfig = JsonConvert.DeserializeObject<RdapDnsRoot>(rdapDnsConfig);
            foreach (var svc in dnsRootConfig.Services)
            {

                foreach (var tld in svc[0])
                {
                    _dnsRootConfig.Add(tld, svc[1][0]);
                }
            }
        }

        public string GetRdapQueryURL(string tld)
        {
            try
            {
                return _dnsRootConfig[tld].ToString();
            }
            catch
            {
                return null;
            }
        }

        public async Task<RdapResult> QueryRDAP(string domain)
        {
            // normalise domain to primary based on the tld
            var queryDomain = NormaliseDomain(domain);

            var queryTld = GetTLD(queryDomain, true);

            // find which service to query
            var queryUrl = GetRdapQueryURL(queryTld);

            if (queryUrl != null)
            {
                // query for rdap response
                
                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                try
                {
                    var json = await httpClient.GetStringAsync($"{queryUrl}/domain/{queryDomain}");
                    var result = new RdapResult { Status = "OK", RawJSON = json };
                    result.Response = JsonConvert.DeserializeObject<RdapResponse>(json);
                    result.Identifier = result.Response.LdhName;
                    result.Nameservers = result.Response.Nameservers.Select(n => n.LdhName).ToArray();
                    result.DateExpiry = result.Response.Events.FirstOrDefault(e => e.EventAction == "expiration")?.EventDate;
                    result.DateRegistered = result.Response.Events.FirstOrDefault(e => e.EventAction == "registration")?.EventDate;
                    result.DateLastChanged = result.Response.Events.FirstOrDefault(e => e.EventAction == "last changed")?.EventDate;

                    return result;
                }
                catch (Exception ex)
                {
                    // return normalised response
                    return new RdapResult { Status = "Error", Error = ex.Message };
                }
                finally
                {
                    httpClient.Dispose();
                }
            }
            else
            {
                return new RdapResult { Status = "Error", Error = "There is no RDAP Server available for the '{queryTld}' TLD" };
            }
        }

        public string GetTLD(string domain, bool shortestFirst = false)
        {
            var tldQuery = _publicSuffixList
                .Where(s => domain.ToLowerInvariant().EndsWith(s));

            if (!shortestFirst)
            {
                tldQuery = tldQuery.OrderByDescending(s => s.Length);
            }

            var tld = tldQuery.FirstOrDefault();

            return tld;
        }

        /// <summary>
        /// return base domain for a given subdomain
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        public string NormaliseDomain(string domain, bool resolveToHighestLevel = false)
        {

            domain = domain.ToLower();

            var tld = GetTLD(domain);

            if (resolveToHighestLevel)
            {
                var topTld = GetTLD(tld, shortestFirst: true);
                while (topTld != tld)
                {
                    tld = topTld;
                    topTld = GetTLD(tld);
                }
            }

            if (tld != null)
            {

                domain = domain
                    .Substring(0, domain.LastIndexOf(tld))
                    .Trim('.')
                    .Split('.')
                    .LastOrDefault();

                domain = $"{domain}.{tld}";
            }

            return domain;
        }
    }
}
