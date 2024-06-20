/*
 * Copyright (c) 2018 THL A29 Limited, a Tencent company. All Rights Reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing,
 * software distributed under the License is distributed on an
 * "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
 * KIND, either express or implied.  See the License for the
 * specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace TencentCloud.Common.Http
{
    public class HttpConnection
    {
        private class HttpClientHolder
        {
            private static readonly ConcurrentDictionary<string, HttpClientHolder> httpclients =
                new ConcurrentDictionary<string, HttpClientHolder>();

            public static HttpClient GetClient(string proxy)
            {
                string key = string.IsNullOrEmpty(proxy) ? "" : proxy;
                HttpClientHolder result = httpclients.GetOrAdd(key, (k) => { return new HttpClientHolder(k); });
                TimeSpan timeSpan = DateTime.Now - result.createTime;

                // A new connection is created every 5 minutes
                // and old connections are discarded to avoid DNS flushing issues.
                while (timeSpan.TotalSeconds > 300)
                {
                    ICollection<KeyValuePair<string, HttpClientHolder>> kv = httpclients;
                    kv.Remove(new KeyValuePair<string, HttpClientHolder>(key, result));
                    result = httpclients.GetOrAdd(key, (k) => { return new HttpClientHolder(k); });
                    timeSpan = DateTime.Now - result.createTime;
                }

                return result.client;
            }

            public readonly HttpClient client;

            public readonly DateTime createTime;

            public HttpClientHolder(string proxy)
            {
                string p = string.IsNullOrEmpty(proxy) ? "" : proxy;
                if (p == "")
                {
                    this.client = new HttpClient();
                }
                else
                {
                    var httpClientHandler = new HttpClientHandler
                    {
                        Proxy = new WebProxy(proxy),
                    };

                    this.client = new HttpClient(handler: httpClientHandler, disposeHandler: true);
                }

                this.client.Timeout = TimeSpan.FromSeconds(60);
                this.createTime = DateTime.Now;
            }
        }

        private readonly HttpClient http;

        private readonly string baseUrl;

        private readonly string proxy;

        private readonly int timeout;

        public HttpConnection(string baseUrl, int timeout, string proxy, HttpClient http)
        {
            this.proxy = string.IsNullOrEmpty(proxy) ? "" : proxy;
            this.timeout = timeout;
            this.baseUrl = baseUrl;
            if (http != null)
            {
                this.http = http;
            }
            else
            {
                this.http = HttpClientHolder.GetClient(this.proxy);
            }
        }

        private static string AppendQuery(StringBuilder builder, Dictionary<string, string> param)
        {
            foreach (KeyValuePair<string, string> kvp in param)
            {
                builder.Append($"{WebUtility.UrlEncode(kvp.Key)}={WebUtility.UrlEncode(kvp.Value)}&");
            }

            return builder.ToString().TrimEnd('&');
        }

        public async Task<HttpResponseMessage> GetRequestAsync(string url, Dictionary<string, string> param)
        {
            StringBuilder urlBuilder = new StringBuilder($"{baseUrl.TrimEnd('/')}{url}?");
            string fullurl = AppendQuery(urlBuilder, param);
            string payload = "";
            Dictionary<string, string> headers = new Dictionary<string, string>();
            return await this.Send(HttpMethod.Get, fullurl, payload, headers).ConfigureAwait(false);
        }

        public async Task<HttpResponseMessage> GetRequestAsync(string path, string queryString,
            Dictionary<string, string> headers)
        {
            string fullurl = $"{this.baseUrl.TrimEnd('/')}{path}?{queryString}";
            string payload = "";
            return await this.Send(HttpMethod.Get, fullurl, payload, headers).ConfigureAwait(false);
        }

        public async Task<HttpResponseMessage> PostRequestAsync(string path, string payload,
            Dictionary<string, string> headers)
        {
            string fullurl = $"{baseUrl.TrimEnd('/')}{path}";
            return await this.Send(HttpMethod.Post, fullurl, payload, headers).ConfigureAwait(false);
        }

        public async Task<HttpResponseMessage> PostRequestAsync(string path, byte[] payload,
            Dictionary<string, string> headers)
        {
            string fullurl = $"{baseUrl.TrimEnd('/')}{path}";
            return await this.Send(HttpMethod.Post, fullurl, payload, headers).ConfigureAwait(false);
        }

        public async Task<HttpResponseMessage> PostRequestAsync(string url, Dictionary<string, string> param)
        {
            string fullurl = $"{this.baseUrl.TrimEnd('/')}{url}?";
            StringBuilder payloadBuilder = new StringBuilder();
            string payload = AppendQuery(payloadBuilder, param);
            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers["Content-Type"] = "application/x-www-form-urlencoded";
            return await this.Send(HttpMethod.Post, fullurl, payload, headers).ConfigureAwait(false);
        }

        private async Task<HttpResponseMessage> Send(HttpMethod method, string url, string payload,
            Dictionary<string, string> headers)
        {
            return await Send(method, url, Encoding.UTF8.GetBytes(payload), headers).ConfigureAwait(false);
        }

        private async Task<HttpResponseMessage> Send(
            HttpMethod method, string url, byte[] payload, Dictionary<string, string> headers)
        {
            using (var cts = new System.Threading.CancellationTokenSource(timeout * 1000))
            {
                using (var msg = new HttpRequestMessage(method, url))
                {
                    foreach (KeyValuePair<string, string> kvp in headers)
                    {
                        if (kvp.Key.Equals("Content-Type"))
                        {
                            ByteArrayContent content = new ByteArrayContent(payload);
                            content.Headers.Remove("Content-Type");
                            content.Headers.Add("Content-Type", kvp.Value);
                            msg.Content = content;
                        }
                        else if (kvp.Key.Equals("Host"))
                        {
                            msg.Headers.Host = kvp.Value;
                        }
                        else if (kvp.Key.Equals("Authorization"))
                        {
                            msg.Headers.Authorization = new AuthenticationHeaderValue("TC3-HMAC-SHA256",
                                kvp.Value.Substring("TC3-HMAC-SHA256".Length));
                        }
                        else
                        {
                            msg.Headers.Add(kvp.Key, kvp.Value);
                        }
                    }

                    return await http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                        .ConfigureAwait(false);
                }
            }
        }
    }
}