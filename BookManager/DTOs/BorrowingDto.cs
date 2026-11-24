namespace BookManagerApi.DTOs
{
    public class BorrowingDto
    {
        public int Id { get; set; }
        public string BookTitle { get; set; }
        public string UserName { get; set; }
        public DateTime BorrowedAt { get; set; }
        public DateTime DueDate { get; set; }
        public string Status { get; set; }
    }
}
