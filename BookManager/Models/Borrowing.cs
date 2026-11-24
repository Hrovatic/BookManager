
namespace BookManagerApi.Models
{
    public class Borrowing
    {
        public int Id { get; set; }
        public int BookInventoryId { get; set; }
        public BookInventory BookInventory { get; set; } = null!;
        public string BookTitle { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public DateTime BorrowedAt { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? ReturnedAt { get; set; }
        public string Status { get; set; } // active, overdue, returned, borrowed
    }
}
