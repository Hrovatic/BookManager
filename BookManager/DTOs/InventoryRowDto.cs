namespace BookManagerApi.DTOs
{
    public class InventoryRowDto
    {
        public int Id { get; set; }
        public int BookId { get; set; }
        public string? InventoryNumber { get; set; }
        public string? OrderNumber { get; set; }
        public string? IssnVolume { get; set; }
        public string? IssnNumber { get; set; }
        public string? Location { get; set; }    
        public string? CenterName { get; set; }
        public string? BorrowedBy { get; set; }         // null če ni izposojeno
        public string? BorrowedByCenter { get; set; }
        public bool IsAvailable { get; set; }

        public string? BookTitle { get; set; }
        public string? BookAuthor { get; set; }
        public string? BookIsbn { get; set; }
    }
}
