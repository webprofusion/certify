// This file is inspired by https://github.com/ovh/csharp-ovh/blob/master/csharp-ovh/Client.cs , which 
// has been adapted to Certify in order to reduce dependencies and remove features useless in our context
// Original file is copyright OVH SAS :

//Copyright(c) 2013-2016, OVH SAS.
//All rights reserved.

//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:

//  * Redistributions of source code must retain the above copyright
//   notice, this list of conditions and the following disclaimer.

// * Redistributions in binary form must reproduce the above copyright
//   notice, this list of conditions and the following disclaimer in the
//   documentation and/or other materials provided with the distribution.

// * Neither the name of OVH SAS nor the
//   names of its contributors may be used to endorse or promote products
//   derived from this software without specific prior written permission.

//THIS SOFTWARE IS PROVIDED BY OVH SAS AND CONTRIBUTORS ``AS IS'' AND ANY
//EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED.IN NO EVENT SHALL OVH SAS AND CONTRIBUTORS BE LIABLE FOR ANY
//DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Certify.Providers.DNS.OVH
{

    // See https://github.com/ovh/csharp-ovh
    // This module provides a simple C# wrapper over the OVH REST API.
    // It handles requesting credential, signing queries...
    // - To get your API keys: https://eu.api.ovh.com/createApp/
    // - To get started with API: https://api.ovh.com/g934.first_step_with_api

    /// <summary>
    /// Low level OVH Client. It abstracts all the authentication and request
    /// signing logic along with some nice tools helping with key generation.
    /// All low level request logic including signing and error handling takes place
    /// in "Client.Call" function. Convenient wrappers
    /// "Client.Get" "Client.Post", "Client.Put",
    /// "Client.Delete" should be used instead. "Client.Post",
    /// "Client.Put" both accept arbitrary list of keyword arguments
    /// mapped to "data" param of "Client.Call".
    /// Example usage:
    ///     from ovh import Client, APIError
    ///     REGION = 'ovh-eu'
    ///     APP_KEY= "&lt;application key&gt;"
    ///     APP_SECRET= "&lt;application secret key&gt;"
    ///     CONSUMER_KEY= "&lt;consumer key&gt;>"
    ///     client = Client(REGION, APP_KEY, APP_SECRET, CONSUMER_KEY)
    ///     try:
    ///         print client.get('/me')
    ///     except APIError as e:
    ///         print "Ooops, failed to get my info:", e.msg
    /// </summary>
    public class OvhClient
    {
        private static readonly Dictionary<string, string> _endpoints = new Dictionary<string, string>()
            {
                { "ovh-eu", "https://eu.api.ovh.com/1.0/"               },
                { "ovh-ca", "https://ca.api.ovh.com/1.0/"               },
                { "kimsufi-eu", "https://eu.api.kimsufi.com/1.0/"       },
                { "kimsufi-ca", "https://ca.api.kimsufi.com/1.0/"       },
                { "soyoustart-eu", "https://eu.api.soyoustart.com/1.0/" },
                { "soyoustart-ca", "https://ca.api.soyoustart.com/1.0/" },
                { "runabove-ca", "https://api.runabove.com/1.0/"        }
            };

        private const int _defaultTimeout = 180;
        private readonly WebClient _webClient;

        /// <summary>
        /// API Endpoint that this <c>Client</c> targets
        /// </summary>
        public string Endpoint { get; set; }
        /// <summary>
        /// API application Key
        /// </summary>
        public string ApplicationKey { get; set; }
        /// <summary>
        /// API application secret
        /// </summary>
        public string ApplicationSecret { get; set; }
        /// <summary>
        /// Consumer key that can be either <see cref="RequestConsumerKey">generated</see> or passed to the <see cref="ConfigurationManager">configuration manager</see>>
        /// </summary>
        public string ConsumerKey { get; set; }
        /// <summary>
        /// HTTP operations timeout
        /// </summary>
        public int Timeout { get; set; }

        private bool _isTimeDeltaInitialized;
        private long _timeDelta;
        /// <summary>
        /// Request signatures are valid only for a short amount of time to mitigate
        /// risk of attack replay scenarii which requires to use a common time
        /// reference.This function queries endpoint's time and computes the delta.
        /// This entrypoint does not require authentication.
        /// This method is *lazy*. It will only load it once even though it is used
        /// for each request.
        /// </summary>
        public async Task<long> GetTimeDelta()
        {

            if (!_isTimeDeltaInitialized)
            {
                _timeDelta = await ComputeTimeDelta();
                _isTimeDeltaInitialized = true;
            }
            return _timeDelta;

        }

        private OvhClient()
        {

            _webClient = new WebClient();
            _webClient.CachePolicy = new RequestCachePolicy(RequestCacheLevel.BypassCache);
        }

        public static string GetAvailableEndpointsAsString()
        {
            return string.Join(", ", _endpoints.Keys);
        }

        /// <summary>
        /// Creates a new Client. No credential check is done at this point.
        /// The "application_key" identifies your application while
        /// "application_secret" authenticates it. On the other hand, the
        /// "consumer_key" uniquely identifies your application's end user without
        /// requiring his personal password.
        /// If any of "endpoint", "application_key", "application_secret"
        /// or "consumer_key" is not provided, this client will attempt to locate
        /// from them from environment, %USERPROFILE%/.ovh.cfg or current_dir/.ovh.cfg.
        /// </summary>
        /// <param name="endpoint">API endpoint to use. Valid values in "Endpoints"</param>
        /// <param name="applicationKey">Application key as provided by OVH</param>
        /// <param name="applicationSecret">Application secret key as provided by OVH</param>
        /// <param name="consumerKey">User token as provided by OVH</param>
        /// <param name="timeout">Connection timeout for each request</param>
        public OvhClient(string endpointName, string applicationKey,
            string applicationSecret, string consumerKey,
            int timeout = _defaultTimeout) : this()
        {

            if (!_endpoints.ContainsKey(endpointName))
            {
                var errorMsg = $"{endpointName} is not a valid endpointName. EndpointName should be one of the following values : {GetAvailableEndpointsAsString()}";
                throw new ArgumentException(errorMsg, nameof(endpointName));
            }

            Endpoint = _endpoints[endpointName];
            _webClient.BaseAddress = Endpoint;

            //ApplicationKey
            if (string.IsNullOrWhiteSpace(applicationKey))
                throw new ArgumentException("ApplicationKey is required.", nameof(applicationKey));


            ApplicationKey = applicationKey;


            //SecretKey
            if (string.IsNullOrWhiteSpace(applicationSecret))
                throw new ArgumentException("ApplicationSecret is required.", nameof(applicationSecret));

            ApplicationSecret = applicationSecret;

            //ConsumerKey
            if (string.IsNullOrWhiteSpace(consumerKey))
                throw new ArgumentException("ConsumerKey is required.", nameof(consumerKey));
            ConsumerKey = consumerKey;


            //Timeout
            Timeout = timeout;
        }

        #region GET

        /// <summary>
        /// Append arguments to the target URL
        /// </summary>
        /// <param name="target">Target URL</param>
        /// <param name="kwargs">Key value arguments to append</param>
        /// <returns>Url suffixed with kwargs</returns>
        private string PrepareGetTarget(string target, NameValueCollection kwargs)
        {
            if (kwargs != null)
            {
                target += kwargs.ToString();
            }

            return target;
        }

        /// <summary>
        /// Issues a POST call
        /// </summary>
        /// <param name="target">API method to call</param>
        /// <param name="kwargs">Arguments to append to URL</param>
        /// <param name="needAuth">If true, send authentication headers</param>
        /// <returns>Raw API response</returns>
        public async Task<string> Get(string target, NameValueCollection kwargs = null, bool needAuth = true)
        {
            target = PrepareGetTarget(target, kwargs);
            return await Call("GET", target, null, needAuth);
        }

        /// <summary>
        /// Issues a POST call with an expected return type
        /// </summary>
        /// <typeparam name="T">Expected return type</typeparam>
        /// <param name="target">API method to call</param>
        /// <param name="kwargs">Arguments to append to URL</param>
        /// <param name="needAuth">If true, send authentication headers</param>
        /// <returns>API response deserialized to T by JSON.Net</returns>
        public async Task<T> Get<T>(string target, NameValueCollection kwargs = null, bool needAuth = true)
        {
            target = PrepareGetTarget(target, kwargs);
            return await Call<T>("GET", target, null, needAuth);
        }

        #endregion

        #region POST

        /// <summary>
        /// Issues a POST call
        /// </summary>
        /// <param name="target">API method to call</param>
        /// <param name="data">Object to serialize and send as body</param>
        /// <param name="needAuth">If true, send authentication headers</param>
        /// <returns>Raw API response</returns>
        public async Task<string> Post(string target, object data, bool needAuth = true)
        {
            return await Call("POST", target, JsonConvert.SerializeObject(data), needAuth);
        }

        /// <summary>
        /// Issues a POST call
        /// </summary>
        /// <param name="target">API method to call</param>
        /// <param name="data">Json data to send as body</param>
        /// <param name="needAuth">If true, send authentication headers</param>
        /// <returns>Raw API response</returns>
        public async Task<string> Post(string target, string data, bool needAuth = true)
        {
            return await Call("POST", target, data, needAuth);
        }

        /// <summary>
        /// Issues a POST call
        /// </summary>
        /// <typeparam name="T">Expected return type</typeparam>
        /// <param name="target">API method to call</param>
        /// <param name="data">Object to serialize and send as body</param>
        /// <param name="needAuth">If true, send authentication headers</param>
        /// <returns>API response deserialized to T by JSON.Net</returns>
        public async Task<T> Post<T>(string target, object data, bool needAuth = true)
        {
            return await Call<T>("POST", target, JsonConvert.SerializeObject(data), needAuth);
        }

        /// <summary>
        /// Issues a POST call
        /// </summary>
        /// <typeparam name="T">Expected return type</typeparam>
        /// <param name="target">API method to call</param>
        /// <param name="data">Json data to send as body</param>
        /// <param name="needAuth">If true, send authentication headers</param>
        /// <returns>API response deserialized to T by JSON.Net</returns>
        public async Task<T> Post<T>(string target, string data, bool needAuth = true)
        {
            return await Call<T>("POST", target, data, needAuth);
        }

        /// <summary>
        /// Issues a POST call
        /// </summary>
        /// <typeparam name="T">Expected return type</typeparam>
        /// <typeparam name="Y">Input type</typeparam>
        /// <param name="target">API method to call</param>
        /// <param name="data">Json data to send as body</param>
        /// <param name="needAuth">If true, send authentication headers</param>
        /// <returns>API response deserialized to T by JSON.Net with Strongly typed object as input</returns>
        public async Task<T> Post<T, Y>(string target, Y data, bool needAuth = true)
            where Y : class
        {
            return await Call<T, Y>("POST", target, data, needAuth);
        }
        #endregion

        #region PUT
        /// <summary>
        /// Issues a PUT call
        /// </summary>
        /// <param name="target">API method to call</param>
        /// <param name="data">Object to serialize and send as body</param>
        /// <param name="needAuth">If true, send authentication headers</param>
        /// <returns>Raw API response</returns>
        public async Task<string> Put(string target, object data, bool needAuth = true)
        {
            return await Call("PUT", target, JsonConvert.SerializeObject(data), needAuth);
        }

        /// <summary>
        /// Issues a POST call
        /// </summary>
        /// <param name="target">API method to call</param>
        /// <param name="data">Json data to send as body</param>
        /// <param name="needAuth">If true, send authentication headers</param>
        /// <returns>Raw API response</returns>
        public async Task<string> Put(string target, string data, bool needAuth = true)
        {
            return await Call("PUT", target, data, needAuth);
        }

        /// <summary>
        /// Issues a POST call
        /// </summary>
        /// <typeparam name="T">Expected return type</typeparam>
        /// <param name="target">API method to call</param>
        /// <param name="data">Object to serialize and send as body</param>
        /// <param name="needAuth">If true, send authentication headers</param>
        /// <returns>API response deserialized to T by JSON.Net</returns>
        public async Task<T> Put<T>(string target, object data, bool needAuth = true)
        {
            return await Call<T>("PUT", target, JsonConvert.SerializeObject(data), needAuth);
        }

        /// <summary>
        /// Issues a POST call
        /// </summary>
        /// <typeparam name="T">Expected return type</typeparam>
        /// <param name="target">API method to call</param>
        /// <param name="data">Json data to send as body</param>
        /// <param name="needAuth">If true, send authentication headers</param>
        /// <returns>API response deserialized to T by JSON.Net</returns>
        public async Task<T> Put<T>(string target, string data, bool needAuth = true)
        {
            return await Call<T>("PUT", target, data, needAuth);
        }

        /// <summary>
        /// Issues a POST call
        /// </summary>
        /// <typeparam name="T">Expected return type</typeparam>
        /// <typeparam name="Y">Input type</typeparam>
        /// <param name="target">API method to call</param>
        /// <param name="data">Json data to send as body</param>
        /// <param name="needAuth">If true, send authentication headers</param>
        /// <returns>API response deserialized to T by JSON.Net with Strongly typed object as input</returns>
        public async Task<T> Put<T, Y>(string target, Y data, bool needAuth = true)
            where Y : class
        {
            return await Call<T, Y>("PUT", target, data, needAuth);
        }
        #endregion PUT

        #region DELETE
        /// <summary>
        /// Issues a DELETE call
        /// </summary>
        /// <param name="target">API method to call</param>
        /// <param name="needAuth">If true, send authentication headers</param>
        /// <returns>Raw API response</returns>
        public async Task<string> Delete(string target, bool needAuth = true)
        {
            return await Call("DELETE", target, null, needAuth);
        }

        /// <summary>
        /// Issues a DELETE call
        /// </summary>
        /// <typeparam name="T">Expected return type</typeparam>
        /// <param name="target">API method to call</param>
        /// <param name="needAuth">If true, send authentication headers</param>
        /// <returns>API response deserialized to T by JSON.Net</returns>
        public async Task<T> Delete<T>(string target, bool needAuth = true)
        {
            return await Call<T>("DELETE", target, null, needAuth);
        }

        #endregion


        /// <summary>
        /// Lowest level call helper. If "consumerKey" is not "null", inject
        /// authentication headers and sign the request.
        /// Request signature is a sha1 hash on following fields, joined by '+'
        ///  - application_secret
        ///  - consumer_key
        ///  - METHOD
        ///  - full request url
        ///  - body
        ///  - server current time (takes time delta into account)
        /// </summary>
        /// <param name="method">HTTP verb. Usualy one of GET, POST, PUT, DELETE</param>
        /// <param name="path">api entrypoint to call, relative to endpoint base path</param>
        /// <param name="data">any json serializable data to send as request's body</param>
        /// <param name="needAuth">if False, bypass signature</param>
        /// <exception cref="HttpException">When underlying request failed for network reason</exception>
        /// <exception cref="InvalidResponseException">when API response could not be decoded</exception>
        private async Task<string> Call(string method, string path, string data = null, bool needAuth = true)
        {
            method = method.ToUpper();
            if (path.StartsWith("/"))
            {
                path = path.Substring(1);
            }
            string target = Endpoint + path;
            WebHeaderCollection headers = new WebHeaderCollection();
            headers.Add("X-Ovh-Application", ApplicationKey);

            if (data != null)
            {
                headers.Add("Content-type", "application/json");
            }

            if (needAuth)
            {
                if (ApplicationSecret == null)
                {
                    throw new InvalidOperationException("Application secret is missing.");
                }
                if (ConsumerKey == null)
                {
                    throw new InvalidOperationException("ConsumerKey is missing.");
                }

                long currentServerTimestamp = GetCurrentUnixTimestamp() + await GetTimeDelta();

                SHA1Managed sha1Hasher = new SHA1Managed();
                string toSign =
                    string.Join("+", ApplicationSecret, ConsumerKey, method,
                        target, data, currentServerTimestamp);
                byte[] binaryHash = sha1Hasher.ComputeHash(Encoding.UTF8.GetBytes(toSign));
                string signature = string.Join("",
                    binaryHash.Select(x => x.ToString("X2"))).ToLower();

                headers.Add("X-Ovh-Consumer", ConsumerKey);
                headers.Add("X-Ovh-Timestamp", currentServerTimestamp.ToString());
                headers.Add("X-Ovh-Signature", "$1$" + signature);
            }

            string response = "";

            //NOTE: would be better to reuse some headers
            _webClient.Headers = headers;
            if (method != "GET")
            {
                response = await _webClient.UploadStringTaskAsync(path, method, data ?? "");
            }
            else
            {
                response = await _webClient.DownloadStringTaskAsync(path);
            }
            return response;
        }

        private async Task<T> Call<T>(string method, string path, string data = null, bool needAuth = true)
        {
            return JsonConvert.DeserializeObject<T>(await Call(method, path, data, needAuth));
        }

        private async Task<T> Call<T, Y>(string method, string path, Y data = null, bool needAuth = true)
            where Y : class
        {
            return JsonConvert.DeserializeObject<T>(await Call(method, path, JsonConvert.SerializeObject(data), needAuth));
        }


        private async Task<long> ComputeTimeDelta()
        {
            long serverUnixTimestamp = await Get<long>("/auth/time", null, false);
            long currentUnixTimestamp = GetCurrentUnixTimestamp();
            return serverUnixTimestamp - currentUnixTimestamp;
        }

        private long GetCurrentUnixTimestamp()
        {
            return (long)DateTime.Now.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        }
    }

}
