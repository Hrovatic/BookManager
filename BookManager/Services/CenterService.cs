using BookManagerApi.Data;
using BookManagerApi.DTOs;
using BookManagerApi.Models;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Npgsql;

namespace BookManagerApi.Services
{
    public interface ICenterService
    {
        Task<List<CenterLookupDto>> GetCentersAsync();
        Task<CenterLookupDto> CreateCenterAsync(string name);
        Task<CenterLookupDto?> UpdateCenterAsync(int id, string name);
        Task<bool> DeleteCenterAsync(int id);
        Task<int> BulkUpdatePricesAsync(List<CenterPriceItemDto> items);
        Task<List<CenterPriceDto>> GetCenterPricesAsync();
        Task<byte[]> ExportCenterPricesXlsxAsync();
        Task<int> ResetPricesAsync();
    }
    public class CenterService : ICenterService
    {
        private readonly ApplicationDbContext _ctx;

        public CenterService(ApplicationDbContext ctx)
        {
            _ctx = ctx;
        }

        public async Task<List<CenterLookupDto>> GetCentersAsync()
        {
            return await _ctx.Center
                .AsNoTracking()
                .OrderBy(c => c.Id)
                .Select(c => new CenterLookupDto
                {
                    Id = c.Id,
                    Name = c.Name
                })
                .ToListAsync();
        }

        public async Task<CenterLookupDto> CreateCenterAsync(string name)
        {
            var cleaned = NormalizeCenterName(name);

            var exists = await _ctx.Center
                .AnyAsync(c => c.Name.ToLower() == cleaned.ToLower());
            if (exists)
                throw new InvalidOperationException("Center with this name already exists.");

            var center = new Center
            {
                Name = cleaned,
                Price = 0m
            };

            _ctx.Center.Add(center);
            try
            {
                await _ctx.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsCenterPrimaryKeyViolation(ex))
            {
                await SyncCenterIdSequenceAsync();
                await _ctx.SaveChangesAsync();
            }

            return new CenterLookupDto
            {
                Id = center.Id,
                Name = center.Name
            };
        }

        public async Task<CenterLookupDto?> UpdateCenterAsync(int id, string name)
        {
            var center = await _ctx.Center.FirstOrDefaultAsync(c => c.Id == id);
            if (center is null) return null;

            var cleaned = NormalizeCenterName(name);
            var exists = await _ctx.Center
                .AnyAsync(c => c.Id != id && c.Name.ToLower() == cleaned.ToLower());
            if (exists)
                throw new InvalidOperationException("Center with this name already exists.");

            center.Name = cleaned;
            await _ctx.SaveChangesAsync();

            return new CenterLookupDto
            {
                Id = center.Id,
                Name = center.Name
            };
        }

        public async Task<bool> DeleteCenterAsync(int id)
        {
            var center = await _ctx.Center.FirstOrDefaultAsync(c => c.Id == id);
            if (center is null) return false;

            var isUsedByUsers = await _ctx.Users.AnyAsync(u => u.CenterId == id);
            var isUsedByCopies = await _ctx.BookCopies.AnyAsync(c => c.CenterId == id);
            var isUsedByWishlist = await _ctx.WishlistRequests.AnyAsync(w => w.CenterId == id);

            if (isUsedByUsers || isUsedByCopies || isUsedByWishlist)
                throw new InvalidOperationException("Center cannot be deleted because it is still in use.");

            _ctx.Center.Remove(center);
            await _ctx.SaveChangesAsync();
            return true;
        }

        public async Task<int> BulkUpdatePricesAsync(List<CenterPriceItemDto> items)
        {
            // vzemi samo veljavne vnose (npr. null ali <=0 ignoriraj po želji)
            var clean = items
                .Where(x => x.CenterId > 0 && x.Price.HasValue)
                .Select(x => new CenterPriceItemDto { CenterId = x.CenterId, Price = x.Price.Value })
                .ToList();

            if (clean.Count == 0) return 0;

            var sumByCenter = clean
                .GroupBy(x => x.CenterId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Price!.Value));

            var centerIds = sumByCenter.Keys.ToList();

            var centers = await _ctx.Center
                .Where(c => centerIds.Contains(c.Id))
                .ToListAsync();

            foreach (var c in centers)
            {
                c.Price += sumByCenter[c.Id];            
            }

            await _ctx.SaveChangesAsync();
            return centers.Count;
        }

