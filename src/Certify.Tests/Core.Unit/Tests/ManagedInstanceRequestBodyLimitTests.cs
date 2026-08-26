using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class ManagedInstanceRequestBodyLimitTests
    {
        private static ManagedInstanceRequestAuthBodyHashMiddleware CreateMiddleware(Action onNextCalled)
        {
            return new ManagedInstanceRequestAuthBodyHashMiddleware(_ =>
            {
                onNextCalled();
                return Task.CompletedTask;
            });
        }

        private static DefaultHttpContext CreateContext(byte[] body, bool setContentLength, bool includeInstanceHeader = true)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/v1/managedchallenge/request";
            context.Request.Body = new MemoryStream(body);

            if (setContentLength)
            {
                context.Request.ContentLength = body.Length;
            }

            if (includeInstanceHeader)
            {
                context.Request.Headers[ManagedInstanceRequestAuth.HubAssignedIdHeaderName] = "instance-1";
            }

            return context;
        }

        [TestMethod]
        public async Task BodyHashMiddleware_RejectsOversizedBody_ByDeclaredContentLength()
        {
            // the middleware runs before authentication, so an anonymous caller must not be able to make the hub
            // buffer an unbounded body simply by presenting the instance id header
            var nextCalled = false;
            var middleware = CreateMiddleware(() => nextCalled = true);

            var context = CreateContext(Encoding.UTF8.GetBytes("{}"), setContentLength: false);
            context.Request.ContentLength = ManagedInstanceRequestBodyLimits.MaxBodyBytes + 1;

            await middleware.InvokeAsync(context);

            Assert.AreEqual(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
            Assert.IsFalse(nextCalled, "An oversized request must not reach the rest of the pipeline.");
        }

        [TestMethod]
        public async Task BodyHashMiddleware_HashesBodyWithinLimit()
        {
            var nextCalled = false;
            var middleware = CreateMiddleware(() => nextCalled = true);

            var body = Encoding.UTF8.GetBytes("{\"value\":1}");
            var context = CreateContext(body, setContentLength: true);

            await middleware.InvokeAsync(context);

            Assert.IsTrue(nextCalled);
            Assert.AreEqual(
                ManagedInstanceRequestAuth.ComputeBodyHash(body),
                context.Items[ManagedInstanceRequestAuth.CachedBodyHashItemKey]);
        }

        [TestMethod]
        public async Task BodyHashMiddleware_RewindsBodyForDownstreamHandlers()
        {
            string? readByNext = null;

            var middleware = new ManagedInstanceRequestAuthBodyHashMiddleware(async ctx =>
            {
                using var reader = new StreamReader(ctx.Request.Body);
                readByNext = await reader.ReadToEndAsync();
            });

            var requestBody = "{\"value\":1}";
            var context = CreateContext(Encoding.UTF8.GetBytes(requestBody), setContentLength: true);

            await middleware.InvokeAsync(context);

            Assert.AreEqual(requestBody, readByNext, "The request body must still be readable after being hashed.");
        }

        [TestMethod]
        public async Task BodyHashMiddleware_IgnoresRequestsWithoutInstanceHeader()
        {
            var nextCalled = false;
            var middleware = CreateMiddleware(() => nextCalled = true);

            var context = CreateContext(Encoding.UTF8.GetBytes("{}"), setContentLength: true, includeInstanceHeader: false);

            await middleware.InvokeAsync(context);

            Assert.IsTrue(nextCalled);
            Assert.IsFalse(context.Items.ContainsKey(ManagedInstanceRequestAuth.CachedBodyHashItemKey));
        }
    }
}
