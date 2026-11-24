namespace BookManagerApi.DTOs
{
    public class BookGrowthPointDto
    {
        public DateTime PeriodStartUtc { get; set; }
        public int Added { get; set; }
        public int Total { get; set; }
    }
}
