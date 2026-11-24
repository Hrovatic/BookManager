namespace BookManagerApi.DTOs
{
    public class WishlistCreateDto
    {
        public string? ISBN { get; set; }
        public string? ISSN { get; set; }
        public string? IssnVolume { get; set; }
        public string? IssnNumber { get; set; }
        public string Priority { get; set; }
        public decimal? Price { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string? Language { get; set; }
        public List<string>? Genres { get; set; }
        public string? Note { get; set; }
        public string? FundingSource { get; set; }
        public string? CategoryCode { get; set; }
        public string? OrderNumber { get; set; }
        public bool NotifyMe { get; set; }
        public int? CenterId { get; set; }
    }
}
