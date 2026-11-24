namespace BookManagerApi.Models
{
    public class Notifications
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Message { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool Seen { get; set; } = false;
    }
}
