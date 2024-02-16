using System.Collections.Generic;
using Certify.Models.Config.AccessControl;

namespace Certify.Models.API
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

        public Models.Config.AccessControl.SecurityPrinciple? SecurityPrinciple { get; set; }

        public RoleStatus? RoleStatus { get; set; }
    }

    public class SecurityPrinciplePasswordCheck
    {
        public string SecurityPrincipleId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        public SecurityPrinciplePasswordCheck() { }
        public SecurityPrinciplePasswordCheck(string securityPrincipleId, string password)
        {
            SecurityPrincipleId = securityPrincipleId;
            Password = password;
        }
    }

    public class SecurityPrincipleCheckResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public SecurityPrinciple? SecurityPrinciple { get; set; }
    }

    public class SecurityPrinciplePasswordUpdate
    {
        public string SecurityPrincipleId { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;

        public SecurityPrinciplePasswordUpdate() { }
        public SecurityPrinciplePasswordUpdate(string securityPrincipleId, string password, string newPassword)
        {
            SecurityPrincipleId = securityPrincipleId;
            Password = password;
            NewPassword = newPassword;
        }
    }
}
