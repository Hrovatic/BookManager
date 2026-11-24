using BookManagerApi.Models;
using BookManagerApi.Helpers;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;


namespace BookManagerApi.Models
{
    public enum IdentifierType
    {
        ISBN = 1,
        ISSN = 2
    }
    public class Book
    {
        [Key]
        public int Id { get; set; }
        public string? ISBN { get; set; }
        public string? ISSN { get; set; }
        public string? IssnNormalized { get; set; }
        public IdentifierType IdentifierType { get; set; }
        public string? Title { get; set; } = "Unknown";
        public string? Author { get; set; } = "Unknown";
        public string? Publisher { get; set; }
        public int? YearPublished { get; set; }
        public string? CoverUrl { get; set; }
        public decimal? Price { get; set; }
        public List<string> Genre { get; set; } = new();

        private string? _language;
        public string? Language
        {
            get => LanguageCodeHelper.NormalizeBookLanguage(_language);
            set => _language = LanguageCodeHelper.NormalizeBookLanguage(value);
        }

        public string CategoryCode { get; set; } = "9.00.00";

        [JsonIgnore]
        public ICollection<BookInventory> Copies { get; set; } = new List<BookInventory>();
    }
}
