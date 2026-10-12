using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Certify.Models.Hub
{
    /// <summary>
    /// The management hub's local admin channel: break-glass administration for an operating system administrator on
    /// the hub machine, served over a named pipe (Windows) or a Unix domain socket rather than the network.
    ///
    /// This file is also compiled into the hub CLI, which does not reference the rest of Certify.Models, so it must only
    /// depend on the base class library.
    /// </summary>
    public static class LocalAdminChannel
    {
        public const string RoutePrefix = "/local-admin/v1";

        public const string PrincipalsRoute = RoutePrefix + "/principals";
        public const string PasswordResetRoute = RoutePrefix + "/principals/password-reset";
        public const string GrantAdministratorRoute = RoutePrefix + "/principals/grant-admin";
        public const string CreateAdministratorRoute = RoutePrefix + "/principals/create-admin";
        public const string EnablePasswordLoginRoute = RoutePrefix + "/settings/enable-password-login";

        /// <summary>
        /// Set to "false" to stop the hub serving the local admin channel.
        /// </summary>
        public const string EnabledVariable = "CERTIFY_HUB_LOCAL_ADMIN";

        public const string PipeNameVariable = "CERTIFY_HUB_ADMIN_PIPE_NAME";

        /// <summary>
        /// Distinct from the agent service's pipe, so a hub and an agent can run on the same machine.
        /// </summary>
        public const string DefaultPipeName = "certify-hub-admin";

        public const string SocketPathVariable = "CERTIFY_HUB_ADMIN_SOCKET";

        public const string SocketFileName = "admin.sock";

        /// <summary>
        /// Where the socket lives when the hub runs under systemd with RuntimeDirectory=certify-hub.
        /// </summary>
        public const string SystemdSocketPath = "/run/certify-hub/" + SocketFileName;

        /// <summary>
        /// Where the socket lives otherwise, e.g. in a container where the service user cannot write to /run.
        /// </summary>
        public const string FallbackSocketPath = "/tmp/certify-hub/" + SocketFileName;

        public static bool IsNamedPipeTransport => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public static string GetPipeName()
        {
            var name = Environment.GetEnvironmentVariable(PipeNameVariable);
            return string.IsNullOrWhiteSpace(name) ? DefaultPipeName : name.Trim();
        }

        /// <summary>
        /// The socket path the hub binds: an explicit setting, else the systemd runtime directory, else the fallback.
        /// </summary>
        public static string GetServerSocketPath()
        {
            var configured = Environment.GetEnvironmentVariable(SocketPathVariable);

            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim();
            }

            // systemd sets RUNTIME_DIRECTORY for the service, as a colon separated list when several are declared
            var runtimeDirectory = Environment.GetEnvironmentVariable("RUNTIME_DIRECTORY")?.Split(':')[0];

            if (!string.IsNullOrWhiteSpace(runtimeDirectory))
            {
                return Path.Combine(runtimeDirectory, SocketFileName);
            }

            return FallbackSocketPath;
        }

        /// <summary>
        /// Socket paths a client tries, in order. A client does not see the service's RUNTIME_DIRECTORY, so it tries the
        /// standard locations rather than resolving the same way as the hub.
        /// </summary>
        public static IEnumerable<string> GetClientSocketPathCandidates()
        {
            var configured = Environment.GetEnvironmentVariable(SocketPathVariable);

            if (!string.IsNullOrWhiteSpace(configured))
            {
                yield return configured.Trim();
                yield break;
            }

            yield return SystemdSocketPath;
            yield return FallbackSocketPath;
        }
    }

    /// <summary>
    /// A security principal as listed over the local admin channel. Never carries a password hash.
    /// </summary>
    public class LocalAdminPrincipalSummary
    {
        public string Id { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string? Title { get; set; }
        public string? PrincipalType { get; set; }
        public string? Provider { get; set; }
        public bool IsBuiltIn { get; set; }
        public bool IsAdministrator { get; set; }
    }

    /// <summary>
    /// Identifies a security principal by id or by username.
    /// </summary>
    public class LocalAdminPrincipalRequest
    {
        public string Principal { get; set; } = string.Empty;
    }

    public class LocalAdminCreateAdministratorRequest
    {
        public string Username { get; set; } = string.Empty;
        public string? Title { get; set; }
    }

    /// <summary>
    /// Outcome of a local admin operation.
    ///
    /// Passwords are only ever generated by the hub and returned here, never sent to it, so a process impersonating the
    /// channel endpoint (e.g. one which created the pipe or socket while the hub was stopped) cannot capture one.
    /// </summary>
    public class LocalAdminResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? PrincipalId { get; set; }
        public string? Username { get; set; }

        /// <summary>
        /// A newly generated password, shown to the administrator once.
        /// </summary>
        public string? GeneratedPassword { get; set; }
    }
}
