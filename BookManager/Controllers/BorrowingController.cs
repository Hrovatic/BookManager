using BookManagerApi.DTOs;
using BookManagerApi.Services;
using BookManagerApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace BookManagerApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BorrowingController : ControllerBase
    {
        private readonly BorrowingService _service;
        private readonly ApplicationDbContext _ctx;

        public BorrowingController(BorrowingService service, ApplicationDbContext ctx)
        {
            _service = service;
            _ctx = ctx;
        }

        [HttpGet("all")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> GetAll() 
            => Ok(await _service.GetAll());

        [HttpGet("my")]
        public async Task<IActionResult> GetMyBorrowings(int userId)
        {
            await _service.RefreshOverdueStatusesAsync(DateTime.UtcNow);

            var items = await _ctx.Borrowings
                .Where(b => b.UserId == userId && b.ReturnedAt == null)
                .ToListAsync();

            return Ok(items);
        }

        [HttpGet("active-by-book/{bookId}")]
        public async Task<IActionResult> GetActiveByBook(int bookId)
        {
            var active = await _service.GetActiveByBookId(bookId);

            return Ok(active);
        }


        [HttpPost("borrow")]
        [Authorize]
        public async Task<IActionResult> Borrow([FromBody] BorrowingCreateDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!int.TryParse(dto.UserId, out var targetUserId))
                return BadRequest("Invalid userId.");

            var requesterIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(requesterIdClaim, out var requesterUserId))
                return Unauthorized();

            var requesterRole = User.FindFirstValue(ClaimTypes.Role);
            var isAdmin = string.Equals(requesterRole, "admin", StringComparison.OrdinalIgnoreCase);

            if (!isAdmin && targetUserId != requesterUserId)
                return Forbid();

            try
            {
                var result = await _service.Borrow(dto);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
        }

        [HttpPost("return")]
        public async Task<IActionResult> ReturnBooks([FromBody] List<int> ids)
        {
            if (ids == null || ids.Count == 0)
                return BadRequest("Ni izbranih izposoj za vračilo.");

            var returnedCount = await _service.ReturnBooks(ids);

            if (returnedCount == 0)
                return NotFound("Ni bilo mogoče vrniti nobene izposoje (ne obstaja ali je že vrnjena).");

            return Ok(new { message = "Knjige uspešno vrnjene", returnedCount });
        }



        [HttpPost("extend")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> ExtendBorrowings([FromBody] List<int> ids) 
            => Ok(await _service.ExtendBorrowings(ids));

        [HttpPost("delete")]
        [Authorize(Roles = "admin")] 
         public async Task<IActionResult> DeleteBorrowings([FromBody] List<int> ids) 
            => Ok(await _service.DeleteBorrowings(ids));

        [HttpGet("last-activities")]
        public async Task<IActionResult> GetLastActivities(int userId, int count = 5)
        {
            var result = await _service.GetLastActivities(userId, count);
            return Ok(result);
        }

        [HttpPost("notify-when-available")]
        [Authorize]
        public async Task<IActionResult> NotifyWhenAvailable([FromBody] BookAvailabilityNotifyRequestDto dto)
        {
            if (dto == null || dto.BookId <= 0)
                return BadRequest("BookId is required.");

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var (ok, message) = await _service.SubscribeAvailabilityAlertAsync(userId, dto.BookId);
            if (!ok) return BadRequest(message);

            return Ok(new { message });
        }
    }
}
