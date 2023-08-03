using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Certify.Models.Providers;

namespace Certify.Providers.ACME.Anvil
{
    /// <summary>
    /// Logging Http Handler
    /// </summary>
    public class LoggingHandler : DelegatingHandler
    {
        private ILog _log = null;

        public LoggingHandler(HttpMessageHandler innerHandler, ILog log)
            : base(innerHandler)
        {
            _log = log;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_log != null)
            {
                _log.Debug("Http Request: {request}", request);

                if (request.Content != null)
                {
                    _log.Debug("Content: {content}", await request.Content.ReadAsStringAsync());
                }
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (_log != null)
            {
                _log.Debug("Http Response: {response}", response);

                if (response.Content != null)
                {
                    _log.Debug("Content: {content}", await response.Content.ReadAsStringAsync());
                }
            }

            return response;
        }
    }
}
