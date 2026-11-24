using BookManagerApi.Data;
using BookManagerApi.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace BookManagerApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "admin")]
    public class AuditLogController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AuditLogController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var logs = await _context.AuditLogs
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => new AuditLogDto
                {
                    Id = x.Id,
                    Action = x.Action,
                    EntityName = x.EntityName,
                    EntityId = x.EntityId,

                    ControllerName = x.ControllerName,
                    ActionName = x.ActionName,

                    HttpMethod = x.HttpMethod,
                    RequestPath = x.RequestPath,

                    StatusCode = x.StatusCode,
                    Succeeded = x.Succeeded,

                    UserId = x.UserId,
                    Username = x.Username,
                    UserRole = x.UserRole,

                    IpAddress = x.IpAddress,
                    UserAgent = x.UserAgent,

                    OldValues = x.OldValues,
                    NewValues = x.NewValues,

                    CreatedAtUtc = x.CreatedAtUtc
                })
                .ToListAsync();

            return Ok(logs);
        }
    }
}