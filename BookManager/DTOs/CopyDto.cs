
namespace BookManagerApi.DTOs
{
    public class CopyDto
    {
        public int Id { get; set; }
        public int BookId { get; set; }
        public string? InventoryNumber { get; set; }
        public string? OrderNumber { get; set; }
        public string? IssnVolume { get; set; }
        public string? IssnNumber { get; set; }
        public string? Location { get; set; }
        public int? CenterId { get; set; }
        public string? CenterName { get; set; }
        public bool Status { get; set; }
        public string DisplayInventoryNumber { get; set; } = null!; // "T-00034" primer
        public string? CategoryCode { get; set; }
        public string? CategoryMain { get; set; }
        public string? CategoryName { get; set; }
        public string? CategoryMainName {  get; set; }
    }
}
