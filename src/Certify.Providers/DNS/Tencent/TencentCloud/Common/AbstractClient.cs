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

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using TencentCloud.Common.Http;
using TencentCloud.Common.Profile;

namespace TencentCloud.Common
{
    public class AbstractClient
    {
        public const int HTTP_RSP_OK = 200;
        public const string SDK_VERSION = "SDK_NET_3.0.1029";

        public AbstractClient(string endpoint, string version, Credential credential, string region,
            ClientProfile profile)
        {
            this.Credential = credential;
            this.Profile = profile;
            this.Endpoint = endpoint;
            this.Region = region;
            this.Path = "/";
            this.SdkVersion = SDK_VERSION;
            this.ApiVersion = version;
            this.HttpClient = null;
        }

        /// <summary>
        /// Credentials.
        /// </summary>
        public Credential Credential { get; set; }

        /// <summary>
        /// Client profiles.
        /// </summary>
        public ClientProfile Profile { get; set; }

        /// <summary>
        /// Service endpoint, or domain name, such as productName.tencentcloudapi.com.
        /// </summary>
        public string Endpoint { get; set; }

        /// <summary>
        /// Service region, such as ap-guangzhou.
        /// </summary>
        public string Region { get; set; }

        /// <summary>
        /// URL path, for API 3.0, is /.
        /// </summary>
        public string Path { get; private set; }

        /// <summary>
        /// SDK version.
        /// </summary>
        public string SdkVersion { get; set; }

        /// <summary>
        /// API version.
        /// </summary>
        public string ApiVersion { get; set; }

        /// <summary>
        /// HttpClient.
        /// </summary>
        public HttpClient HttpClient { get; set; }

        protected async Task<string> InternalRequest(AbstractModel request, string actionName)
        {
            if ((this.Profile.HttpProfile.ReqMethod != HttpProfile.REQ_GET) &&
                (this.Profile.HttpProfile.ReqMethod != HttpProfile.REQ_POST))
            {
                throw new TencentCloudSDKException("Method only support (GET, POST)");
            }

            HttpResponseMessage response = null;
            if (ClientProfile.SIGN_SHA1.Equals(this.Profile.SignMethod)
                || ClientProfile.SIGN_SHA256.Equals(this.Profile.SignMethod))
            {
                response = await RequestV1(request, actionName).ConfigureAwait(false);
            }
            else
            {
                response = await RequestV3(request, actionName).ConfigureAwait(false);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new TencentCloudSDKException(
                    $"invalid http status: {response.StatusCode}, body: {await response.Content.ReadAsStringAsync().ConfigureAwait(false)}");
            }

            string strResp = null;
            try
            {
                strResp = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new TencentCloudSDKException("API request failed", e);
            }

            JsonResponseModel<JsonResponseErrModel> errResp = null;
            try
            {
                errResp = JsonConvert.DeserializeObject<JsonResponseModel<JsonResponseErrModel>>(strResp);
            }
            catch (JsonSerializationException e)
            {
                throw new TencentCloudSDKException(e.Message);
            }

            if (errResp.Response.Error != null)
            {
                throw new TencentCloudSDKException(
                    errResp.Response.Error.Message, errResp.Response.Error.Code, errResp.Response.RequestId);
            }

            return strResp;
        }

        protected string InternalRequestSync(AbstractModel request, string actionName)
        {
            return Task.Run(() => InternalRequest(request, actionName)).Result;
        }

        protected async Task<T> InternalRequestAsync<T>(AbstractModel request, string actionName)
        {
            if (Profile.HttpProfile.ReqMethod != HttpProfile.REQ_GET &&
                Profile.HttpProfile.ReqMethod != HttpProfile.REQ_POST)
            {
                throw new TencentCloudSDKException("Method only support (GET, POST)");
            }

            HttpResponseMessage response;
            if (ClientProfile.SIGN_SHA1.Equals(Profile.SignMethod)
                || ClientProfile.SIGN_SHA256.Equals(Profile.SignMethod))
                response = await RequestV1(request, actionName).ConfigureAwait(false);
            else
                response = await RequestV3(request, actionName).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new TencentCloudSDKException(
                    $"invalid http status: {response.StatusCode}, body: {await response.Content.ReadAsStringAsync().ConfigureAwait(false)}");
            }

