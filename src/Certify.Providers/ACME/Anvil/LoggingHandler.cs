using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Certify.Models.Providers;

namespace Certify.Providers.ACME.Anvil
{
    /// <summary>
    /// Logging Http Handler with max request rate throttling
    /// </summary>
    public class LoggingHandler : DelegatingHandler
    {
        private ILog _log = null;
        private SemaphoreSlim _throttle = new SemaphoreSlim(initialCount: 1);
        private int _maxRequestPerSecond = 2;
        private int _maxThrottleWaitMS = 5000;
        private DateTimeOffset _lastRequestTime = DateTimeOffset.MinValue;

        public LoggingHandler(HttpMessageHandler innerHandler, ILog log, int maxRequestsPerSecond)
            : base(innerHandler)
        {
            _log = log;
            _maxRequestPerSecond = maxRequestsPerSecond;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await _throttle.WaitAsync(_maxThrottleWaitMS);

            var requestTimeSpan = DateTimeOffset.Now - _lastRequestTime;
            _lastRequestTime = DateTimeOffset.UtcNow;

            var maxFrequencyMS = 1000 / _maxRequestPerSecond;
            if (requestTimeSpan.TotalMilliseconds < maxFrequencyMS)
            {
                await Task.Delay(maxFrequencyMS, cancellationToken);
            }

            try
            {
                if (_log != null)
                {
                    _log.Debug("Http Request: {request}", request);

                    if (request.Content != null)
                    {
                        try
                        {
                            _log.Debug("Content: {content}", await request.Content.ReadAsStringAsync());
                        }
                        catch
                        {
                            // response stream may already be disposed on error
                            _log.Debug("Content: <content stream already disposed>");
                        }
                    }
                }

                var response = await base.SendAsync(request, cancellationToken);

                if (_log != null)
                {
                    _log.Debug("Http Response: {response}", response);

                    if (response?.Content != null)
                    {
                        try
                        {  
                            _log.Debug("Content: {content}", await response.Content.ReadAsStringAsync());
                        }
                        catch
                        {
                            // response stream may already be disposed on error
                            _log.Debug("Content: <content stream already disposed>");
                        }
                    }
                }

                return response;
            }
            finally
            {
                try
                {
                    _throttle.Release();
                }
                catch (Exception ex)
                {
                    // throttle semaphore release exception. 
                    _log.Error(ex, "LoggingHandler: exception during throttle semaphore release.");
                }
            }
        }
    }
}
