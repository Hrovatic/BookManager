using BookManagerApi.Models;
using System.ComponentModel.DataAnnotations;

namespace BookManagerApi.DTOs
{
    public class WishlistUpdateDto
    {
        public string? ISBN { get; set; }
        public string? ISSN { get; set; }
        public string? IssnVolume { get; set; }
        public string? IssnNumber { get; set; }
        [Required]public string? Title { get; set; }
        [Required]public string? Author { get; set; }
        public string? Note { get; set; }
        public string? FundingSource { get; set; }
        public string? CategoryCode { get; set; }
        public string? OrderNumber { get; set; }
        public string? Language { get; set; }
        public WishlistStatus? Status { get; set; }
        public List<string>? Genres {  get; set; }
        public string? Priority { get; set; }
        public decimal? Price { get; set; }
        public bool? NotifyMe { get; set; }
    }
}
