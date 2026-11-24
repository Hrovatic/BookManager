using BookManagerApi.Data;
using BookManagerApi.DTOs;
using BookManagerApi.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace BookManagerApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoriesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private static readonly Regex CategoryCodeInTextRegex = new(@"\b\d{1,2}\.\d{2}\.\d{2}\b", RegexOptions.Compiled);

        public CategoriesController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string? parentCode = null, [FromQuery] int? level = null)
        {
            var q = _db.Categories.AsNoTracking().Where(c => c.IsActive);

            if (!string.IsNullOrWhiteSpace(parentCode))
                q = q.Where(c => c.ParentCode == parentCode || c.Code == parentCode);

            if (level.HasValue)
                q = q.Where(c => c.Level == level.Value);

            var data = await q
                .OrderBy(c => c.Code)
                .Select(c => new { code = c.Code, name = c.Name, level = c.Level, parentCode = c.ParentCode })
                .ToListAsync();

            return Ok(data);
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var data = await (
                from inv in _db.BookCopies.AsNoTracking()
                join b in _db.Books.AsNoTracking() on inv.BookId equals b.Id

                join cat in _db.Categories.AsNoTracking() on b.CategoryCode equals cat.Code into catJoin
                from cat in catJoin.DefaultIfEmpty()
                let categoryMain = b.CategoryCode != null
                    ? (b.CategoryCode.Split('.')[0] + ".00.00")
                    : null

                join mainCat in _db.Categories.AsNoTracking() on categoryMain equals mainCat.Code into mainJoin
                from mainCat in mainJoin.DefaultIfEmpty()

                select new CopyDto
                {
                    Id = inv.Id,
                    BookId = inv.BookId,

                    InventoryNumber = inv.InventoryNumber,
                    Location = inv.Location,
                    Status = inv.IsAvailable,

                    DisplayInventoryNumber = inv.InventoryNumber ?? "",

                    CategoryCode = b.CategoryCode,
                    CategoryMain = categoryMain,

                    CategoryName = cat != null ? cat.Name : null,
                    CategoryMainName = mainCat != null ? mainCat.Name : null
                }
            ).ToListAsync();

            foreach (var x in data)
            {
                var inv = string.IsNullOrWhiteSpace(x.InventoryNumber)
                    ? ""
                    : NormalizeInventoryHelper.NormalizeInventory(x.InventoryNumber);

                var locationCode = TryExtractCategoryCode(x.Location);
                var prefixSourceCode = !string.IsNullOrWhiteSpace(locationCode)
                    ? locationCode
                    : x.CategoryCode;
                var prefix = CategoryPrefixHelper.FromCategoryCode(prefixSourceCode);
                x.InventoryNumber = string.IsNullOrWhiteSpace(inv) ? null : inv;
                x.DisplayInventoryNumber = string.IsNullOrWhiteSpace(inv) ? "" : $"{prefix}-{inv}";
            }

            return Ok(data);
        }

        private static string? TryExtractCategoryCode(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var match = CategoryCodeInTextRegex.Match(text);
            return match.Success ? match.Value : null;
        }

    }
}
