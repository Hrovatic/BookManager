namespace BookManagerApi.DTOs
{
    public class ChangeOwnPasswordDto
    {
        public string? CurrentPassword { get; set; }
        public string? NewPassword { get; set; }
        public string? ConfirmPassword { get; set; }
    }
}
