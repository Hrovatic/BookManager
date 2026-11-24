using BookManagerApi.Data;
using BookManagerApi.DTOs;
using BookManagerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BookManagerApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class InventoryController : ControllerBase
    {
        private readonly ICopiesService _copiesService;
        private readonly ApplicationDbContext _db;

        public InventoryController(ICopiesService copiesService, ApplicationDbContext db)
        {
            _copiesService = copiesService;
            _db = db;
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var data = await _copiesService.GetAllAsync();
            return Ok(data);
        }


        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var copy = await _copiesService.GetByIdAsync(id);
            return copy is null ? NotFound() : Ok(copy);
        }

        [HttpGet("allcopies")]
        public async Task<IActionResult> GetAll([FromQuery] int? bookId)
        {
            var data = await _copiesService.GetAllAsync(bookId);
            return Ok(data);
        }

        [HttpGet("by-book/{bookId:int}")]
        public async Task<IActionResult> GetByBookId(int bookId)
        {
            var copy = await _copiesService.GetByBookIdAsync(bookId);
            return copy is null ? NotFound() : Ok(copy);
        }

        [HttpPost("copy")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Create([FromBody] CreateCopyDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var created = await _copiesService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }

        [HttpPost("from-isbn")]
        public async Task<IActionResult> AddCopyFromIsbn([FromBody] AddCopyFromIsbnDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Isbn))
                return BadRequest("ISBN is required.");

            try
            {
                var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
                int? reviewedByUserId = int.TryParse(userIdRaw, out var parsed) ? parsed : null;

                var created = await _copiesService.AddCopyFromIsbnAsync(dto, reviewedByUserId);
                return Ok(created);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("next-inventory")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> GetNextInventoryNumber()
        {
            var next = await _copiesService.GetNextInventoryNumberAsync();
            return Ok(new { inventoryNumber = next });
        }

        [HttpPatch("{id}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateCopy(int id, [FromBody] UpdateCopyDto dto)
        {
            try
            {
                var updated = await _copiesService.UpdateAsync(id, dto);
                if (updated == null) return NotFound();

                var condition = await _db.BookCopies
                    .Where(c => c.Id == id)
                    .Select(c => c.Condition)
                    .FirstOrDefaultAsync();

                return Ok(new
                {
                    id = updated.Id,
                    bookId = updated.BookId,
                    inventoryNumber = updated.InventoryNumber,
                    location = updated.Location,
                    condition,
                    centerId = updated.CenterId,
                    centerName = updated.CenterName,
                    isAvailable = updated.Status
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            var copy = await _db.BookCopies
                .Include(c => c.Borrowings)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (copy == null) return NotFound();

            // ne briši, če je kopija trenutno izposojena
            var hasActiveBorrowing = copy.Borrowings.Any(b => b.ReturnedAt == null);
            if (hasActiveBorrowing)
                return BadRequest("Kopije ni možno izbrisati, ker je trenutno izposojena.");

            var bookId = copy.BookId;

            // 1) izbriši kopijo
            _db.BookCopies.Remove(copy);
            await _db.SaveChangesAsync();

            // 2) če ni več kopij -> izbriši še knjigo
            var deletedBook = false;
            var anyCopiesLeft = await _db.BookCopies.AnyAsync(c => c.BookId == bookId);

            if (!anyCopiesLeft)
            {
                var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId);
                if (book != null)
                {
                    _db.Books.Remove(book);
                    await _db.SaveChangesAsync();
                    deletedBook = true;
                }
            }

            await tx.CommitAsync();

            return Ok(new { deletedBook });
        }

        [HttpGet("availability")]
        public async Task<IActionResult> GetAvailability([FromQuery] int bookId)
        {
            if (bookId <= 0) return BadRequest("bookId is required.");

            var result = await _copiesService.GetAvailabilityAsync(bookId);
            return Ok(result);
        }

        [HttpGet("availability/all")]
        public async Task<IActionResult> GetAvailabilityAll()
            => Ok(await _copiesService.GetAvailabilityAllAsync());

        [HttpGet("counts-by-center")]
        public async Task<IActionResult> GetCountsByCenter()
            => Ok(await _copiesService.GetCountsByCenterAsync());
    }
}
