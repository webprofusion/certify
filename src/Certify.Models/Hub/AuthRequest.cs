namespace Certify.Models.Hub
{
    /// <summary>
    /// Required info to begin auth
    /// </summary>
    public class AuthRequest
    {
        /// <summary>
        /// Username to authenticate with
        /// </summary>

        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Password to authenticate with
        /// </summary>
        public string Password { get; set; } = string.Empty;

    }

    /// <summary>
    /// Response info for an auth operation
    /// </summary>
    public class AuthResponse
    {
        /// <summary>
        /// String providing summary message
        /// </summary>
        public string Detail { get; set; } = string.Empty;

        /// <summary>
        /// Access token string
        /// </summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>
        /// Refresh token string
        /// </summary>
        public string RefreshToken { get; set; } = string.Empty;

        public SecurityPrincipal? SecurityPrincipal { get; set; }

        public RoleStatus? RoleStatus { get; set; }
    }

    public class SecurityPrincipalPasswordCheck
    {
        public string SecurityPrincipalId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        public SecurityPrincipalPasswordCheck() { }
        public SecurityPrincipalPasswordCheck(string securityPrincipalId, string password)
        {
            SecurityPrincipalId = securityPrincipalId;
            Password = password;
        }
    }

    public class SecurityPrincipalCheckResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public SecurityPrincipal? SecurityPrincipal { get; set; }
    }

    public class SecurityPrincipalPasswordUpdate
    {
        public string SecurityPrincipalId { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;

        public SecurityPrincipalPasswordUpdate() { }
        public SecurityPrincipalPasswordUpdate(string securityPrincipalId, string password, string newPassword)
        {
            SecurityPrincipalId = securityPrincipalId;
            Password = password;
            NewPassword = newPassword;
        }
    }

    public class ClientSecret
    {
        public string ClientId { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;
    }

    public class HubJoiningClientSecret : ClientSecret
    {
        public string Url { get; set; } = string.Empty;
    }
}
