namespace BookManagerApi.DTOs
{
    public class BorrowingActiveDto
    {
        public int BorrowingId { get; set; }
        public int BookId { get; set; }
        public string? BookTitle { get; set; }
        public string? UserName { get; set; }

        public DateTime BorrowedAt { get; set; }
        public DateTime DueDate { get; set; }
        public string Status { get; set; } = "active";
    }
}
