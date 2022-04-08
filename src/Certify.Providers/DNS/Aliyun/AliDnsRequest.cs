using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace Certify.Providers.DNS.Aliyun
{
    internal class AliDnsRequest
    {
        private const string DNS_SERVICE_BASE_ADDRESS = "https://alidns.aliyuncs.com";

        public static string CreateTimaStamp() => DateTime.Now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.CreateSpecificCulture("en-US"));

        private HttpMethod _httpMethod;

        public AliDnsRequest(HttpMethod httpMethod, string keyId, string secret, Dictionary<string, string> parameters)
        {
            AccessKeyId = keyId;
            AccessKeySecret = secret;
            _httpMethod = httpMethod;
            _parameters = parameters;
        }

        private Dictionary<string, string> _parameters;

        private void BuildParameters()
        {
            _parameters.Add(nameof(Format), Format.ToString().ToUpper());
            _parameters.Add(nameof(Version), Version);
            _parameters.Add(nameof(AccessKeyId), AccessKeyId);
            _parameters.Add(nameof(SignatureVersion), SignatureVersion);
            _parameters.Add(nameof(SignatureMethod), SignatureMethod);
            _parameters.Add(nameof(SignatureNonce), SignatureNonce);
            _parameters.Add(nameof(Timestamp), Timestamp);
        }

        private string PercentEncode(string value) => UpperCaseUrlEncode(value)
                .Replace("+", "%20")
                .Replace("*", "%2A")
                .Replace("%7E", "~");

        private static string UpperCaseUrlEncode(string s)
        {
            var enc = System.Web.HttpUtility.UrlEncode(s);
            if (enc == null)
            {
                return null;
            }

            var temp = enc.ToCharArray();
            for (var i = 0; i < temp.Length - 2; i++)
            {
                if (temp[i] == '%')
                {
                    temp[i + 1] = char.ToUpper(temp[i + 1]);
                    temp[i + 2] = char.ToUpper(temp[i + 2]);
                }
            }
            return new string(temp);
        }

        public void ComputeSignature()
        {
            BuildParameters();
            var sortedDictionary = new SortedDictionary<string, string>(_parameters, StringComparer.Ordinal);
            var canonicalizedQueryString = string.Join("&", sortedDictionary.Select(x => PercentEncode(x.Key) + "=" + PercentEncode(x.Value)));
            var stringToSign = _httpMethod.ToString().ToUpper() + "&%2F&" + PercentEncode(canonicalizedQueryString);
            var keyBytes = Encoding.UTF8.GetBytes(AccessKeySecret + "&");
            var hmac = new System.Security.Cryptography.HMACSHA1(keyBytes);
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
            Signature = Convert.ToBase64String(hashBytes);
            _parameters.Add(nameof(Signature), Signature);
        }

        public string GetUrl()
        {
            ComputeSignature();
            return DNS_SERVICE_BASE_ADDRESS + "?" +
                string.Join("&", _parameters.Select(x => x.Key + "=" + System.Web.HttpUtility.UrlEncode(x.Value, Encoding.UTF8)));
        }
        public string Format { get; set; } = "JSON";

        public string Version { get; } = "2015-01-09";

        public string AccessKeyId { get; set; }

        public string AccessKeySecret { get; set; }

        public string Signature { get; set; }

        public string SignatureMethod { get; } = "HMAC-SHA1";

        public string Timestamp { get; set; } = CreateTimaStamp();

        public string SignatureVersion { get; } = "1.0";

        public string SignatureNonce { get; } = Guid.NewGuid().ToString();
    }

    #region Models
    public enum RecordType
    {

        A,
        NS,
        MX,
        TXT,
        CNAME,
        SRV,
        AAAA,
        CAA,
        REDIRECT_URL,
        FORWARD_URL
    }

    public class DomainRecord
    {
        public string RequestId { get; set; }
        public string RecordId { get; set; }
    }

    public class DescribeDomainRecords
    {
        public string RequestId { get; set; }
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public Domainrecords DomainRecords { get; set; }
    }

    public class Domainrecords
    {
        public Record[] Record { get; set; }
    }

    public class Record
    {
        public string DomainName { get; set; }
        public string RecordId { get; set; }
        public string RR { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public string Line { get; set; }
        public int Priority { get; set; }
        public int TTL { get; set; }
        public string Status { get; set; }
        public bool Locked { get; set; }
    }

    public class DescribeDomainsResponse
    {
        public string RequestId { get; set; }
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public Domains Domains { get; set; }
    }

    public class Domains
    {
        public Domain[] Domain { get; set; }
    }

    public class Domain
    {
        public string DomainId { get; set; }
        public string DomainName { get; set; }
        public string AliDomain { get; set; }
        public string GroupId { get; set; }
        public string PunyCode { get; set; }
        public string InstanceId { get; set; }
        public string VersionCode { get; set; }
        public Dnsservers DnsServers { get; set; }
    }

    public class Dnsservers
    {
        public string[] DnsServer { get; set; }
    }

    #endregion
}
