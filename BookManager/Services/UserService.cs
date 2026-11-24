using BCrypt.Net;
using BookManagerApi.Data;
using BookManagerApi.DTOs;
using BookManagerApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace BookManagerApi.Services
{
    public interface IUserService
    {
        User? GetByUserNameAndPassword(string username, string password);
        User? GetByUserId(int id);
        Task CreateUser(RegisterDto dto);
        //void CreateAdmin(UserDto dto);
        Task <bool> UserExists(string username);
        Task UpdateUser(User user);
        Task<List<AdminUserRowDto>> GetAllForAdminAsync();

        //ploymorphism
        Task DeleteUser(User user);
        Task DeleteUser(int id);

        List<User?> GetUsers();
        Task<(bool Success, string Message)> AdminResetPasswordAsync(int targetUserId, string newPassword);
        Task<(bool Success, string Message)> ChangeOwnPasswordAsync(int userId, string currentPassword, string newPassword);
        Task<(bool Success, string Message)> AdminDeleteUserAsync(int requesterUserId, int targetUserId);
    }

    public class UserService : IUserService
    {
        private readonly IServiceProvider _provider;
        private readonly PasswordHasher<User> _passwordHasher = new();
        private readonly ApplicationDbContext _context;

        public UserService(IServiceProvider provider, ApplicationDbContext context)
        {
            _provider = provider;
            _context = context;
        }

        public User? GetByUserNameAndPassword(string username, string password)
        {
            try
            {
                using var scope = _provider.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var user = ctx.Users.AsNoTracking().SingleOrDefault(x => x.Username == username);
                if (user != null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                    return user;

                return null;
            }
            catch
            {
                return null;
            }
        }

        public User? GetByUserId(int id)
        {
            try
            {
                using var scope = _provider.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                return ctx.Users.AsNoTracking().SingleOrDefault(x => x.Id == id);
            }
            catch
            {
                return null;
            }
        }

        public async Task CreateUser(RegisterDto dto)
        {
            using var scope = _provider.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (await ctx.Users.AnyAsync(U => U.Username == dto.Username))
                throw new InvalidOperationException("Uporabnik že obstaja");

            if (await ctx.Users.AnyAsync(U => U.Email == dto.Email))
                throw new InvalidOperationException("Email že obstaja");

            if (!await ctx.Center.AnyAsync(c => c.Id == dto.Center))
                throw new InvalidOperationException("Izbrani center ne obstaja");

            var user = new User
            {
                Username = dto.Username,
                Email = dto.Email,
                Role = "user",
                CenterId = dto.Center,
                PreferredLanguage = "sl"
            };

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();
        }

        public async Task UpdateUser(User user)
        {
            try
            {
                using var scope = _provider.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                ctx.Users.Update(user);
                await ctx.SaveChangesAsync();
            }
            catch
            {
            }
        }

        /*public void CreateAdmin(UserDto userDto)
        {
            var user = new User
            {
                Username = userDto.Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(userDto.Password),
                Role = "admin",
                Email = "jure.kos@koscloud.eu"
            };
            _context.Users.Add(user);
            _context.SaveChanges();
        }*/

        public Task DeleteUser(User user)
        {
            return DeleteUser(user.Id);
        }

        public async Task DeleteUser(int id)
        {
            try
            {
                using var scope = _provider.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var user = await ctx.Users.SingleOrDefaultAsync(x => x.Id == id);
                if (user == null) return;

                ctx.Users.Remove(user);
                await ctx.SaveChangesAsync();
            }
            catch
            {
            }
        }

        public async Task<List<AdminUserRowDto>> GetAllForAdminAsync()
        {
            return await _context.Users
                .AsNoTracking()
                .Include(u => u.Center)
                .Select(u => new AdminUserRowDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Email = u.Email,
                    Role = u.Role,
                    CenterId = u.CenterId,
                    CenterName = u.Center != null ? u.Center.Name : null
                })
                .ToListAsync();
        }

        public List<User?> GetUsers()
        {
            try
            {
                using var scope = _provider.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                return ctx.Users.AsNoTracking().ToList();
            }
            catch
            {
                return new List<User?>();
            }
        }

        public async Task<bool> UserExists(string username)
        {
                using var scope = _provider.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                return await ctx.Users.AsNoTracking().AnyAsync(x => x.Username == username);
        }

        public async Task<(bool Success, string Message)> AdminResetPasswordAsync(int targetUserId, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                return (false, "New password is required.");

            var normalized = newPassword.Trim();
            if (normalized.Length < 8)
                return (false, "Password must contain at least 8 characters.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == targetUserId);
            if (user == null)
                return (false, "User not found.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(normalized);
            await _context.SaveChangesAsync();
            return (true, "Password updated.");
        }

        public async Task<(bool Success, string Message)> ChangeOwnPasswordAsync(int userId, string currentPassword, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(currentPassword))
                return (false, "Current password is required.");

            if (string.IsNullOrWhiteSpace(newPassword))
                return (false, "New password is required.");

            var normalizedNew = newPassword.Trim();
            if (normalizedNew.Length < 8)
                return (false, "Password must contain at least 8 characters.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return (false, "User not found.");

            if (string.IsNullOrWhiteSpace(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
                return (false, "Current password is incorrect.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(normalizedNew);
            await _context.SaveChangesAsync();
            return (true, "Password updated.");
        }

        public async Task<(bool Success, string Message)> AdminDeleteUserAsync(int requesterUserId, int targetUserId)
        {
            if (requesterUserId == targetUserId)
                return (false, "You cannot delete your own account.");

            var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == targetUserId);
            if (targetUser == null)
                return (false, "User not found.");

            if (string.Equals(targetUser.Role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                var adminCount = await _context.Users.CountAsync(u => u.Role == "admin");
                if (adminCount <= 1)
                    return (false, "Cannot delete the last admin user.");
            }

            _context.Users.Remove(targetUser);
            try
            {
                await _context.SaveChangesAsync();
                return (true, "User deleted.");
            }
            catch (DbUpdateException)
            {
                return (false, "User cannot be deleted because related records exist.");
            }
        }
    }
}
