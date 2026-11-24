using BookManagerApi.DTOs;
using BookManagerApi.Models;
using BookManagerApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BookManagerApi.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IUserService _userAccountService;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;

        public AuthController(IUserService userAccountService, IConfiguration configuration, IHostEnvironment environment   )
        {
            _userAccountService = userAccountService;
            _configuration = configuration;
            _environment = environment;
        }

        
        /*[HttpGet("create-admin")]
        public IActionResult Register()
        {
            UserDto request = new UserDto
            {
                Username = "admin",
                Password = "admin"
            };
            _userAccountService.CreateAdmin(request);
            return Ok();
        }*/
        

        [HttpPost("login")]
        public IActionResult Login([FromBody] UserDto request)
        {
            try
            {
                var user = _userAccountService.GetByUserNameAndPassword(request.Username, request.Password);

                if (user == null)
                    return Unauthorized("Napačno uporabniško ime ali geslo.");

                string token = CreateJwt(user);
                return Ok(token);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, "Error");
            }
        }

        private string CreateJwt(User user)
        {
            List<Claim> claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(ClaimTypes.Email, user.Email)
            };

            var jwtKey = "";
            if (_environment.IsDevelopment())
            {
                jwtKey = _configuration.GetSection("Jwt:Key").Value;
            }
            else
            {
                jwtKey = Environment.GetEnvironmentVariable("JWT_KEY");
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

            var token = new JwtSecurityToken(
                _configuration["Jwt:Issuer"],
                _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddDays(31),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);

        }
    }
}