        public async Task<List<CenterPriceDto>> GetCenterPricesAsync()
        {
            return await _ctx.Center
                .AsNoTracking()
                .OrderBy(c => c.Id)
                .Select(c => new CenterPriceDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Price = c.Price,
                    UsersCount = c.Users.Count,
                })
                .ToListAsync();
        }

        public async Task<byte[]> ExportCenterPricesXlsxAsync()
        {
            var centers = await _ctx.Center
                .AsNoTracking()
                .OrderBy(c => c.Id)
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Price
                })
                .ToListAsync();

            var users = await _ctx.Users
                .AsNoTracking()
                .Where(u => u.CenterId != null)
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.CenterId
                })
                .ToListAsync();

            var addedCounts = await _ctx.WishlistRequests
                .AsNoTracking()
                .Where(w => w.Status == WishlistStatus.Added && w.UserId != null)
                .GroupBy(w => w.UserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    Count = g.Count()
                })
                .ToDictionaryAsync(x => x.UserId, x => x.Count);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Price per center");

            ws.Cell(1, 1).Value = "CenterId";
            ws.Cell(1, 2).Value = "Center";
            ws.Cell(1, 3).Value = "User";
            ws.Cell(1, 4).Value = "Added items";
            ws.Cell(1, 5).Value = "Price";

            ws.Row(1).Style.Font.Bold = true;

            var rowIndex = 2;
            foreach (var center in centers)
            {
                var centerUsers = users
                    .Where(u => u.CenterId == center.Id)
                    .OrderBy(u => u.Username)
                    .ToList();

                if (centerUsers.Count == 0)
                {
                    ws.Cell(rowIndex, 1).Value = center.Id;
                    ws.Cell(rowIndex, 2).Value = center.Name;
                    ws.Cell(rowIndex, 3).Value = "";
                    ws.Cell(rowIndex, 4).Value = 0;
                    var priceCell = ws.Cell(rowIndex, 5);
                    priceCell.Value = center.Price;
                    priceCell.Style.NumberFormat.Format = "#,##0.00 \"EUR\"";
                    rowIndex++;
                    continue;
                }

                foreach (var user in centerUsers)
                {
                    ws.Cell(rowIndex, 1).Value = center.Id;
                    ws.Cell(rowIndex, 2).Value = center.Name;
                    ws.Cell(rowIndex, 3).Value = user.Username ?? "";
                    ws.Cell(rowIndex, 4).Value = user.Id != 0 && addedCounts.TryGetValue(user.Id, out var cnt) ? cnt : 0;
                    var priceCell = ws.Cell(rowIndex, 5);
                    priceCell.Value = center.Price;
                    priceCell.Style.NumberFormat.Format = "#,##0.00 \"EUR\"";
                    rowIndex++;
                }
            }

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public async Task<int> ResetPricesAsync()
        {
            var centers = await _ctx.Center.ToListAsync();

            foreach (var c in centers)
                c.Price = 0m;

            await _ctx.SaveChangesAsync();
            return centers.Count;
        }

        private static string NormalizeCenterName(string? name)
        {
            var cleaned = string.Join(' ', (name ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Trim();

            if (string.IsNullOrWhiteSpace(cleaned))
                throw new ArgumentException("Center name is required.");

            if (cleaned.Length > 120)
                throw new ArgumentException("Center name is too long.");

            return cleaned;
        }

        private static bool IsCenterPrimaryKeyViolation(DbUpdateException ex)
        {
            if (ex.InnerException is not PostgresException pg) return false;
            if (pg.SqlState != PostgresErrorCodes.UniqueViolation) return false;

            return string.Equals(pg.ConstraintName, "pk_center", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pg.ConstraintName, "center_pkey", StringComparison.OrdinalIgnoreCase);
        }

        private async Task SyncCenterIdSequenceAsync()
        {
            const string sql = """
WITH seq AS (
    SELECT pg_get_serial_sequence('center', 'id') AS name
),
mx AS (
    SELECT COALESCE(MAX(id), 0) AS max_id FROM center
)
SELECT setval(
    seq.name,
    CASE WHEN mx.max_id > 0 THEN mx.max_id ELSE 1 END,
    CASE WHEN mx.max_id > 0 THEN true ELSE false END
)
FROM seq, mx
WHERE seq.name IS NOT NULL;
""";

            await _ctx.Database.ExecuteSqlRawAsync(sql);
        }
    }
}
