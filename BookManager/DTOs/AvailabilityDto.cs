namespace BookManagerApi.DTOs
{
    public class AvailabilityDto
    {
        public int BookId { get; set; }
        public int Total { get; set; }
        public int Available { get; set; }

        public bool IsAvailable => Available > 0;
    }
}
