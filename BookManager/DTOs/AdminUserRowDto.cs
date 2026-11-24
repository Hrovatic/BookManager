namespace BookManagerApi.DTOs
{
    public class AdminUserRowDto
    {
        public int Id { get; set; }
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
        public int? CenterId { get; set; }
        public string? CenterName { get; set; }
    }
}
