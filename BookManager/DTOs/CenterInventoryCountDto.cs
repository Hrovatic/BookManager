namespace BookManagerApi.DTOs
{
    public class CenterInventoryCountDto
    {
        public int? CenterId { get; set; }
        public string CenterName { get; set; } = "";
        public int CopyCount { get; set; }
        public int TitleCount { get; set; }
    }
}
