namespace BookManagerApi.DTOs
{
    public class TopSubcategoryDto
    {
        public string CategoryCode { get; set; } = "";
        public string? CategoryName { get; set; }
        public int AddedCopies { get; set; }
    }
}
