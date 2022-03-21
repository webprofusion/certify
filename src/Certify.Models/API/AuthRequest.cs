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
    }
}
