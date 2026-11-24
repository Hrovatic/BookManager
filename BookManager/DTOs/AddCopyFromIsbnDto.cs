using System.ComponentModel.DataAnnotations;

namespace BookManagerApi.DTOs
{
    public class AddCopyFromIsbnDto
    {
        public string Isbn { get; set; } = null!;
        public string? InventoryNumber { get; set; }
        public string? IssnVolume { get; set; }
        public string? IssnNumber { get; set; }
        public string? Location { get; set; }
        public string? CategoryCode { get; set; }
        public string? Condition { get; set; }

        [Range(typeof(decimal), "0", "999999999", ErrorMessage = "Cena ne sme biti negativna.")]
        public decimal? Price { get; set; }

        public int? CenterId { get; set; }
    }
}
