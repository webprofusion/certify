using System.IO;
using Certify.Models.Hub;

namespace Certify.Server.Hub.Api.Middleware
{
    public class ManagedInstanceRequestAuthBodyHashMiddleware
    {
        private readonly RequestDelegate _next;

        public ManagedInstanceRequestAuthBodyHashMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Headers.ContainsKey(ManagedInstanceRequestAuth.HubAssignedIdHeaderName))
            {
                context.Request.EnableBuffering();

                if (context.Request.Body.CanSeek)
                {
                    context.Request.Body.Position = 0;
                }

                using (var ms = new MemoryStream())
                {
                    await context.Request.Body.CopyToAsync(ms, context.RequestAborted);
                    context.Items[ManagedInstanceRequestAuth.CachedBodyHashItemKey] = ManagedInstanceRequestAuth.ComputeBodyHash(ms.ToArray());
                }

                if (context.Request.Body.CanSeek)
                {
                    context.Request.Body.Position = 0;
                }
            }

            await _next(context);
        }
    }
}
