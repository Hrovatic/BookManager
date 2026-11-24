namespace BookManagerApi.DTOs
{
    public class WishlistAddedWithPriceDto
    {
        public List<int> WishlistIds { get; set; } = new();
        public int Year { get; set; }
        public decimal? Price { get; set; }
        public string? CategoryCode { get; set; }
        public string? Location { get; set; }
        public string? OrderNumber { get; set; }
        public int? CenterId { get; set; }
    }
}
