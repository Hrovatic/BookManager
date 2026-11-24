using System.ComponentModel.DataAnnotations;

namespace BookManagerApi.Models
{
    public class BookAvailabilityNotification
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public int BookId { get; set; }
        public Book Book { get; set; } = null!;

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? NotifiedAtUtc { get; set; }
    }
}

