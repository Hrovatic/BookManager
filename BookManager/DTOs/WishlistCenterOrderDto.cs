namespace BookManagerApi.DTOs
{
    public class WishlistCenterOrderDto
    {
        public int CenterId { get; set; }
        public int Status { get; set; }
        public string? ISBN { get; set; }
        public string? Title { get; set; }
        public string? Author { get; set; }
    }
}
