namespace BookManagerApi.DTOs
{
    public class CenterPriceDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
        public int UsersCount { get; set; }
    }
}
