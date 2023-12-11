using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Shared;
using Newtonsoft.Json.Linq;

namespace Certify.Core.Management.Challenges
{
    public class HttpChallengeServer
    {
        /// <summary>
        /// The http listener uses http.sys to listen for incoming challenge requests
        /// </summary>
        private HttpListener _httpListener;

        /// <summary>
        /// The api client talks back to the main certify service to get the current list of challenge responses expected
        /// </summary>
        private HttpClient _apiClient;

        /// <summary>
        /// The control key is the key which will stop the server
        /// </summary>
        private string _controlKey = "QUIT123";

        /// <summary>
        /// The check key is the key which will return a test response to prove the server is responding OK
        /// </summary>
        private string _checkKey = "TESTING123";

        /// <summary>
        /// The challenge prefix is the path prefix which the http listener will respond to, we specifically listen for acme challenges
        /// </summary>
        private readonly string _challengePrefix = "/.well-known/acme-challenge/";
        private string _listeningUrl = string.Empty;

        private ConcurrentDictionary<string, string> _challengeResponses { get; set; }

        private int _maxServiceLookups = 1000;
        private int _autoCloseSeconds = 60;
        private string _baseUri = "";
        private Timer _autoCloseTimer;
        private readonly object _challengeServerStartLock = new object();
        private readonly object _challengeServerStopLock = new object();

        /// <summary>
        /// If true, challenge server has been started or a start has been attempted
        /// </summary>
        private bool _isActive = false;
#if DEBUG
        private bool _debugMode = true;
#else

        private bool _debugMode = false;
#endif
        private DateTimeOffset _lastRequestTime { get; set; }

        private void Log(string msg, bool clearLog = false)
        {
            msg = DateTime.Now + ": " + msg + "\r\n";

            try
            {
                var logPath = Path.Combine(EnvironmentUtil.GetAppDataFolder(), "logs", "httpChallengeServer.log");
                if (clearLog)
                {
                    System.IO.File.WriteAllText(logPath, msg);
                }
                else
                {
                    System.IO.File.AppendAllText(logPath, msg);
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
        public bool Start(ServiceConfig serverConfig, string controlKey = null, string checkKey = null)
        {
            lock (_challengeServerStartLock)
            {
#if DEBUG
                _debugMode = true;
#endif
                _lastRequestTime = DateTimeOffset.UtcNow;
                try
                {
                    if (controlKey != null)
                    {
                        _controlKey = controlKey;
                    }

                    if (checkKey != null)
                    {
                        _checkKey = checkKey;
                    }

                    _isActive = true;
                    _httpListener = new HttpListener();

                    _baseUri = $"http://{serverConfig.Host}:{serverConfig.Port}/api/";

                    _apiClient = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });
                    _apiClient.DefaultRequestHeaders.Add("User-Agent", "Certify/HttpChallengeServer");
                    _apiClient.Timeout = new TimeSpan(0, 0, 20);

                    var uriPrefix = $"http://+:{serverConfig.HttpChallengeServerPort}{_challengePrefix}";
                    _listeningUrl = uriPrefix;
                    _httpListener.Prefixes.Add(uriPrefix);

                    _challengeResponses = new ConcurrentDictionary<string, string>();

                    _httpListener.Start();

                    Log($"Http Challenge Server Started: {uriPrefix}", true);
                    Log($"Control Key: {_controlKey}: Check Key: {_checkKey}");

                    _ = Task.Run(ServerTask);

                    _autoCloseTimer = new Timer((object stateInfo) =>
                    {
                        Log("Checking for auto close.");
                        var time = _lastRequestTime - DateTimeOffset.UtcNow;
                        if (Math.Abs(time.TotalSeconds) > _autoCloseSeconds || !IsRunning)
                        {
                            Log("No requests recently, stopping server.");
                            Stop();

                            _autoCloseTimer.Dispose();
                            _autoCloseTimer = null;
                        }
                    }, null, 1000 * 10, 1000 * 10);

                    return true;
                }
                catch (Exception exp)
                {
                    // could not start listener, port may be in use
                    Log($"Failed to Start Http Challenge Server: {exp.Message}");
                    Stop();
                }

                return false;
            }
        }

        private async Task ServerTask()
        {

            _lastRequestTime = DateTimeOffset.UtcNow;

            while (_httpListener != null && _httpListener.IsListening)
            {
                try
                {
                    // blocks until a request is received

                    var server = await _httpListener.GetContextAsync();
                    _lastRequestTime = DateTimeOffset.UtcNow;
                    var path = server.Request.Url.LocalPath;

                    if (_debugMode)
                    {
                        Log(path);
                    }

                    var key = path.Replace(_challengePrefix, "").ToLower();

                    if (_debugMode)
                    {
                        if (key == "panic")
                        {
                            throw new Exception("Simulated panic requested");
                        }
                    }

                    if (key == _controlKey.ToLower())
                    {
                        using (var res = server.Response)
                        {
                            SendResponse("Stopping", res);
                        }

                        Stop();

                        return;
                    }

                    if (key == _checkKey.ToLower())
                    {
                        if (_debugMode)
                        {
                            Log("Check key sent. OK.");
                        }

                        using (var res = server.Response)
                        {
                            SendResponse("OK", res);
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

                                if (_debugMode)
                                {
                                    Log($"Key {key} not found: Refreshing challenges.. {apiUrl}");
                                }

                                var response = await _apiClient.GetAsync(apiUrl);
                                if (response.IsSuccessStatusCode)
                                {
                                    var json = await response.Content.ReadAsStringAsync();

                                    if (_debugMode)
                                    {
                                        Log(json);
                                    }

                                    var list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<SimpleAuthorizationChallengeItem>>(json);
                                    _challengeResponses.Clear();
                                    list.ForEach(i => _challengeResponses.TryAdd(i.Key.ToLower(), i.Value));
                                }
                                else
                                {
                                    Log($"Could not refresh current challenges from main service [{response.StatusCode}]. Service may be unavailable or inaccessible.");
                                }
                            }
                            catch (Exception exp)
                            {
                                Log($"Could not refresh current challenges from main service. Service may be unavailable or inaccessible. {exp} ");
                            }
                        }

                        if (_challengeResponses.ContainsKey(key))
                        {
                            var value = _challengeResponses[key];

                            if (value != null)
                            {
                                using (var res = server.Response)
                                {
                                    SendResponse(value, res);
                                }

                                Log($"Responded with Key: {key} Value:{value}");

                            }
                            else
                            {
                                using (var res = server.Response)
                                {
                                    SendResponseCode(HttpStatusCode.NotFound, res);
                                }

                                Log($"Requested key not found: {key}");
                            }
                        }
                        else
                        {
                            using (var res = server.Response)
                            {
                                SendResponseCode(HttpStatusCode.NotFound, res);
                            }
                        }
                    }

                    if (_debugMode)
                    {
                        Log("End request.");
                    }
                }
                catch (ObjectDisposedException)
                {
                    // object disposed exception is normal when stopping the server
                }
                catch (System.Net.ProtocolViolationException exp)
                {
                    Log($"Error communicating with client: {exp}");
                    // this happens when the client closes the connection after receiving the headers, like curl -I
                }
                catch (Exception exp)
                {
                    Log($"Error in http challenge server: {exp}");
                    Stop();
                    return;
                }

                if (_maxServiceLookups == 0)
                {
                    // give up trying to resolve challenges, we have been queried too many times for
                    // challenge responses we don't know about
                    Stop();

                    if (_debugMode)
                    {
                        Log("Max lookups failed.");
                    }

                    return;
                }
            }
        }

