namespace BookManagerApi.DTOs
{
    public class CreateAuditLogDto
    {
        public string Action { get; set; } = string.Empty;
        public string? EntityName { get; set; }
        public string? EntityId { get; set; }
        public string? DetailsJson { get; set; }
    }
}