            if (response.Content.Headers.TryGetValues("Content-Type", out var cts) &&
                cts.First() == "text/event-stream")
            {
                return await ReadSSEResponseAsync<T>(response).ConfigureAwait(false);
            }

            return await ReadJsonResponseAsync<T>(response).ConfigureAwait(false);
        }

        private async Task<T> ReadJsonResponseAsync<T>(HttpResponseMessage response)
        {
            string strResp = null;
            try
            {
                strResp = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new TencentCloudSDKException("API request failed", e);
            }

            JsonResponseModel<JsonResponseErrModel> errResp = null;
            try
            {
                errResp = JsonConvert.DeserializeObject<JsonResponseModel<JsonResponseErrModel>>(strResp);
            }
            catch (JsonSerializationException e)
            {
                throw new TencentCloudSDKException(e.Message);
            }

            if (errResp.Response.Error != null)
            {
                throw new TencentCloudSDKException(
                    errResp.Response.Error.Message, errResp.Response.Error.Code, errResp.Response.RequestId);
            }

            return JsonConvert.DeserializeObject<JsonResponseModel<T>>(strResp).Response;
        }

        private Task<T> ReadSSEResponseAsync<T>(HttpResponseMessage response)
        {
            if (!typeof(AbstractSSEModel).IsAssignableFrom(typeof(T)))
            {
                throw new InvalidCastException(
                    $"invalid type: {typeof(T)} is not subclass of {nameof(AbstractSSEModel)}");
            }

            var resp = Activator.CreateInstance<T>() as AbstractSSEModel;
            if (resp == null)
            {
                throw new InvalidCastException(
                    $"invalid type: {typeof(T)} is not subclass of {nameof(AbstractSSEModel)}");
            }

            resp.RequestId = response.Headers.GetValues("X-TC-RequestId").FirstOrDefault();
            resp.Response = response;

            return Task.FromResult((T)(object)resp);
        }

        private async Task<HttpResponseMessage> RequestV3(AbstractModel request, string actionName)
        {
            string canonicalQueryString = this.BuildCanonicalQueryString(request);
            byte[] requestPayload = this.BuildRequestPayload(request);
            string contentType = this.BuildContentType(request);

            Dictionary<string, string> headers =
                this.BuildHeaders(contentType, requestPayload, canonicalQueryString, request);
            headers.Add("X-TC-Action", actionName);
            string endpoint = headers["Host"];

            HttpConnection conn = new HttpConnection(
                $"{this.Profile.HttpProfile.Protocol}{endpoint}",
                this.Profile.HttpProfile.Timeout,
                this.Profile.HttpProfile.WebProxy,
                this.HttpClient);

            try
            {
                if (this.Profile.HttpProfile.ReqMethod == HttpProfile.REQ_GET)
                    return await conn.GetRequestAsync(this.Path, canonicalQueryString, headers).ConfigureAwait(false);

                if (this.Profile.HttpProfile.ReqMethod == HttpProfile.REQ_POST)
                    return await conn.PostRequestAsync(this.Path, requestPayload, headers).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new TencentCloudSDKException("API request failed", e);
            }

            return null;
        }

