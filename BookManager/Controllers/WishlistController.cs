using BookManagerApi.DTOs;
using BookManagerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using static BookManagerApi.Services.WishlistService;

namespace BookManagerApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WishlistController : ControllerBase
    {
        private readonly WishlistService _wishlistService;
        private readonly IBookService _bookService;

        public WishlistController(WishlistService wishlistService, IBookService bookService)
        {
            _wishlistService = wishlistService;
            _bookService = bookService;
        }

        [HttpPost("add")]
        public async Task<IActionResult> Add([FromBody] WishlistCreateDto dto)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr)) return Unauthorized();

            var userId = int.Parse(userIdStr);
            try
            {
                await _wishlistService.AddRequest(dto, userId); // userId obvezno naprej
                return Ok();
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("all")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> GetAll()
        {
            var data = await _wishlistService.GetAllRequests();
            return Ok(data);
        }

        [HttpGet("center-orders")]
        [Authorize]
        public async Task<IActionResult> GetCenterOrders()
        {
            var data = await _wishlistService.GetCenterOrdersAsync();
            return Ok(data);
        }


        [HttpGet("mine")]
        [Authorize]
        public async Task<IActionResult> GetMine()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var items = await _wishlistService.GetMineRequests(userId);
            return Ok(items);
        }

        [HttpGet("by-isbn/{isbn}")]
        [Authorize]
        public async Task<IActionResult> GetByIsbn(string isbn)
        {
            var book = await _bookService.LookupByIsbnNoPersist(isbn);
            if (book == null) return NotFound();

            return Ok(new
            {
                id = book.Id,
                isbn = book.ISBN,
                issn = book.ISSN,
                identifierType = (int)book.IdentifierType,
                title = book.Title,
                author = book.Author,
                publisher = book.Publisher,
                yearPublished = book.YearPublished,
                language = book.Language,
                genre = book.Genre
            });
        }

        [HttpGet("by-issn/{issn}")]
        [Authorize]
        public async Task<IActionResult> GetByIssn(string issn)
        {
            try
            {
                var book = await _bookService.LookupByIssnNoPersist(issn);
                if (book == null) return NotFound();

                return Ok(new
                {
                    id = book.Id,
                    isbn = book.ISBN,
                    issn = book.ISSN,
                    identifierType = (int)book.IdentifierType,
                    title = book.Title,
                    author = book.Author,
                    publisher = book.Publisher,
                    yearPublished = book.YearPublished,
                    language = book.Language,
                    genre = book.Genre
                });
            }
            catch (IssnProviderUnavailableException ex)
            {
                return StatusCode(503, ex.Message);
            }
        }

        [HttpGet("{id:int}")]
        [Authorize]
        public async Task<IActionResult> GetById(int id)
        {
            var wishlist = await _wishlistService.GetByIdAsync(id);
            if (wishlist == null)
                return NotFound();

            var isAdmin = User.IsInRole("admin");
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // varnostna kontrola ostane v controllerju
            if (!isAdmin && wishlist.UserId != userId)
                return Forbid();

            return Ok(wishlist);
        }


        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var success = await _wishlistService.DeleteRequest(id);
            if (!success) return NotFound();
            return Ok();
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update([FromRoute] int id, [FromBody] WishlistUpdateDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var ok = await _wishlistService.Update(id, dto);
            if (!ok) return NotFound();
            return Ok();
        }


        [HttpPost("status/{id}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateStatus(
            int id,
            [FromBody] WishlistStatusUpdateDto dto)
        {
            try
            {
                await _wishlistService.UpdateStatusAsync(id, dto.Status);
                return Ok();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("bulk-status")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> BulkStatus([FromBody] WishlistBulkStatusDto dto)
        {
            if (dto.Ids == null || dto.Ids.Count == 0)
                return BadRequest("No wishlist items selected.");

            var updated = await _wishlistService.BulkUpdateStatusAsync(
                dto.Ids,
                dto.Status,
                dto.CategoryCode,
                dto.OrderNumber
            );
            return Ok(new { updated });
        }

        [HttpPost("added-with-prices")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> AddedWithPrices([FromBody] WishlistAddedWithPriceDto dto)
        {
            if (dto?.WishlistIds == null || dto.WishlistIds.Count == 0)
                return BadRequest("No wishlist items selected.");

            if (dto.Year < 1900 || dto.Year > DateTime.UtcNow.Year + 5)
                return BadRequest("Invalid year.");

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int? userId = null;
            if (!string.IsNullOrWhiteSpace(userIdStr))
                userId = int.Parse(userIdStr);

            var updated = await _wishlistService.AddToInventoryWithPricesAsync(dto, userId);
            return Ok(new { updated });
        }

        [HttpGet("price-reviews")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> GetPriceReviews([FromQuery] int? year)
        {
            var data = await _wishlistService.GetPriceReviewsAsync(year);
            return Ok(data);
        }

        [HttpGet("price-reviews/export")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> ExportPriceReviews([FromQuery] int? year)
        {
            var bytes = await _wishlistService.ExportPriceReviewsXlsxAsync(year);
            var fileName = year.HasValue
                ? $"wishlist-price-reviews-{year.Value}.xlsx"
                : $"wishlist-price-reviews-{DateTime.UtcNow:yyyy-MM-dd}.xlsx";
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        [HttpPost("price-reviews")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpsertPriceReviews([FromBody] WishlistPriceReviewCreateDto dto)
        {
            if (dto?.WishlistIds == null || dto.WishlistIds.Count == 0)
                return BadRequest("No wishlist items selected.");

            if (dto.Year < 1900 || dto.Year > DateTime.UtcNow.Year + 5)
                return BadRequest("Invalid year.");

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int? userId = null;
            if (!string.IsNullOrWhiteSpace(userIdStr))
                userId = int.Parse(userIdStr);

            var updated = await _wishlistService.UpsertPriceReviewsAsync(dto.WishlistIds, dto.Year, dto.Price, userId);
            return Ok(new { updated });
        }

        [HttpPost("bulk-delete")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> BulkDelete([FromBody] BulkIdsDto dto)
        {
            if (dto?.Ids == null || dto.Ids.Count == 0)
                return BadRequest("No items selected.");

            var deleted = await _wishlistService.BulkDeleteAsync(dto.Ids);
            return Ok(new { deleted });
        }

    }
}
