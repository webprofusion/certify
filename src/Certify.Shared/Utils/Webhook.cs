using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.Shared.Utils
{
    public class Webhook
    {
        public const string ON_NONE = "None";
        public const string ON_SUCCESS = "On Success";
        public const string ON_ERROR = "On Error";
        public const string ON_SUCCESS_OR_ERROR = "On Success Or Error";

        public const string METHOD_GET = "GET";
        public const string METHOD_POST = "POST";

        public const string DEFAULT_BODY = @"{
          ""Success"": ""$Success"",
          ""PrimaryDomain"": ""$PrimaryDomain"",
          ""SANs"": ""$SubjectAlternativeNames"",
          ""ChallengeType"": ""$ChallengeType""
        }";

        public static List<string> TriggerTypes = new List<string>() { ON_NONE, ON_SUCCESS, ON_ERROR, ON_SUCCESS_OR_ERROR };
        public static List<string> Methods = new List<string>() { METHOD_GET, METHOD_POST };

        /// <summary>
        /// Sends an HTTP Request with the requested parameters 
        /// </summary>
        /// <param name="url"></param>
        /// <param name="method"></param>
        /// <param name="contentType"></param>
        /// <param name="body"></param>
        /// <returns> A named Tuple with Success boolean and int StatusCode of the HTTP Request </returns>
        public async static Task<WebhookResult> SendRequest(CertRequestConfig config, bool forSuccess)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", Management.Util.GetUserAgent() + " CertifyManager");

                HttpRequestMessage message;
                string Url = ParseValues(config.WebhookUrl, config, forSuccess, true);
                switch (config.WebhookMethod)
                {
                    case METHOD_GET:
                        message = new HttpRequestMessage(HttpMethod.Get, Url);
                        break;

                    case METHOD_POST:
                        message = new HttpRequestMessage(HttpMethod.Post, Url)
                        {
                            Content = new StringContent(
                                ParseValues(!string.IsNullOrEmpty(config.WebhookContentBody) ? config.WebhookContentBody : DEFAULT_BODY,
                                    config, forSuccess, false),
                                Encoding.UTF8,
                                string.IsNullOrEmpty(config.WebhookContentType) ? "application/json" : config.WebhookContentType)
                        };
                        break;

                    default:
                        throw new ArgumentException("Method must be GET or POST", "method");
                }
                var resp = await client.SendAsync(message);
                return new WebhookResult(resp.IsSuccessStatusCode, (int)resp.StatusCode);
            }
        }

        /// <summary>
        /// Provides templating variable replacement for Config values 
        /// </summary>
        /// <param name="template"></param>
        /// <param name="config"></param>
        /// <param name="forSuccess"></param>
        /// <returns></returns>
        private static string ParseValues(string template, CertRequestConfig config, bool forSuccess, bool url_encode)
        {
            // add all config properties to template vars
            var vars = new Dictionary<string, string>();
            foreach (var prop in config.GetType().GetProperties())
            {
                string value = prop.GetValue(config)?.ToString() ?? "";
                if (url_encode)
                {
                    value = WebUtility.UrlEncode(value);
                }
                else
                {
                    value = value.Replace(@"\", @"\\");
                }
                vars[prop.Name.ToLower()] = value;
            }

            // add special processing for these values
            vars["success"] = forSuccess ? "true" : "false";
            vars["subjectalternativenames"] = string.Join(",", config.SubjectAlternativeNames ?? new string[] { config.PrimaryDomain });

            // process the template and replace values
            return Regex.Replace(template, @"\$(\w+)(?=[\W$])", m =>
            {
                // replace var if it can be found, otherwise don't
                string key = m.Groups[1].Value.ToLower();
                return vars.ContainsKey(key) ? vars[key] : "$" + key;
            },
                RegexOptions.IgnoreCase);
        }
    }
}
