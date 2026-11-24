using BookManagerApi.Data;
using BookManagerApi.DTOs;
using BookManagerApi.Helpers;
using BookManagerApi.Models;
using BookManagerApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.Security.Claims;
using System.Linq;
using System.Text.Json;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace BookManagerApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BookController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IBookService _books;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public BookController(IBookService books, ApplicationDbContext db, IWebHostEnvironment env, IConfiguration config)
        {
            _books = books;
            _db = db;
            _env = env;
            _config = config;
        }

        [HttpGet("isbn/{isbn}")]
        [Authorize(Roles = "admin")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> GetByIsbn(string isbn)
        {
            var book = await _books.GetByIsbn(isbn);
            if (book == null) return NotFound();

            return Ok(book);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var b = await _db.Books.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (b == null) return NotFound();

            return Ok(new
            {
                id = b.Id,
                isbn = b.ISBN,
                issn = b.ISSN,
                identifierType = b.IdentifierType,
                title = b.Title,
                author = b.Author,
                publisher = b.Publisher,
                yearPublished = b.YearPublished,
                price = b.Price,
                coverUrl = b.CoverUrl,
                language = b.Language,
                genre = GenreHelper.CleanGenres(b.Genre),
                categoryCode = b.CategoryCode
            });
        }

        [HttpGet("vse")]
        public async Task<IActionResult> GetAll()
        {
            var books = await _books.GetAll();
            return Ok(books);
        }

        [HttpPost("{id:int}/cover")]
        [Authorize(Roles = "admin")]
        [ApiExplorerSettings(IgnoreApi = true)]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(10_000_000)]
        public async Task<IActionResult> UploadCover(int id, [FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File is required.");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowed = new HashSet<string> { ".jpg", ".jpeg", ".png", ".webp" };
            if (!allowed.Contains(ext))
                return BadRequest("Unsupported file type.");

            var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == id);
            if (book == null) return NotFound();

            var storageRoot = _env.IsDevelopment() ? _config["CoverStoragePath"] : Environment.GetEnvironmentVariable("APP_STORAGE_PATH");
            if (string.IsNullOrWhiteSpace(storageRoot))
                return BadRequest("Cover storage is not configured.");

            try
            {
                Directory.CreateDirectory(storageRoot);
            }
            catch (IOException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Cover storage path is unavailable.");
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Cover storage path is not accessible.");
            }

            var fileName = $"book_{id}_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(storageRoot, fileName);
            var thumbFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_thumb.webp";
            var thumbPath = Path.Combine(storageRoot, thumbFileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
            try
            {
                using var image = await Image.LoadAsync(filePath);
                image.Mutate(x => x
                    .AutoOrient()
                    .Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(240, 0)
                    })
                );

                var encoder = new WebpEncoder { Quality = 65 };
                await image.SaveAsync(thumbPath, encoder);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cover thumbnail generation failed for book {id}: {ex.Message}");
            }

            book.CoverUrl = $"/covers/{fileName}";
            await _db.SaveChangesAsync();

            return Ok(new { coverUrl = book.CoverUrl, coverThumbUrl = $"/covers/{thumbFileName}" });
        }

        [HttpPost("vnos")]
        [Authorize(Roles = "admin")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> Create(CreateBookDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int? reviewedByUserId = int.TryParse(userIdRaw, out var parsed) ? parsed : null;

            var book = await _books.CreateBook(dto, reviewedByUserId);
            if (book == null) return Conflict("Book with this ISBN already exists.");

            return CreatedAtAction(nameof(GetById), new { id = book.Id }, book);
        }

        [HttpPut("{id:int}/category")]
        [Authorize(Roles = "admin")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> SetCategory(int id, [FromBody] SetCategoryDto dto)
        {
            try
            {
                var ok = await _books.SetCategoryAsync(id, dto.CategoryCode);
                return ok ? NoContent() : NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

                [HttpGet("lookup/{code}")]
        [Authorize(Roles = "admin")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> Lookup(string code)
        {
            try
            {
                var (book, created) = await _books.LookupWithCreated(code);
                if (book == null) return NotFound();

                return created
                    ? StatusCode(StatusCodes.Status201Created, book)
                    : Ok(book);
            }
            catch (IssnProviderUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    code = "ISSN_PROVIDER_UNAVAILABLE",
                    message = ex.Message
                });
            }
        }
[HttpPut("edit/{id}")]
        [Authorize(Roles = "admin")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> Update(int id, UpdateBookDto dto)
        {
            var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int? reviewedByUserId = int.TryParse(userIdRaw, out var parsed) ? parsed : null;

            var ok = await _books.UpdateBook(id, dto, reviewedByUserId);
            if (!ok) return NotFound();

            return NoContent();
        }


        [HttpDelete("delete/{id}")]
        [Authorize(Roles = "admin")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> Delete(int id)
        {
            var ok = await _books.DeleteBook(id);
            if (!ok) return NotFound();

            return NoContent();
        }

        [HttpPost("normalize-languages")]
        [Authorize(Roles = "admin")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> NormalizeLanguages()
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var conn = _db.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync();

                var dbTx = tx.GetDbTransaction();
                var booksUpdated = await NormalizeLanguageColumnAsync(conn, dbTx, "books");
                var wishlistUpdated = await NormalizeLanguageColumnAsync(conn, dbTx, "wishlist_requests");

                await tx.CommitAsync();

                return Ok(new
                {
                    booksUpdated,
                    wishlistUpdated,
                    totalUpdated = booksUpdated + wishlistUpdated
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

        [HttpPost("normalize-categories-from-inventory")]
        [Authorize(Roles = "admin")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> NormalizeCategoriesFromInventory()
        {
            var updated = await _books.NormalizeCategoriesFromInventoryAsync();
            return Ok(new { updated });
        }

        private static async Task<int> NormalizeLanguageColumnAsync(
            DbConnection conn,
            DbTransaction tx,
            string tableName)
        {
            var updates = new List<(int Id, string NormalizedLanguage)>();

            await using (var selectCmd = conn.CreateCommand())
            {
                selectCmd.Transaction = tx;
                selectCmd.CommandText = $"SELECT id, language FROM {tableName} WHERE language IS NOT NULL AND btrim(language) <> ''";

                await using var reader = await selectCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var id = reader.GetInt32(0);
                    var raw = reader.IsDBNull(1) ? null : reader.GetString(1);
                    var normalized = LanguageCodeHelper.NormalizeBookLanguage(raw);
                    if (string.IsNullOrWhiteSpace(normalized))
                        continue;

                    var trimmedRaw = raw?.Trim() ?? string.Empty;
                    if (string.Equals(trimmedRaw, normalized, StringComparison.Ordinal))
                        continue;

                    updates.Add((id, normalized));
                }
            }

            if (updates.Count == 0)
                return 0;

            var updated = 0;
            await using var updateCmd = conn.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText = $"UPDATE {tableName} SET language = @lang WHERE id = @id AND language IS DISTINCT FROM @lang";

            var idParam = updateCmd.CreateParameter();
            idParam.ParameterName = "@id";
            updateCmd.Parameters.Add(idParam);

            var langParam = updateCmd.CreateParameter();
            langParam.ParameterName = "@lang";
            updateCmd.Parameters.Add(langParam);

            foreach (var row in updates)
            {
                idParam.Value = row.Id;
                langParam.Value = row.NormalizedLanguage;
                updated += await updateCmd.ExecuteNonQueryAsync();
            }

            return updated;
        }

        [HttpGet("related")]
        public async Task<IActionResult> GetRelated(
            [FromQuery] List<string> genre,
            [FromQuery] int? excludeId,
            [FromQuery] int take = 3)
        {
            var queryGenres = GenreHelper.CleanGenres(genre);
            if (queryGenres.Count == 0)
                return BadRequest("genre is required");

            var candidates = await _db.Books.AsNoTracking()
                .Where(b => !excludeId.HasValue || b.Id != excludeId.Value)
                .OrderByDescending(b => b.Id)
                .ToListAsync();

            var related = candidates
                .Select(b => new
                {
                    Book = b,
                    CleanedGenres = GenreHelper.CleanGenres(b.Genre)
                })
                .Where(x => x.CleanedGenres.Any(g =>
                    queryGenres.Contains(g, StringComparer.OrdinalIgnoreCase)))
                .Take(Math.Max(1, take))
                .Select(x => new
                {
                    id = x.Book.Id,
                    title = x.Book.Title,
                    author = x.Book.Author,
                    genre = x.CleanedGenres
                })
                .ToList();

            return Ok(related);
        }
    }
}


