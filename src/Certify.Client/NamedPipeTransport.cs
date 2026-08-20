using System;
using System.Net.Http;

#if NET8_0_OR_GREATER
using System.IO.Pipes;
using System.Security.Principal;
#endif

namespace Certify.Client
{
    /// <summary>
    /// Creates HTTP message handlers which tunnel requests to the local service over a named pipe
    /// instead of TCP. Used for both the service API and the status hub (SignalR) connection.
    /// </summary>
    internal static class NamedPipeTransport
    {
        /// <summary>
        /// True if the current runtime can connect to the service over a named pipe
        /// </summary>
        public static bool IsSupported
        {
#if NET8_0_OR_GREATER
            get => true;
#else
            get => false;
#endif
        }

        /// <summary>
        /// Create a message handler which opens a new named pipe connection for each HTTP connection
        /// </summary>
        /// <param name="pipeName">Name of the pipe published by the service</param>
        public static HttpMessageHandler CreateHandler(string pipeName)
        {
#if NET8_0_OR_GREATER
            // https://andrewlock.net/using-named-pipes-with-aspnetcore-and-httpclient/
            return new SocketsHttpHandler
            {
                // called each time the handler needs to open a new connection
                ConnectCallback = async (ctx, ct) =>
                {
                    var pipeClientStream = new NamedPipeClientStream(
                        serverName: ".", // this machine only
                        pipeName: pipeName,
                        direction: PipeDirection.InOut, // duplex stream
                        options: PipeOptions.Asynchronous,

                        // the service needs to identify us to apply role claims, but must not be able
                        // to act as us. This limits the damage if another process manages to squat the
                        // pipe name before the service starts.
                        impersonationLevel: TokenImpersonationLevel.Identification);

                    try
                    {
                        await pipeClientStream.ConnectAsync(ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        pipeClientStream.Dispose();
                        throw;
                    }

                    return pipeClientStream;
                }
            };
#else
            throw new PlatformNotSupportedException($"Named pipe connections to the Certify service require .NET 8 or later. Pipe: {pipeName}");
#endif
        }
    }
}
