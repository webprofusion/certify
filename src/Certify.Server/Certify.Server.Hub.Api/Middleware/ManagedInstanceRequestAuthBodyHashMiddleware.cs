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
            // This runs before authentication and is triggered by an unauthenticated caller simply presenting the hub
            // assigned id header, so the body it buffers has to be bounded. Without a limit an anonymous request can
            // make the hub buffer an arbitrarily large body into memory and a temp file before anything checks who
            // sent it.
            if (context.Request.Headers.ContainsKey(ManagedInstanceRequestAuth.HubAssignedIdHeaderName))
            {
                if (context.Request.ContentLength > ManagedInstanceRequestBodyLimits.MaxBodyBytes)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }

                context.Request.EnableBuffering(
                    ManagedInstanceRequestBodyLimits.MemoryBufferThresholdBytes,
                    ManagedInstanceRequestBodyLimits.MaxBodyBytes);

                if (context.Request.Body.CanSeek)
                {
                    context.Request.Body.Position = 0;
                }

                try
                {
                    using (var ms = new MemoryStream())
                    {
                        await context.Request.Body.CopyToAsync(ms, context.RequestAborted);
                        context.Items[ManagedInstanceRequestAuth.CachedBodyHashItemKey] = ManagedInstanceRequestAuth.ComputeBodyHash(ms.ToArray());
                    }
                }
                catch (IOException)
                {
                    // raised by the buffering stream when the body exceeds the limit without having declared an
                    // accurate content length
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }

                if (context.Request.Body.CanSeek)
                {
                    context.Request.Body.Position = 0;
                }
            }

            await _next(context);
        }
    }

    /// <summary>
    /// Size limits applied when buffering a managed instance request body in order to hash it for signature validation.
    /// </summary>
    public static class ManagedInstanceRequestBodyLimits
    {
        /// <summary>
        /// Bytes held in memory before a buffered body spills to a temp file.
        /// </summary>
        public const int MemoryBufferThresholdBytes = 64 * 1024;

        /// <summary>
        /// Largest signed managed instance request body which will be buffered and hashed.
        /// </summary>
        public const long MaxBodyBytes = 30 * 1024 * 1024;
    }
}
