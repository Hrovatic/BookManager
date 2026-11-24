
namespace BookManagerApi.DTOs
{
    public class CreateCopyDto
    {
        public int BookId { get; set; }
        public string? InventoryNumber { get; set; }
        public string? Location { get; set; }
        public string? Condition { get; set; }
        public int? CenterId { get; set; }
    }
}