        private Dictionary<string, string> BuildHeaders(string contentType, byte[] requestPayload,
            string canonicalQueryString, AbstractModel request)
        {
            string endpoint = this.Endpoint;
            if (!string.IsNullOrEmpty(this.Profile.HttpProfile.Endpoint))
            {
                endpoint = this.Profile.HttpProfile.Endpoint;
            }

            string httpRequestMethod = this.Profile.HttpProfile.ReqMethod;
            string canonicalURI = "/";
            string canonicalHeaders = "content-type:" + contentType + "\nhost:" + endpoint + "\n";
            string signedHeaders = "content-type;host";
            string hashedRequestPayload = SignHelper.SHA256Hex(requestPayload);
            string canonicalRequest = httpRequestMethod + "\n"
                                                        + canonicalURI + "\n"
                                                        + canonicalQueryString + "\n"
                                                        + canonicalHeaders + "\n"
                                                        + signedHeaders + "\n"
                                                        + hashedRequestPayload;

            string algorithm = "TC3-HMAC-SHA256";
            long timestamp = ToTimestamp() / 1000;
            string requestTimestamp = timestamp.ToString();
            string date = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(timestamp)
                .ToString("yyyy-MM-dd");
            string service = endpoint.Split('.')[0];
            string credentialScope = date + "/" + service + "/" + "tc3_request";
            string hashedCanonicalRequest = SignHelper.SHA256Hex(canonicalRequest);
            string stringToSign = algorithm + "\n"
                                            + requestTimestamp + "\n"
                                            + credentialScope + "\n"
                                            + hashedCanonicalRequest;

            byte[] tc3SecretKey = Encoding.UTF8.GetBytes("TC3" + Credential.SecretKey);
            byte[] secretDate = SignHelper.HmacSHA256(tc3SecretKey, Encoding.UTF8.GetBytes(date));
            byte[] secretService = SignHelper.HmacSHA256(secretDate, Encoding.UTF8.GetBytes(service));
            byte[] secretSigning = SignHelper.HmacSHA256(secretService, Encoding.UTF8.GetBytes("tc3_request"));
            byte[] signatureBytes = SignHelper.HmacSHA256(secretSigning, Encoding.UTF8.GetBytes(stringToSign));
            string signature = BitConverter.ToString(signatureBytes).Replace("-", "").ToLower();

            string authorization = algorithm + " "
                                             + "Credential=" + Credential.SecretId + "/" + credentialScope + ", "
                                             + "SignedHeaders=" + signedHeaders + ", "
                                             + "Signature=" + signature;

            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers.Add("Authorization", authorization);
            headers.Add("Host", endpoint);
            headers.Add("Content-Type", contentType);
            headers.Add("X-TC-Timestamp", requestTimestamp);
            headers.Add("X-TC-Version", this.ApiVersion);
            headers.Add("X-TC-Region", this.Region);
            headers.Add("X-TC-RequestClient", this.SdkVersion);
            if (!string.IsNullOrEmpty(this.Credential.Token))
            {
                headers.Add("X-TC-Token", this.Credential.Token);
            }

            if (this.Profile.Language == Language.EN_US)
            {
                headers.Add("X-TC-Language", "en-US");
            }
            else
            {
                headers.Add("X-TC-Language", "zh-CN");
            }

            if (contentType == "application/octet-stream")
            {
                Dictionary<string, string> param = new Dictionary<string, string>();
                request.ToMap(param, "X-" + service.ToUpper() + "-");
                foreach (KeyValuePair<string, string> kv in param)
                    headers.Add(kv.Key, kv.Value);
            }

            return headers;
        }

        private string BuildContentType(AbstractModel request)
        {
            if (request is IOctetRequest)
                return "application/octet-stream";

            string httpRequestMethod = this.Profile.HttpProfile.ReqMethod;
            if (HttpProfile.REQ_GET.Equals(httpRequestMethod))
            {
                return "application/x-www-form-urlencoded";
            }
            else
            {
                return "application/json";
            }
        }

        private string BuildCanonicalQueryString(AbstractModel request)
        {
            string httpRequestMethod = this.Profile.HttpProfile.ReqMethod;
            if (!HttpProfile.REQ_GET.Equals(httpRequestMethod))
            {
                return "";
            }

            Dictionary<string, string> param = new Dictionary<string, string>();
            request.ToMap(param, "");
            StringBuilder urlBuilder = new StringBuilder();
            foreach (KeyValuePair<string, string> kvp in param)
            {
                urlBuilder.Append($"{WebUtility.UrlEncode(kvp.Key)}={WebUtility.UrlEncode(kvp.Value)}&");
            }

            return urlBuilder.ToString().TrimEnd('&');
        }

