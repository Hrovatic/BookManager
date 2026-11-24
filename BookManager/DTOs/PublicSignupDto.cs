namespace BookManagerApi.DTOs
{
    public class PublicSignupDto
    {
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public string ConfirmPassword { get; set; } = "";
        public int Center { get; set; }
    }
}
