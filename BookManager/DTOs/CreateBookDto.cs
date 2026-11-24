using BookManagerApi.Models;
using System.ComponentModel.DataAnnotations;

namespace BookManagerApi.DTOs
{
    public class CreateBookDto
    {
        [StringLength(13, MinimumLength = 10)]
        public string? ISBN { get; set; }

        [StringLength(8)]
        public string? ISSN { get; set; }

        public IdentifierType IdentifierType { get; set; }

        [Required(ErrorMessage = "Naslov je obvezen.")]
        public string Title { get; set; } = null!;

        [Required(ErrorMessage = "Avtor je obvezen.")]
        public string Author { get; set; } = null!; 

        public string? Publisher { get; set; }

        [Range(1, 2100, ErrorMessage = "Leto mora biti med 1 in 2100.")]
        public int? YearPublished { get; set; }

        [Range(typeof(decimal), "0", "999999999", ErrorMessage = "Cena ne sme biti negativna.")]
        public decimal? Price { get; set; }

        public string? CoverUrl { get; set; }
        public string? Language { get; set; }
        public string? Location { get; set; }
        public string? CategoryCode { get; set; }
        public List<string>? Genre { get; set; }
        public string? IssnVolume { get; set; }
        public string? IssnNumber { get; set; }
    }
}
