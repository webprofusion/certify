using System;
using System.Collections.Generic;
using System.Linq;
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

        public class WebhookConfig
        {
            /// <summary>
            /// The trigger for the webhook (None, Success, Error) 
            /// </summary>
            public string Trigger { get; set; } = "None";

            /// <summary>
            /// The http method for the webhook request 
            /// </summary>
            public string Method { get; set; }

            /// <summary>
            /// The http url for the webhook request 
            /// </summary>
            public string Url { get; set; }

            /// <summary>
            /// The http content type header for the webhook request 
            /// </summary>
            public string ContentType { get; set; }

            /// <summary>
            /// The http body template for the webhook request 
            /// </summary>
            public string ContentBody { get; set; }
        }

        /// <summary>
        /// Sends an HTTP Request with the requested parameters 
        /// </summary>
        /// <param name="url"></param>
        /// <param name="method"></param>
        /// <param name="contentType"></param>
        /// <param name="body"></param>
        /// <returns> A named Tuple with Success boolean and int StatusCode of the HTTP Request </returns>
        public static async Task<WebhookResult> SendRequest(WebhookConfig config, ManagedCertificate item, bool forSuccess)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", Management.Util.GetUserAgent());

                HttpRequestMessage message;

                var url = ParseValues(config.Url, item?.RequestConfig, forSuccess, true);

                switch (config.Method)
                {
                    case METHOD_GET:
                        message = new HttpRequestMessage(HttpMethod.Get, url);
                        break;

                    case METHOD_POST:
                        message = new HttpRequestMessage(HttpMethod.Post, url)
                        {
                            Content = new StringContent(
                                ParseValues(!string.IsNullOrEmpty(config.ContentBody) ? config.ContentBody : DEFAULT_BODY, item.RequestConfig, forSuccess, false),
                                Encoding.UTF8,
                                string.IsNullOrEmpty(config.ContentType) ? "application/json" : config.ContentType
                                )
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
                var objValue = prop.GetValue(config);

                var value = "";
                if (objValue != null && objValue is Array array)
                {
                    foreach (var i in array)
                    {
                        value += i.ToString() + " ";
                    }
                }
                else
                {
                    value = objValue?.ToString() ?? "";
                }

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

            // ChallengeType can be multiple values, use the first one present

            vars["challengetype"] = config.Challenges.FirstOrDefault()?.ChallengeType ?? vars["challengetype"];

            // add special processing for these values
            vars["success"] = forSuccess ? "true" : "false";
            vars["subjectalternativenames"] = string.Join(",", config.SubjectAlternativeNames ?? new string[] { config.PrimaryDomain });

            // process the template and replace values
            return Regex.Replace(template, @"\$(\w+)(?=[\W$])", m =>
            {
                // replace var if it can be found, otherwise don't
                var key = m.Groups[1].Value.ToLower();
                return vars.ContainsKey(key) ? vars[key] : "$" + key;
            },
                RegexOptions.IgnoreCase);
        }
    }
}
