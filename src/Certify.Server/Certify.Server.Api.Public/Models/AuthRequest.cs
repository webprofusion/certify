using System.ComponentModel.DataAnnotations;

namespace Certify.Server.Api.Public.Models
{
    public class AuthRequest
    {
        [Required]
        public string Username { get; set; }

        [Required]
        public string Password { get; set; }

    }
    public class AuthResponse
    {
        public string Detail { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
    }
}
