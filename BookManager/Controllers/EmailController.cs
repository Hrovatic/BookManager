using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookManagerApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmailController : ControllerBase
    {
        private readonly IEmailService _email;

        public EmailController(IEmailService email)
        {
            _email = email;
        }

        //[HttpPost("test")]
        //[Authorize(Roles = "admin")]
        //public IActionResult SendTest([FromQuery] string to)
        //{
        //    if (string.IsNullOrWhiteSpace(to))
        //        return BadRequest("Missing 'to' email.");

        //    _email.SendEmail(to, "Test email", "<b>Email service works.</b>");
        //    return Ok();
        //}
    }
}
