using BookManagerApi.DTOs;

namespace BookManagerApi.DTOs
{
    public class BulkCenterPriceDto
    {
        public List<CenterPriceItemDto> Items { get; set; } = new();

    }
}
