namespace BookManagerApi.DTOs
{
    public class AuditLogDto
    {
        public int Id { get; set; }

        public string? Action { get; set; }
        public string? EntityName { get; set; }
        public string? EntityId { get; set; }

        public string? ControllerName { get; set; }
        public string? ActionName { get; set; }

        public string? HttpMethod { get; set; }
        public string? RequestPath { get; set; }

        public int? StatusCode { get; set; }
        public bool Succeeded { get; set; }

        public int? UserId { get; set; }
        public string? Username { get; set; }
        public string? UserRole { get; set; }

        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }

        public string? OldValues { get; set; }
        public string? NewValues { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}