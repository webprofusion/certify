using System.ComponentModel.DataAnnotations;

namespace Certify.Server.Api.Public.Models
{
    /// <summary>
    /// Required info to begin auth
    /// </summary>
    public class AuthRequest
    {
        /// <summary>
        /// Username to authenticate with
        /// </summary>
        [Required]
        public string Username { get; set; }

        /// <summary>
        /// Password to authenticate with
        /// </summary>
        [Required]
        public string Password { get; set; }

    }

    /// <summary>
    /// Response info for an auth operation
    /// </summary>
    public class AuthResponse
    {
        /// <summary>
        /// String providing summary message
        /// </summary>
        public string Detail { get; set; }

        /// <summary>
        /// Access token string
        /// </summary>
        public string AccessToken { get; set; }

        /// <summary>
        /// Refresh token string
        /// </summary>
        public string RefreshToken { get; set; }
    }
}
