
namespace BookManagerApi.Models
{
    public class Center
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
        public ICollection<User> Users { get; set; } = new List<User>();
        public ICollection<BookInventory> BookCopies { get; set; } = new List<BookInventory>();
        
    }
}