        private byte[] BuildRequestPayload(AbstractModel request)
        {
            string httpRequestMethod = this.Profile.HttpProfile.ReqMethod;
            if (HttpProfile.REQ_GET.Equals(httpRequestMethod))
            {
                return Encoding.UTF8.GetBytes("");
            }

            var serializableRequest = request as ISerializable;
            if (serializableRequest != null)
                return Encoding.UTF8.GetBytes(serializableRequest.Serialize());

            var octetRequest = request as IOctetRequest;
            if (octetRequest != null)
            {
                byte[] octetBody = octetRequest.OctetBody;
                if (octetBody != null)
                    return octetBody;
                return Encoding.UTF8.GetBytes("");
            }

            return Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(request, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
        }

        private Dictionary<string, string> BuildParam(AbstractModel request, string actionName)
        {
            Dictionary<string, string> param = new Dictionary<string, string>();
            request.ToMap(param, "");
            // inplace change
            this.FormatRequestData(actionName, param);
            return param;
        }

        private async Task<HttpResponseMessage> RequestV1(AbstractModel request, string actionName)
        {
            Dictionary<string, string> param = BuildParam(request, actionName);
            string endpoint = this.Endpoint;
            if (!string.IsNullOrEmpty(this.Profile.HttpProfile.Endpoint))
            {
                endpoint = this.Profile.HttpProfile.Endpoint;
            }

            HttpConnection conn = new HttpConnection(
                $"{this.Profile.HttpProfile.Protocol}{endpoint}",
                this.Profile.HttpProfile.Timeout,
                this.Profile.HttpProfile.WebProxy,
                this.HttpClient);

            try
            {
                if (this.Profile.HttpProfile.ReqMethod == HttpProfile.REQ_GET)
                    return await conn.GetRequestAsync(this.Path, param).ConfigureAwait(false);

                if (this.Profile.HttpProfile.ReqMethod == HttpProfile.REQ_POST)
                    return await conn.PostRequestAsync(this.Path, param).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new TencentCloudSDKException("API request failed", e);
            }

            return null;
        }

        private Dictionary<string, string> FormatRequestData(string action, Dictionary<string, string> param)
        {
            param.Add("Action", action);
            param.Add("RequestClient", this.SdkVersion);
            param.Add("Nonce", Math.Abs(new Random().Next()).ToString());

            long unixTime = ToTimestamp();
            param.Add("Timestamp", (unixTime / 1000).ToString());
            param.Add("Version", this.ApiVersion);

            if (!string.IsNullOrEmpty(this.Credential.SecretId))
            {
                param.Add("SecretId", this.Credential.SecretId);
            }

            if (!string.IsNullOrEmpty(this.Region))
            {
                param.Add("Region", this.Region);
            }

            if (!string.IsNullOrEmpty(this.Profile.SignMethod))
            {
                param.Add("SignatureMethod", this.Profile.SignMethod);
            }

            if (!string.IsNullOrEmpty(this.Credential.Token))
            {
                param.Add("Token", this.Credential.Token);
            }

            if (this.Profile.Language == Language.EN_US)
            {
                param.Add("Language", "en-US");
            }
            else if (this.Profile.Language == Language.ZH_CN)
            {
                param.Add("Language", "zh-CN");
            }

            string endpoint = this.Endpoint;
            if (!string.IsNullOrEmpty(this.Profile.HttpProfile.Endpoint))
            {
                endpoint = this.Profile.HttpProfile.Endpoint;
            }

            string sigInParam = SignHelper.MakeSignPlainText(
                new SortedDictionary<string, string>(param, StringComparer.Ordinal),
                this.Profile.HttpProfile.ReqMethod, endpoint, this.Path);
            string sigOutParam = SignHelper.Sign(this.Credential.SecretKey, sigInParam, this.Profile.SignMethod);
            param.Add("Signature", sigOutParam);
            return param;
        }

        public long ToTimestamp()
        {
#if NET45
            DateTime startTime = new System.DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            DateTime nowTime = DateTime.UtcNow;
            long unixTime = (long)Math.Round((nowTime - startTime).TotalMilliseconds, MidpointRounding.AwayFromZero);
            return unixTime;
#endif

#if NS2 || NETSTANDARD2_0
            DateTimeOffset expiresAtOffset = DateTimeOffset.Now;
            var totalSeconds = expiresAtOffset.ToUnixTimeMilliseconds();
            return totalSeconds;
#endif
        }
    }
}