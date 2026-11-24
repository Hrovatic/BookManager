namespace BookManagerApi.DTOs
{
    public class WishlistPriceReviewCreateDto
    {
        public List<int> WishlistIds { get; set; } = new();
        public int Year { get; set; }
        public decimal? Price { get; set; }
    }
}
