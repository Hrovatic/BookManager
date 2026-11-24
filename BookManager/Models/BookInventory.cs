using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace BookManagerApi.Models
{
    public class BookInventory
    {
        public int Id { get; set; }

        // FK -> Book
        public int BookId { get; set; }
        [JsonIgnore]
        public Book Book { get; set; } = null!;

        //Inventar del
        public string? InventoryNumber { get; set; }   // npr. nalepka
        public string? OrderNumber { get; set; }
        public string? IssnVolume { get; set; }
        public string? IssnNumber { get; set; }
        public string? Location { get; set; }           // npr. Polica A3
        public string? Condition { get; set; }           
        public int? CenterId { get; set; }
        public Center? Center { get; set; }
        public DateTime? CreatedAt { get; set; }

        public bool IsAvailable { get; set; } = true;
        public ICollection<Borrowing> Borrowings { get; set; } = new List<Borrowing>();
    }
}
