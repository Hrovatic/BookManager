namespace BookManagerApi.Models
{
    public class WishlistPriceReview
    {
        public const string SourceWishlist = "wishlist";
        public const string SourceIsbn = "isbn";
        public const string SourceManual = "manual";
        public const string SourceEdited = "edited";

        public int Id { get; set; }
        public int? WishlistRequestId { get; set; }
        public WishlistRequest? WishlistRequest { get; set; }
        public int Year { get; set; }
        public decimal? Price { get; set; }
        public DateTime ReviewedAtUtc { get; set; } = DateTime.UtcNow;
        public int? ReviewedByUserId { get; set; }
        public User? ReviewedByUser { get; set; }

        // snapshot fields (persist after wishlist is deleted)
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? ISBN { get; set; }
        public string? OrderNumber { get; set; }
        public int? CenterId { get; set; }
        public string? Username { get; set; }
        public string? Source { get; set; }
    }
}
