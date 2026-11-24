using System.ComponentModel.DataAnnotations;

namespace BookManagerApi.Models
{
    public class Category
    {
        // npr. "2.07.00"
        [Key]
        public string Code { get; set; } = "";

        // npr. "Računalništvo in informatika"
        public string Name { get; set; } = "";

        // 1 = main (2.00.00), 2 = sub (2.07.00)
        public int Level { get; set; }

        // npr. "2.00.00" za "2.07.00"
        public string? ParentCode { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
