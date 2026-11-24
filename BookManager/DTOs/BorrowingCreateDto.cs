namespace BookManagerApi.DTOs
{
    public class BorrowingCreateDto
    {
        public int BookId { get; set; }
        public string UserId { get; set; } = string.Empty;
    }
}
