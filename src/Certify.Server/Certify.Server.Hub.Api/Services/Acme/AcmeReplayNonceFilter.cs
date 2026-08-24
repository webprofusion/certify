using Microsoft.AspNetCore.Mvc.Filters;

namespace Certify.Server.Hub.Api.Services.Acme
{
    /// <summary>
    /// RFC 8555 Section 6.5 - every ACME response carries a fresh replay nonce, including error
    /// responses. Nonces are consumed before the request signature is checked, so without a
    /// replacement nonce on the error a client which hits badNonce (or any other failure) cannot
    /// retry without a separate new-nonce round trip.
    /// </summary>
    public class AcmeReplayNonceFilter : IAsyncResultFilter
    {
        private readonly AcmeHelper _acmeHelper;

        public AcmeReplayNonceFilter(AcmeHelper acmeHelper)
        {
            _acmeHelper = acmeHelper ?? throw new ArgumentNullException(nameof(acmeHelper));
        }

        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            var response = context.HttpContext.Response;

            if (!response.HasStarted)
            {
                response.Headers["Replay-Nonce"] = await _acmeHelper.GenerateNonce();
            }

            await next();
        }
    }
}
