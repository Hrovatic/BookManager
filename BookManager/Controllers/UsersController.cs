using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Runtime.InteropServices;
using BookManagerApi.Models;
using BookManagerApi.Services;
using BookManagerApi.DTOs;
using Microsoft.AspNetCore.Identity;
using BookManagerApi.Data;
using System.Security.Claims;

namespace BookManagerApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userAccountService;
        private readonly ApplicationDbContext _db;

        public UsersController(
            IUserService userAccountService,
            ApplicationDbContext db)
        {
            _userAccountService = userAccountService;
            _db = db;
        }


        [HttpGet(Name = "GetUsers")]
        [Authorize(Roles = "admin")]
        public IActionResult GetUsers()
        {
            try
            {
                List<User?> list = _userAccountService.GetUsers();
                return Ok(list);
            }
            catch (Exception ex)
            {
                Console.Write(ex);
                return BadRequest("Error");
            }
        }

        [HttpGet("{id:int}")]
        [Authorize(Roles = "admin")]
        public IActionResult GetUser(int id)
        {
            try
            {
                User user = _userAccountService.GetByUserId(id);
                return Ok(user);
            }
            catch (Exception ex)
            {
                Console.Write(ex);
                return BadRequest("Error");
            }
        }

        [HttpPost("register")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto) 
        { 
            try 
            { 
                await _userAccountService.CreateUser(dto); 
                return Ok(); 
            } 
            catch (InvalidOperationException ex) 
            { 
                return Conflict(ex.Message); 
            } 
            catch (Exception ex) 
            { 
                Console.WriteLine(ex); 
                return BadRequest("Error"); 
            } 
        }

        [HttpPost("signup")]
        [AllowAnonymous]
        public async Task<IActionResult> Signup([FromBody] PublicSignupDto dto)
        {
            if (dto == null)
            {
                return BadRequest("Missing signup payload.");
            }

            dto.Username = (dto.Username ?? "").Trim();
            dto.Email = (dto.Email ?? "").Trim();

            if (string.IsNullOrWhiteSpace(dto.Username) ||
                string.IsNullOrWhiteSpace(dto.Email) ||
                string.IsNullOrWhiteSpace(dto.Password) ||
                string.IsNullOrWhiteSpace(dto.ConfirmPassword) ||
                dto.Center <= 0)
            {
                return BadRequest("All fields are required.");
            }

            if (dto.Password != dto.ConfirmPassword)
            {
                return BadRequest("Passwords do not match.");
            }

            if (dto.Password.Length < 8)
            {
                return BadRequest("Password must contain at least 8 characters.");
            }

            if (!await _db.Center.AsNoTracking().AnyAsync(c => c.Id == dto.Center))
            {
                return BadRequest("Selected center is invalid.");
            }

            if (await _db.Users.AsNoTracking().AnyAsync(u => u.Username == dto.Username))
            {
                return Conflict("User already exists.");
            }

            if (await _db.Users.AsNoTracking().AnyAsync(u => u.Email == dto.Email))
            {
                return Conflict("Email already exists.");
            }

            try
            {
                await _userAccountService.CreateUser(new RegisterDto
                {
                    Username = dto.Username,
                    Email = dto.Email,
                    Password = dto.Password,
                    Center = dto.Center
                });

                return Ok();
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return BadRequest("Error");
            }
        }


        [HttpPost("update")]
        [Authorize(Roles = "admin")]
        public IActionResult UpdateUser([FromBody] User request)
        {
            try
            {
                _userAccountService.UpdateUser(request);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.Write(ex);
                return BadRequest("Error");
            }
        }

        [HttpPost("delete")]
        [Authorize(Roles = "admin")]
        public IActionResult DeleteUser([FromBody] User request)
        {
            try
            {
                _userAccountService.DeleteUser(request);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.Write(ex);
                return BadRequest("Error");
            }
        }

        [HttpPost("exists")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> CheckUserExists([FromBody] User request)
        {
            try
            {
                bool exists = await _userAccountService.UserExists(request.Username);
                return Ok(exists);
            }
            catch (Exception ex)
            {
                Console.Write(ex);
                return BadRequest("Error");
            }
        }

        [HttpGet("all")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> GetAll()
        {
            var data = await _userAccountService.GetAllForAdminAsync();
            return Ok(data);
        }

        [HttpDelete("admin/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteUserByAdmin(int id)
        {
            var adminIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(adminIdClaim, out var adminId))
                return Unauthorized();

            var result = await _userAccountService.AdminDeleteUserAsync(adminId, id);
            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(new { message = result.Message });
        }

        [HttpPost("admin/{id:int}/password")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> AdminResetPassword(int id, [FromBody] AdminResetPasswordDto dto)
        {
            var newPassword = (dto?.NewPassword ?? "").Trim();
            if (string.IsNullOrWhiteSpace(newPassword))
                return BadRequest("New password is required.");

            var result = await _userAccountService.AdminResetPasswordAsync(id, newPassword);
            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(new { message = result.Message });
        }

        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangeOwnPassword([FromBody] ChangeOwnPasswordDto dto)
        {
            var currentPassword = dto?.CurrentPassword ?? "";
            var newPassword = dto?.NewPassword ?? "";
            var confirmPassword = dto?.ConfirmPassword ?? "";

            if (string.IsNullOrWhiteSpace(currentPassword) ||
                string.IsNullOrWhiteSpace(newPassword) ||
                string.IsNullOrWhiteSpace(confirmPassword))
            {
                return BadRequest("All password fields are required.");
            }

            if (newPassword != confirmPassword)
                return BadRequest("New password and confirmation do not match.");

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var result = await _userAccountService.ChangeOwnPasswordAsync(userId, currentPassword, newPassword);
            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(new { message = result.Message });
        }

        [HttpPost("preferred-language")]
        [Authorize]
        public async Task<IActionResult> SetPreferredLanguage([FromBody] UserPreferredLanguageDto dto)
        {
            var lang = (dto?.Language ?? "").Trim().ToLowerInvariant();
            if (lang != "sl" && lang != "en")
                return BadRequest("Language must be 'sl' or 'en'.");

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound("User not found.");

            user.PreferredLanguage = lang;
            await _db.SaveChangesAsync();

            return Ok(new { language = lang });
        }
    }
}
