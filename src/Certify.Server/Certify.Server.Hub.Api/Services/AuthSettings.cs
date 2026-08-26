namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Sign in options for the hub API, configured under an AuthSettings section of appsettings.json / hubservice.json.
    /// </summary>
    /// <remarks>
    /// A hub which federates identity to an OIDC provider generally wants the built in username/password login turned
    /// off, so that the provider remains the only way in and local credentials cannot be used to bypass it.
    /// </remarks>
    public static class AuthSettings
    {
        private const string ConfigSection = "AuthSettings";

        /// <summary>
        /// Whether the username/password login endpoint is available. Defaults to true, so an existing install keeps
        /// working when the setting is absent.
        /// </summary>
        /// <remarks>
        /// The setting is honoured as configured. It is not automatically re-enabled when no OIDC provider is
        /// configured, because silently restoring password login would defeat the point of setting it, so an operator
        /// disabling this must have a working OIDC provider (or edit the setting back) to sign in.
        /// </remarks>
        public static bool IsPasswordLoginEnabled(IConfiguration config)
        {
            return config.GetSection(ConfigSection).GetValue<bool?>("enablePasswordLogin") ?? true;
        }
    }
}
