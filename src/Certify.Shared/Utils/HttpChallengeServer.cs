using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.Core.Management.Challenges
{
    public class HttpChallengeServer
    {
        private HttpListener _httpListener;
        private HttpClient _apiClient;
        private Task _serverTask;

        private string _controlKey = "";
        private string _checkKey = "TESTING123";
        private string _challengePrefix = "/.well-known/acme-challenge/";

        private Dictionary<string, string> _challengeResponses { get; set; }

        private bool _debugMode = false;
        private int _maxLookups = 3;

#if DEBUG
        private string _baseUri = Certify.Locales.ConfigResources.LocalServiceBaseURIDebug + "/api/";
#else
        private string _baseUri = Certify.Locales.ConfigResources.LocalServiceBaseURI + "/api/";
        _debugMode = false;
#endif

        /// <summary>
        /// Start http challenge server. 
        /// </summary>
        /// <param name="port"> Port to listen on, default 80 </param>
        /// <param name="controlKey"> Control key to command process to quit </param>
        /// <param name="checkKey"> Check key to test server response </param>
        /// <returns></returns>
        public bool Start(int port = 80, string controlKey = null, string checkKey = null)
        {
            try
            {
                _httpListener = new HttpListener();
                _apiClient = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });
                _apiClient.Timeout = new TimeSpan(0, 0, 5);

                var uriPrefix = $"http://+:{port}{_challengePrefix}";
                _httpListener.Prefixes.Add(uriPrefix);

                _challengeResponses = new Dictionary<string, string>();

                _httpListener.Start();

                _serverTask = Task.Run(ServerTask);
                return true;
            }
            catch (Exception exp)
            {
                //could not start listener, port may be in use
                System.Diagnostics.Debug.WriteLine($"Http Challenge server error: {exp}");
                _httpListener.Stop();
                _httpListener.Close();
                _httpListener = null;

                _apiClient.Dispose();
                _apiClient = null;
            }

            return false;
        }

        private async Task ServerTask()
        {
            while (_httpListener != null && _httpListener.IsListening)
            {
                var server = await _httpListener.GetContextAsync();

                var path = server.Request.Url.LocalPath;

                if (_debugMode) System.Console.Out.WriteLine(path);

                var key = path.Replace(_challengePrefix, "").ToLower();

                if (key == _controlKey)
                {
                    server.Response.Close();
                    Stop();
                    return;
                }

                if (key == _checkKey)
                {
                    if (_debugMode) System.Console.Out.WriteLine("Check key sent. OK.");
                    using (var stream = new StreamWriter(server.Response.OutputStream))
                    {
                        stream.Write("OK");
                        stream.Flush();
                        stream.Close();
                    }
                }
                else
                {
                    if (key.Length > 8 && !_challengeResponses.ContainsKey(key))
                    {
                        // if challenge response not in our cache, fetch from local API
                        try
                        {
                            if (_debugMode) System.Console.Out.WriteLine($"Key {key} not found: Refreshing challenges..");
                            var json = await _apiClient.GetStringAsync($"{_baseUri}managedcertificates/currentchallenges/");

                            if (_debugMode) System.Console.Out.WriteLine(json);
                            var list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<SimpleAuthorizationChallengeItem>>(json);
                            _challengeResponses = new Dictionary<string, string>();
                            list.ForEach(i => _challengeResponses.Add(i.Key.ToLower(), i.Value));
                        }
                        catch (Exception exp)
                        {
                            System.Console.Out.WriteLine(exp);
                            _maxLookups--;
                        }
                    }

                    if (_challengeResponses.ContainsKey(key))
                    {
                        var value = _challengeResponses[key];

                        if (value != null)
                        {
                            // System.Console.Out.WriteLine(value);

                            server.Response.StatusCode = (int)HttpStatusCode.OK;
                            server.Response.ContentType = "text/plain";

                            using (var stream = new StreamWriter(server.Response.OutputStream))
                            {
                                stream.Write(value);
                                stream.Flush();
                                stream.Close();
                            }
                        }
                        else
                        {
                            server.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        }
                    }
                    else
                    {
                        server.Response.StatusCode = 404;
                    }
                }
                server.Response.Close();
                if (_debugMode) System.Console.Out.WriteLine("End request.");

                if (_maxLookups == 0)
                {
                    // give up trying to resolve challenges, we have been queried too many times for
                    // challenge responses we don't know about
                    Stop();
                }
            }
        }

        public bool IsRunning()
        {
            return _httpListener != null;
        }

        public void Stop()
        {
            if (_httpListener != null)
            {
                try
                {
                    _httpListener.Stop();
                }
                catch
                {
                }

                _httpListener.Close();
                _httpListener = null;
            }
        }
    }
}

/*
 Benchmark notes:
 loadtest -n 1000 -c 100 http://localhost/.well-known/acme-challenge/test123
 1000 Requests, 0.608164001s, 1644 request per second.
*/
