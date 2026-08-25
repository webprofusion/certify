using System;
using System.Runtime.InteropServices;

namespace Certify.Shared
{
    /// <summary>
    /// Shared details for the optional local named pipe transport, used as a secure alternative to
    /// the TCP service API when the client and the service are on the same machine.
    /// </summary>
    public static class NamedPipeConnection
    {
        /// <summary>
        /// Value of <see cref="ServerConnection.Mode"/> which selects the named pipe transport
        /// </summary>
        public const string ConnectionMode = "namedpipe";

        /// <summary>
        /// Authentication scheme used for callers arriving over the named pipe
        /// </summary>
        public const string AuthScheme = "NamedPipeAuthScheme";

        /// <summary>
        /// Environment variable which overrides the pipe name on both the service and the client
        /// </summary>
        public const string PipeNameEnvVariable = "CERTIFY_SERVICE_PIPE_NAME";

        /// <summary>
        /// Value of <see cref="ServiceConfig.Transport"/> selecting the standard http endpoint
        /// </summary>
        public const string HttpMode = "http";

        /// <summary>
        /// Environment variable which overrides <see cref="ServiceConfig.Transport"/>. Intended as a
        /// recovery and development escape hatch, so the transport can be forced without editing
        /// serviceconfig.json on a machine the service is no longer reachable on.
        /// </summary>
        public const string TransportEnvVariable = "CERTIFY_SERVICE_TRANSPORT";

        /// <summary>
        /// True when the named pipe transport can actually be used on this platform. Named pipes are
        /// a windows only feature of Kestrel, so the service publishes http elsewhere regardless of
        /// what the config asks for, and clients have to follow it.
        /// </summary>
        public static bool IsPlatformSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>
        /// Determine whether the named pipe transport is selected by the given service config,
        /// allowing for the environment override. Any value other than "namedpipe" selects http, so
        /// an unrecognised setting fails safe to the default.
        /// </summary>
        public static bool IsNamedPipeTransport(ServiceConfig config)
        {
            var setting = Environment.GetEnvironmentVariable(TransportEnvVariable);

            if (string.IsNullOrWhiteSpace(setting))
            {
                setting = config?.Transport;
            }

            return string.Equals(setting?.Trim(), ConnectionMode, StringComparison.OrdinalIgnoreCase);
        }

#if DEBUG
        // debug builds use their own pipe, mirroring the debug/release service port split, so a
        // development build and an installed service can run side by side
        public const string DefaultPipeName = "certify-service-debug";
#else
        public const string DefaultPipeName = "certify-service";
#endif

        /// <summary>
        /// Host component used for pipe requests. The pipe determines the endpoint so the host is
        /// only present to form a valid absolute uri.
        /// </summary>
        public const string RequestHost = "http://localhost";

        /// <summary>
        /// Get the pipe name to listen on/connect to, allowing for an environment override
        /// </summary>
        public static string GetPipeName()
        {
            var name = Environment.GetEnvironmentVariable(PipeNameEnvVariable);

            return string.IsNullOrWhiteSpace(name) ? DefaultPipeName : name.Trim();
        }
    }
}