        private static void SendResponse(string value, HttpListenerResponse res)
        {

            res.AddHeader("Server", "Http-Challenge-Server-Certify/");
            res.StatusCode = (int)HttpStatusCode.OK;
            res.ContentType = "text/plain";

            res.ContentEncoding = Encoding.UTF8;
            res.ContentLength64 = Encoding.UTF8.GetByteCount(value);

            using (var stream = new StreamWriter(res.OutputStream))
            {
                stream.Write(value);
                stream.Flush();
                stream.Close();
            }

            res.OutputStream.Dispose();
            res.Close();
        }

        private static void SendResponseCode(HttpStatusCode value, HttpListenerResponse res)
        {
            res.Headers.Add("Server", "Http-Challenge-Server-Certify/");
            res.StatusCode = (int)value;
            res.Close();
        }

        public bool IsRunning => _httpListener?.IsListening == true;

        public void Stop()
        {
            if (_isActive == false)
            {
                // nothing to do
                return;
            }

            lock (_challengeServerStopLock)
            {
                _isActive = false;

                Log("Stopping Server");

                try
                {
                    if (_httpListener != null)
                    {
                        try
                        {
                            // try to stop the listener, if a collision on port etc then listener will already be disposed
                            _httpListener?.Stop();

                            try
                            {
                                _httpListener.Prefixes.Remove(_listeningUrl);
                            }
                            catch (Exception ex)
                            {
                                Log($"Failed to remove listener {_listeningUrl} {ex.Message}");
                            }

                            _httpListener?.Abort();
                            _httpListener?.Close();

                        }
                        catch (Exception ex)
                        {
                            Log("Failed to properly shut down http listener " + ex.Message);
                        }
                    }

                    _apiClient?.Dispose();
                }
                finally
                {
                    _httpListener = null;
                    _apiClient = null;
                }
            }
        }
    }
}

/*
 Benchmark notes:
 loadtest -n 1000 -c 100 http://localhost/.well-known/acme-challenge/test123
 1000 Requests, 0.608164001s, 1644 request per second.
*/
