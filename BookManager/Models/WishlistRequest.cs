namespace BookManagerApi.Models
{
    public enum WishlistStatus
    {
        New = 0,
        Review = 1,
        Ordered = 2,
        Added = 3,
        Rejected = 4
    }
    public class WishlistRequest
    {
        public int Id { get; set; }
        public int? UserId { get; set; }
        public User? User { get; set; }
        public string? ISBN { get; set; }
        public string? ISSN { get; set; }
        public string? IssnVolume { get; set; }
        public string? IssnNumber { get; set; }
        public string Priority { get; set; }
        public decimal? Price { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string? Language { get; set; }
        public string? Genres { get; set; }
        public string? Note { get; set; }
        public string? FundingSource { get; set; }
        public string? CategoryCode { get; set; }
        public string? OrderNumber { get; set; }
        public int? CenterId { get; set; }
        public Center? Center { get; set; }
        public WishlistStatus Status { get; set; } = WishlistStatus.Review;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool NotifyMe { get; set; } = false;
        public bool Notified { get; set; } = false;
    }
}
