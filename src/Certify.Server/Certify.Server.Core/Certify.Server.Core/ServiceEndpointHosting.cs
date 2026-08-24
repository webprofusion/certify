using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Certify.Shared;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;

namespace Certify.Server.Core
{
    /// <summary>
    /// Decides which endpoints the service listens on: the standard http endpoint and/or the optional
    /// local named pipe, offered as a secure alternative for clients on the same machine (desktop app, CLI).
    /// </summary>
    internal static class ServiceEndpointHosting
    {
        /// <summary>
        /// Access control for the service pipe. The pipe is the primary access boundary for this
        /// transport, so only the service account itself and local administrators may open it.
        /// </summary>
        /// <remarks>
        /// Note that a non-elevated member of the administrators group holds that SID as deny-only,
        /// so an unelevated process fails this check and cannot open the pipe at all.
        /// </remarks>
        [SupportedOSPlatform("windows")]
        public static PipeSecurity CreatePipeSecurity()
        {
            // starts with an empty DACL, which denies everyone, we then add only the identities we want
            var security = new PipeSecurity();

            var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

            // the service normally runs as LocalSystem and needs to create further pipe instances
            security.AddAccessRule(new PipeAccessRule(localSystem, PipeAccessRights.FullControl, AccessControlType.Allow));

            // elevated admin clients (desktop app, CLI). CreateNewInstance is included so the service
            // can still create pipe instances when run interactively as an admin during development.
            security.AddAccessRule(new PipeAccessRule(
                administrators,
                PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow));

            // windows exposes named pipes remotely through the IPC$ share, so an administrator on
            // another machine would otherwise satisfy the rule above. This endpoint is only ever
            // intended for callers on this machine, and a remote caller's token carries the NETWORK
            // sid, so deny it explicitly. Deny ACEs are evaluated before allow ACEs.
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Deny));

            return security;
        }

        /// <summary>
        /// Publish the local named pipe as the service's only endpoint
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static string ConfigureNamedPipeEndpoint(WebApplicationBuilder builder)
        {
            var pipeName = NamedPipeConnection.GetPipeName();

            // touch the windows identity types once at startup. Resolving a pipe caller happens under
            // Identification level impersonation, where the thread token cannot open files, so any
            // assembly loaded lazily at that point would fail to load.
            using (var warmup = WindowsIdentity.GetCurrent())
            {
                _ = warmup.IsAuthenticated;
                _ = warmup.Claims.Any();
            }

            builder.WebHost.UseNamedPipes(opts =>
            {
                // the service runs as LocalSystem while clients run as an elevated interactive user,
                // so the default same-user-and-elevation restriction can never be satisfied here. The
                // explicit ACL below takes its place.
                opts.CurrentUserOnly = false;
                opts.PipeSecurity = CreatePipeSecurity();
            });

            // https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints
            builder.WebHost.ConfigureKestrel(opts =>
            {
                // endpoints declared under Kestrel:Endpoints (appsettings-core.json) are bound in
                // addition to anything set here, and take precedence over UseUrls, so not calling
                // UseUrls is not by itself enough to disable http. Replacing the configuration loader
                // with an empty one drops those endpoints whatever they happen to be named.
                opts.Configure(new ConfigurationBuilder().Build());

                opts.ListenNamedPipe(pipeName);
            });

            return pipeName;
        }
    }
}
