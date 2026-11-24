namespace BookManagerApi.DTOs
{
    public class WishlistPriceReviewDto
    {
        public int Id { get; set; }
        public int? WishlistRequestId { get; set; }
        public int Year { get; set; }
        public decimal? Price { get; set; }
        public DateTime ReviewedAtUtc { get; set; }
        public int? ReviewedByUserId { get; set; }
        public string? ReviewedByUsername { get; set; }
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? ISBN { get; set; }
        public string? OrderNumber { get; set; }
        public int? CenterId { get; set; }
        public string? Username { get; set; }
        public string? Source { get; set; }
    }
}
