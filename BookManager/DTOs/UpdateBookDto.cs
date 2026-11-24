using System.ComponentModel.DataAnnotations;

namespace BookManagerApi.DTOs
{
    public class UpdateBookDto
    {
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? Publisher { get; set; }
        public int? YearPublished { get; set; }
        [Range(typeof(decimal), "0", "999999999", ErrorMessage = "Cena ne sme biti negativna.")]
        public decimal? Price { get; set; }
        public string? CoverUrl { get; set; }
        public string? Language { get; set; }
        public List<string>? Genre { get; set; }
    }
}
