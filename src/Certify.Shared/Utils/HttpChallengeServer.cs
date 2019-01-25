using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.Core.Management.Challenges
{
    public class HttpChallengeServer
    {
        private HttpListener _httpListener;
        private HttpClient _apiClient;
        private Task _serverTask;

        private string _controlKey = "QUIT123";
        private string _checkKey = "TESTING123";
        private readonly string _challengePrefix = "/.well-known/acme-challenge/";

        private Dictionary<string, string> _challengeResponses { get; set; }

        private int _maxServiceLookups = 1000;
        private string _baseUri = "";

#if DEBUG
        private bool _debugMode = true;
#else

        private bool _debugMode = false;
#endif
        private DateTime _lastRequestTime { get; set; }

        private void Log(string msg, bool clearLog = false)
        {
            msg = DateTime.Now + ": " + msg + "\r\n";

            try
            {
                if (clearLog)
                {
                    System.IO.File.WriteAllText(Util.GetAppDataFolder() + "\\logs\\httpChallengeServer.log", msg);
                }
                else
                {
                    System.IO.File.AppendAllText(Util.GetAppDataFolder() + "\\logs\\httpChallengeServer.log", msg);
                }
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine(msg);
            }
        }

        /// <summary>
        /// Start http challenge server.
        /// </summary>
        /// <param name="port"> Port to listen on, default 80 </param>
        /// <param name="controlKey"> Control key to command process to quit </param>
        /// <param name="checkKey"> Check key to test server response </param>
        /// <returns>  </returns>
        public bool Start(int port = 80, string controlKey = null, string checkKey = null)
        {
#if DEBUG
            _debugMode = true;
#endif
            _lastRequestTime = DateTime.Now;
            try
            {
                if (controlKey != null) _controlKey = controlKey;
                if (checkKey != null) _checkKey = checkKey;

                _httpListener = new HttpListener();
                
                var serverConfig = Certify.SharedUtils.ServiceConfigManager.GetAppServiceConfig();
                _baseUri = $"http://{serverConfig.Host}:{serverConfig.Port}/api/";

                _apiClient = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });
                _apiClient.DefaultRequestHeaders.Add("User-Agent", "Certify/HttpChallengeServer");
                _apiClient.Timeout = new TimeSpan(0, 0, 20);

                var uriPrefix = $"http://+:{port}{_challengePrefix}";
                _httpListener.Prefixes.Add(uriPrefix);

                _challengeResponses = new Dictionary<string, string>();

                _httpListener.Start();

                Log($"Http Challenge Server Started: {uriPrefix}", true);
                Log($"Control Key: {_controlKey}: Check Key: {_checkKey}");

                _serverTask = Task.Run(ServerTask);

                var stateTimer = new Timer((object stateInfo) =>
                {
                    Log("Checking for auto close.");
                    var time = _lastRequestTime - DateTime.Now;
                    if (Math.Abs(time.TotalSeconds) > 30)
                    {
                        Log("No requests recently, stopping server.");
                        Stop();
                    }
                }, null, 1000 * 10, 1000 * 10);

                return true;
            }
            catch (Exception exp)
            {
                //could not start listener, port may be in use
                System.Diagnostics.Debug.WriteLine($"Http Challenge server error: {exp}");
                try
                {
                    // try to stop the listener, if a collision on port etc then listener will already be disposed
                    _httpListener.Stop();
                }
                catch { }

                _httpListener.Close();
                _httpListener = null;

                _apiClient.Dispose();
                _apiClient = null;

                Log("Failed to Start Http Challenge Server");
            }

            return false;
        }

        private async Task ServerTask()
        {
            string serverHeader = "Http-Challenge-Server-Certify/";

            while (_httpListener != null && _httpListener.IsListening)
            {
                _lastRequestTime = DateTime.Now;

                var server = await _httpListener.GetContextAsync();

                var path = server.Request.Url.LocalPath;

                if (_debugMode) Log(path);

                server.Response.Headers.Add("Server", serverHeader);

                var key = path.Replace(_challengePrefix, "").ToLower();

                if (key == _controlKey.ToLower())
                {
                    server.Response.StatusCode = (int)HttpStatusCode.OK;
                    server.Response.ContentType = "text/plain";
                    using (var stream = new StreamWriter(server.Response.OutputStream))
                    {
                        stream.Write("Stopping");
                        stream.Flush();
                        stream.Close();
                    }
                    server.Response.Close();
                    Stop();
                    return;
                }

                if (key == _checkKey.ToLower())
                {
                    if (_debugMode) Log("Check key sent. OK.");
                    server.Response.StatusCode = (int)HttpStatusCode.OK;
                    server.Response.ContentType = "text/plain";

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
                            _maxServiceLookups--;

                            var apiUrl = $"{_baseUri}managedcertificates/currentchallenges/";

                            if (_debugMode) Log($"Key {key} not found: Refreshing challenges.. {apiUrl}");
                            var response = await _apiClient.GetAsync(apiUrl);
                            if (response.IsSuccessStatusCode)
                            {
                                var json = await response.Content.ReadAsStringAsync();

                                if (_debugMode) Log(json);
                                var list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<SimpleAuthorizationChallengeItem>>(json);
                                _challengeResponses = new Dictionary<string, string>();
                                list.ForEach(i => _challengeResponses.Add(i.Key.ToLower(), i.Value));
                            }
                            else
                            {
                                Log("Failed to refresh current challenges");
                            }
                        }
                        catch (Exception exp)
                        {
                            Log(exp.ToString());
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

                            Log($"Responded with Key: {key} Value:{value}");
                            
                        }
                        else
                        {
                            server.Response.StatusCode = (int)HttpStatusCode.NotFound;

                            Log($"Requested key not found: {key}");
                        }

                       
                    }
                    else
                    {
                        server.Response.StatusCode = 404;
                    }
                }

                server.Response.Close();

                if (_debugMode) Log("End request.");

                if (_maxServiceLookups == 0)
                {
                    // give up trying to resolve challenges, we have been queried too many times for
                    // challenge responses we don't know about
                    Stop();

                    if (_debugMode) Log("Max lookups failed.");
                }
            }
        }

        public bool IsRunning()
        {
            return _httpListener != null;
        }

        public void Stop()
        {
            Log("Stopping Server");
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
