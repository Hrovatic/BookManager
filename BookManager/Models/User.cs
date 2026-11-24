using BookManagerApi.Models;
using System.ComponentModel.DataAnnotations;

namespace BookManagerApi.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }
        public string? Username { get; set; }
        public string? PasswordHash { get; set; }
        public string? Role { get; set; }
        public string? Email { get; set; }
        public string PreferredLanguage { get; set; } = "sl";

        public int? CenterId { get; set;  }
        public Center? Center { get; set; }
    }
}
