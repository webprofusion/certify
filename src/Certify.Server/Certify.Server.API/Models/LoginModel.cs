using System.ComponentModel.DataAnnotations;

namespace Certify.Server.Api.Public.Models
{
    public class LoginModel
    {
        [Required]
        public string Username { get; set; }

        [Required]
        public string Password { get; set; }

    }
}
