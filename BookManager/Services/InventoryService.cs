using BookManagerApi.Data;
using BookManagerApi.DTOs;
using BookManagerApi.Helpers;
using BookManagerApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace BookManagerApi.Services
{
    public interface ICopiesService
    {
        Task<List<CopyDto>> GetAllAsync(int? bookId);
        Task<CopyDto?> GetByIdAsync(int id);
        Task<CopyDto?> GetByBookIdAsync(int bookId);

        Task<CopyDto> CreateAsync(CreateCopyDto dto);
        Task<CopyDto> AddCopyFromIsbnAsync(AddCopyFromIsbnDto dto, int? reviewedByUserId = null);
        Task<string> GetNextInventoryNumberAsync();

        Task<CopyDto?> UpdateAsync(int id, UpdateCopyDto dto);
        Task<bool> DeleteAsync(int id);
        Task<List<InventoryRowDto>> GetAllAsync();
        Task<AvailabilityDto> GetAvailabilityAsync(int bookId);
        Task<List<AvailabilityDto>> GetAvailabilityAllAsync();
        Task<List<CenterInventoryCountDto>> GetCountsByCenterAsync();
    }

    public class InventoryService : ICopiesService
    {
        private readonly ApplicationDbContext _db;
        private static readonly Regex CategoryCodeInTextRegex = new(@"\b\d{1,2}\.\d{2}\.\d{2}\b", RegexOptions.Compiled);

        public InventoryService(ApplicationDbContext db)
        {
            _db = db;
        }
        private CopyDto ToDto(BookInventory c)
        {
            return ToDto(c, null, null);
        }


        // ---------------------------
        // READ
        // ---------------------------
        public async Task<List<CopyDto>> GetAllAsync(int? bookId)
        {
            var q = _db.BookCopies
                .AsNoTracking()
                .Include(c => c.Book)
                .Include(c => c.Center)
                .AsQueryable();

            if (bookId.HasValue)
                q = q.Where(c => c.BookId == bookId.Value);

            var rows = await q.ToListAsync();

            // zberi vse potrebne kode (sub + main)
            var codes = new HashSet<string>();

            foreach (var r in rows)
            {
                var cc = r.Book?.CategoryCode;
                if (string.IsNullOrWhiteSpace(cc)) continue;

                cc = cc.Trim();
                codes.Add(cc);

                var parts = cc.Split('.');
                if (parts.Length == 3)
                    codes.Add($"{parts[0]}.00.00");
            }

            // en query za šifrant
            var catMap = await _db.Categories
                .AsNoTracking()
                .Where(x => codes.Contains(x.Code))
                .ToDictionaryAsync(x => x.Code, x => x.Name);

            // map DTO + napolni imena
            return rows.Select(c =>
            {
                var cc = c.Book?.CategoryCode?.Trim();

                string? cm = null;
                if (!string.IsNullOrWhiteSpace(cc))
                {
                    var parts = cc.Split('.');
                    if (parts.Length == 3) cm = $"{parts[0]}.00.00";
                }

                var categoryName =
                    (!string.IsNullOrWhiteSpace(cc) && catMap.TryGetValue(cc!, out var n)) ? n : null;

                var categoryMainName =
                    (!string.IsNullOrWhiteSpace(cm) && catMap.TryGetValue(cm!, out var mn)) ? mn : null;

                return ToDto(c, categoryName, categoryMainName);
            }).ToList();
        }
        public async Task<CopyDto?> GetByIdAsync(int id)
        {
            var entity = await _db.BookCopies
                .AsNoTracking()
                .Include(c => c.Book)
                .Include(c => c.Center)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (entity == null) return null;

            var cc = entity.Book?.CategoryCode?.Trim();

            string? cm = null;
            if (!string.IsNullOrWhiteSpace(cc))
            {
                var parts = cc.Split('.');
                if (parts.Length == 3) cm = $"{parts[0]}.00.00";
            }

            var categoryName = !string.IsNullOrWhiteSpace(cc)
                ? await _db.Categories.AsNoTracking()
                    .Where(x => x.Code == cc)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync()
                : null;

            var categoryMainName = !string.IsNullOrWhiteSpace(cm)
                ? await _db.Categories.AsNoTracking()
                    .Where(x => x.Code == cm)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync()
                : null;

            return ToDto(entity, categoryName, categoryMainName);
        }

        public async Task<CopyDto?> GetByBookIdAsync(int bookId)
        {
            var entity = await _db.BookCopies
                .AsNoTracking()
                .Include(c => c.Book)
                .Include(c => c.Center)
                .Where(c => c.BookId == bookId)
                .OrderBy(c => c.InventoryNumber)
                .FirstOrDefaultAsync();

            if (entity == null) return null;

            var cc = entity.Book?.CategoryCode?.Trim();

            string? cm = null;
            if (!string.IsNullOrWhiteSpace(cc))
            {
                var parts = cc.Split('.');
                if (parts.Length == 3) cm = $"{parts[0]}.00.00";
            }

            var categoryName = !string.IsNullOrWhiteSpace(cc)
                ? await _db.Categories.AsNoTracking()
                    .Where(x => x.Code == cc)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync()
                : null;

            var categoryMainName = !string.IsNullOrWhiteSpace(cm)
                ? await _db.Categories.AsNoTracking()
                    .Where(x => x.Code == cm)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync()
                : null;

            return ToDto(entity, categoryName, categoryMainName);
        }

        // ---------------------------
        // CREATE
        // ---------------------------
        public async Task<CopyDto> CreateAsync(CreateCopyDto dto)
        {
            var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == dto.BookId);
            if (book == null) throw new InvalidOperationException("Book not found.");
            if (dto.CenterId.HasValue)
            {
                var centerExists = await _db.Center.AnyAsync(c => c.Id == dto.CenterId.Value);
                if (!centerExists) throw new ArgumentException("Center not found.");
            }

            // Končna številka: ali generirana ali normalizirana
            var inventoryNumber = string.IsNullOrWhiteSpace(dto.InventoryNumber)
                ? await GenerateNextInventoryAsync()
                : NormalizeInventoryHelper.NormalizeInventory(dto.InventoryNumber);

            // Unique check nad NORMALIZIRANO vrednostjo
            var exists = await _db.BookCopies.AnyAsync(c => c.InventoryNumber == inventoryNumber);
            if (exists) throw new ArgumentException("Inventory number already exists.");

            // Location and CategoryCode must always stay in sync.
            var syncedCategoryCode = await ResolveValidCategoryCodeAsync(
                rawCode: null,
                rawLocation: dto.Location,
                fallbackCode: book.CategoryCode
            );
            book.CategoryCode = syncedCategoryCode;
            var mainLocationCode = ToMainCategoryCode(syncedCategoryCode);

            var entity = new BookInventory
            {
                BookId = dto.BookId,
                InventoryNumber = inventoryNumber,
                Location = mainLocationCode,
                Condition = dto.Condition,
                CenterId = dto.CenterId,
                IsAvailable = true,
                CreatedAt = DateTime.UtcNow,
            };

            _db.BookCopies.Add(entity);
            await _db.SaveChangesAsync();

            // reload z Book
            entity = await _db.BookCopies.AsNoTracking()
                .Include(c => c.Book)
                .Include(c => c.Center)
                .SingleAsync(c => c.Id == entity.Id);

            // category names (2 hitra queryja, ker je samo 1 item)
            var cc = entity.Book?.CategoryCode?.Trim();
            string? cm = null;
            if (!string.IsNullOrWhiteSpace(cc))
            {
                var parts = cc.Split('.');
                if (parts.Length == 3) cm = $"{parts[0]}.00.00";
            }

            var categoryName = !string.IsNullOrWhiteSpace(cc)
                ? await _db.Categories.AsNoTracking().Where(x => x.Code == cc).Select(x => x.Name).FirstOrDefaultAsync()
                : null;

            var categoryMainName = !string.IsNullOrWhiteSpace(cm)
                ? await _db.Categories.AsNoTracking().Where(x => x.Code == cm).Select(x => x.Name).FirstOrDefaultAsync()
                : null;

            return ToDto(entity, categoryName, categoryMainName);

        }
        public async Task<CopyDto> AddCopyFromIsbnAsync(AddCopyFromIsbnDto dto, int? reviewedByUserId = null)
        {
            if (string.IsNullOrWhiteSpace(dto.Isbn))
                throw new ArgumentException("ISBN/ISSN is required.", nameof(dto.Isbn));
            if (dto.CenterId.HasValue)
            {
                var centerExists = await _db.Center.AnyAsync(c => c.Id == dto.CenterId.Value);
                if (!centerExists) throw new ArgumentException("Center not found.");
            }

            var normalized = dto.Isbn.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
            var canonicalIssn = normalized.Length == 8
                ? normalized.Insert(4, "-")
                : normalized;

            var book = await _db.Books.FirstOrDefaultAsync(b =>
                b.ISBN == normalized ||
                b.IssnNormalized == normalized ||
                b.ISSN == canonicalIssn ||
                b.ISSN == normalized);

            if (book == null)
                throw new InvalidOperationException("Book not found in database. First fetch it by ISBN/ISSN.");

            var syncedCategoryCode = await ResolveValidCategoryCodeAsync(
                rawCode: dto.CategoryCode,
                rawLocation: dto.Location,
                fallbackCode: book.CategoryCode
            );
            book.CategoryCode = syncedCategoryCode;
            var mainLocationCode = ToMainCategoryCode(syncedCategoryCode);

            var previousPrice = book.Price;

            if (dto.Price.HasValue)
            {
                book.Price = dto.Price.Value;
            }

            var inventoryNumber = string.IsNullOrWhiteSpace(dto.InventoryNumber)
                ? await GenerateNextInventoryAsync()
                : NormalizeInventoryHelper.NormalizeInventory(dto.InventoryNumber);

            var exists = await _db.BookCopies.AnyAsync(c => c.InventoryNumber == inventoryNumber);
            if (exists) throw new InvalidOperationException("Inventory number already exists.");

            var entity = new BookInventory
            {
                BookId = book.Id,
                InventoryNumber = inventoryNumber,
                IssnVolume = string.IsNullOrWhiteSpace(dto.IssnVolume) ? null : dto.IssnVolume.Trim(),
                IssnNumber = string.IsNullOrWhiteSpace(dto.IssnNumber) ? null : dto.IssnNumber.Trim(),
                Location = mainLocationCode,
                Condition = dto.Condition,
                CenterId = dto.CenterId,
                IsAvailable = true,
                CreatedAt = DateTime.UtcNow,
            };

            _db.BookCopies.Add(entity);

            if (HasPriceChanged(previousPrice, dto.Price))
            {
                var reviewerUsername = await GetReviewerUsernameAsync(reviewedByUserId);
                _db.WishlistPriceReviews.Add(new WishlistPriceReview
                {
                    WishlistRequestId = null,
                    Year = DateTime.UtcNow.Year,
                    Price = dto.Price,
                    ReviewedAtUtc = DateTime.UtcNow,
                    ReviewedByUserId = reviewedByUserId,
                    Title = book.Title,
                    Author = book.Author,
                    ISBN = !string.IsNullOrWhiteSpace(book.ISBN) ? book.ISBN : book.ISSN,
                    CenterId = dto.CenterId,
                    Username = reviewerUsername,
                    Source = WishlistPriceReview.SourceIsbn
                });
            }

            await _db.SaveChangesAsync();

            // reload z Book
            var reloaded = await _db.BookCopies.AsNoTracking()
                .Include(c => c.Book)
                .Include(c => c.Center)
                .SingleAsync(c => c.Id == entity.Id);

            // category names
            var cc = reloaded.Book?.CategoryCode?.Trim();
            string? cm = null;

            if (!string.IsNullOrWhiteSpace(cc))
            {
                var parts = cc.Split('.');
                if (parts.Length == 3) cm = $"{parts[0]}.00.00";
            }

            var categoryName = !string.IsNullOrWhiteSpace(cc)
                ? await _db.Categories.AsNoTracking()
                    .Where(x => x.Code == cc)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync()
                : null;

            var categoryMainName = !string.IsNullOrWhiteSpace(cm)
                ? await _db.Categories.AsNoTracking()
                    .Where(x => x.Code == cm)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync()
                : null;

            return ToDto(reloaded, categoryName, categoryMainName);
        }

        public Task<string> GetNextInventoryNumberAsync()
            => GenerateNextInventoryAsync();

        // ---------------------------
        // UPDATE
        // ---------------------------
        public async Task<CopyDto?> UpdateAsync(int id, UpdateCopyDto dto)
        {
            var entity = await _db.BookCopies
                .Include(c => c.Book)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (entity is null) return null;

            // Inventory number (če dovoliš spremembo nalepke)
            if (dto.InventoryNumberSet)
            {
                if (string.IsNullOrWhiteSpace(dto.InventoryNumber))
                    throw new ArgumentException("Inventory number cannot be empty.");

                var normalized = NormalizeInventoryHelper.NormalizeInventory(dto.InventoryNumber);

                var exists = await _db.BookCopies.AnyAsync(c =>
                    c.InventoryNumber == normalized && c.Id != id);

                if (exists) throw new ArgumentException("Inventory number already exists.");

                entity.InventoryNumber = normalized;
            }

            if (dto.LocationSet)
            {
                var syncedCategoryCode = await ResolveValidCategoryCodeAsync(
                    rawCode: null,
                    rawLocation: dto.Location,
                    fallbackCode: entity.Book?.CategoryCode
                );

                var mainLocationCode = ToMainCategoryCode(syncedCategoryCode);
                entity.Location = mainLocationCode;
                if (entity.Book != null)
                    entity.Book.CategoryCode = syncedCategoryCode;
            }
            else
            {
                // Keep data consistent even on updates that don't touch location.
                var syncedCategoryCode = await ResolveValidCategoryCodeAsync(
                    rawCode: entity.Book?.CategoryCode,
                    rawLocation: entity.Location,
                    fallbackCode: entity.Book?.CategoryCode
                );

                entity.Location = ToMainCategoryCode(syncedCategoryCode);
                if (entity.Book != null)
                    entity.Book.CategoryCode = syncedCategoryCode;
            }

            if (dto.ConditionSet)
                entity.Condition = dto.Condition;

            if (dto.CenterIdSet)
            {
                if (dto.CenterId.HasValue)
                {
                    var centerExists = await _db.Center.AnyAsync(c => c.Id == dto.CenterId.Value);
                    if (!centerExists) throw new ArgumentException("Center not found.");
                }

                entity.CenterId = dto.CenterId;
            }

            entity.IsAvailable = dto.Status;

            await _db.SaveChangesAsync();

            var reloaded = await _db.BookCopies.AsNoTracking()
                .Include(c => c.Book)
                .Include(c => c.Center)
                .SingleAsync(c => c.Id == id);

            // category names
            var cc = reloaded.Book?.CategoryCode?.Trim();
            string? cm = null;

            if (!string.IsNullOrWhiteSpace(cc))
            {
                var parts = cc.Split('.');
                if (parts.Length == 3) cm = $"{parts[0]}.00.00";
            }

            var categoryName = !string.IsNullOrWhiteSpace(cc)
                ? await _db.Categories.AsNoTracking()
                    .Where(x => x.Code == cc)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync()
                : null;

            var categoryMainName = !string.IsNullOrWhiteSpace(cm)
                ? await _db.Categories.AsNoTracking()
                    .Where(x => x.Code == cm)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync()
                : null;

            return ToDto(reloaded, categoryName, categoryMainName);
        }

        // ---------------------------
        // DELETE
        // ---------------------------
        public async Task<bool> DeleteAsync(int id)
        {
            var entity = await _db.BookCopies
                .Include(c => c.Borrowings)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (entity is null) return false;

            // Varnost: ne briši, če ima zgodovino izposoj (ali vsaj aktivne izposoje).
            if (entity.Borrowings != null && entity.Borrowings.Count > 0)
                throw new InvalidOperationException("Kopije ni možno izbrisati, ker ima povezane izposoje.");

            _db.BookCopies.Remove(entity);
            await _db.SaveChangesAsync();
            return true;
        }

        // ---------------------------
        // AVAILABILITY
        // ---------------------------
        public async Task<AvailabilityDto> GetAvailabilityAsync(int bookId)
        {
            var total = await _db.BookCopies.CountAsync(c => c.BookId == bookId);
            var available = await _db.BookCopies.CountAsync(c => c.BookId == bookId && c.IsAvailable);

            return new AvailabilityDto
            {
                BookId = bookId,
                Total = total,
                Available = available
            };
        }

        public async Task<List<AvailabilityDto>> GetAvailabilityAllAsync()
        {
            var rows = await _db.BookCopies
                .AsNoTracking()
                .Select(c => new { c.BookId, c.IsAvailable })
                .ToListAsync();

            return rows
                .GroupBy(x => x.BookId)
                .Select(g => new AvailabilityDto
                {
                    BookId = g.Key,
                    Total = g.Count(),
                    Available = g.Count(x => x.IsAvailable)
                })
                .ToList();
        }

        public async Task<List<CenterInventoryCountDto>> GetCountsByCenterAsync()
        {
            var copyRows = await _db.BookCopies
                .AsNoTracking()
                .Select(c => new
                {
                    c.Id,
                    c.BookId,
                    c.CenterId
                })
                .ToListAsync();

            // If a copy is currently borrowed, count it under the borrower's center.
            // Otherwise, count it under the copy's assigned center.
            var activeBorrowerCenterByCopyId = await (
                from b in _db.Borrowings.AsNoTracking()
                where b.ReturnedAt == null
                join u in _db.Users.AsNoTracking() on b.UserId equals u.Id into uu
                from u in uu.DefaultIfEmpty()
                group new { b, u } by b.BookInventoryId into g
                select new
                {
                    CopyId = g.Key,
                    BorrowerCenterId = g
                        .OrderByDescending(x => x.b.BorrowedAt)
                        .Select(x => x.u != null ? x.u.CenterId : (int?)null)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.CopyId, x => x.BorrowerCenterId);

            var rows = copyRows
                .GroupBy(c =>
                {
                    if (activeBorrowerCenterByCopyId.TryGetValue(c.Id, out var borrowerCenterId) && borrowerCenterId.HasValue)
                        return borrowerCenterId;

                    return c.CenterId;
                })
                .Select(g => new
                {
                    CenterId = g.Key,
                    CopyCount = g.Count(),
                    TitleCount = g.Select(x => x.BookId).Distinct().Count()
                })
                .ToList();

            var centerMap = await _db.Center
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Id, c => c.Name);

            return rows
                .Select(r => new CenterInventoryCountDto
                {
                    CenterId = r.CenterId,
                    CenterName = r.CenterId.HasValue && centerMap.TryGetValue(r.CenterId.Value, out var name)
                        ? name
                        : "Na voljo",
                    CopyCount = r.CopyCount,
                    TitleCount = r.TitleCount
                })
                .OrderBy(x => x.CenterName)
                .ToList();
        }

        public async Task<List<InventoryRowDto>> GetAllAsync()
        {
            return await _db.BookCopies
                .AsNoTracking()
                .Include(i => i.Book)
                .Select(i => new InventoryRowDto
                {
                    Id = i.Id,
                    BookId = i.BookId,
                    InventoryNumber = i.InventoryNumber,
                    OrderNumber = i.OrderNumber,
                    IssnVolume = i.IssnVolume,
                    IssnNumber = i.IssnNumber,

                    // Lokacija = CategoryCode
                    Location = i.Location,
                    CenterName = i.Center != null ? i.Center.Name : null,

                    // samo če je aktivno izposojeno
                    BorrowedBy = (
                        from b in i.Borrowings
                        join u in _db.Users on b.UserId equals u.Id
                        join c in _db.Center on u.CenterId equals c.Id into cc
                        from c in cc.DefaultIfEmpty()
                        where b.ReturnedAt == null
                        select b.UserName
                    ).FirstOrDefault(),

                    BorrowedByCenter = (
                        from b in i.Borrowings
                        join u in _db.Users on b.UserId equals u.Id
                        join c in _db.Center on u.CenterId equals c.Id into cc
                        from c in cc.DefaultIfEmpty()
                        where b.ReturnedAt == null
                        select c.Name
                    ).FirstOrDefault(),

                    IsAvailable = !i.Borrowings.Any(b => b.ReturnedAt == null),

                    BookTitle = i.Book.Title,
                    BookAuthor = i.Book.Author,
                    BookIsbn = i.Book.ISBN
                })
                .ToListAsync();
        }
        // ---------------------------
        // HELPERS (service-level)
        // ---------------------------
        private CopyDto ToDto(BookInventory c, string? categoryName, string? categoryMainName)
        {
            var invRaw = c.InventoryNumber ?? "";
            string inv = "";
            var hasNormalized = NormalizeInventoryHelper.TryNormalizeInventory(invRaw, out var normalized);
            if (hasNormalized)
                inv = normalized;

            var categoryCode = c.Book?.CategoryCode;
            var locationCategoryCode = TryExtractCategoryCode(c.Location);
            var prefixSourceCode = !string.IsNullOrWhiteSpace(locationCategoryCode)
                ? locationCategoryCode
                : categoryCode;
            var prefix = CategoryPrefixHelper.FromCategoryCode(prefixSourceCode);
            var display = !string.IsNullOrWhiteSpace(inv)
                ? $"{prefix}-{inv}"
                : (string.IsNullOrWhiteSpace(invRaw) ? "" : invRaw);

            string? categoryMain = null;
            if (!string.IsNullOrWhiteSpace(categoryCode))
            {
                var parts = categoryCode.Split('.');
                if (parts.Length == 3) categoryMain = $"{parts[0]}.00.00";
            }

            return new CopyDto
            {
                Id = c.Id,
                BookId = c.BookId,
                InventoryNumber = string.IsNullOrWhiteSpace(inv) ? null : inv,
                OrderNumber = c.OrderNumber,
                IssnVolume = c.IssnVolume,
                IssnNumber = c.IssnNumber,
                Location = c.Location,
                CenterId = c.CenterId,
                CenterName = c.Center?.Name,
                Status = c.IsAvailable,

                DisplayInventoryNumber = display,
                CategoryCode = categoryCode,
                CategoryMain = categoryMain,

                CategoryName = categoryName,
                CategoryMainName = categoryMainName
            };
        }

        public async Task<string> GenerateNextInventoryAsync()
        {
            // robustno: tolerira "čudne" zapise, če bi se kdaj znašli v bazi
            var numbers = await _db.BookCopies
                .Where(c => c.InventoryNumber != null)
                .Select(c => c.InventoryNumber!)
                .ToListAsync();

            var max = 0;
            foreach (var s in numbers)
            {
                if (int.TryParse(s, out var n) && n > max)
                    max = n;
            }

            var next = max + 1;
            if (next > 999999)
                throw new InvalidOperationException("Inventory limit reached.");

            return next.ToString("D6"); // "000001" ... "999999"
        }

        private static bool HasPriceChanged(decimal? previousPrice, decimal? incomingPrice)
        {
            if (!incomingPrice.HasValue) return false;
            if (!previousPrice.HasValue) return true;
            return previousPrice.Value != incomingPrice.Value;
        }

        private async Task<string?> GetReviewerUsernameAsync(int? reviewedByUserId)
        {
            if (!reviewedByUserId.HasValue) return null;
            return await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == reviewedByUserId.Value)
                .Select(u => u.Username)
                .FirstOrDefaultAsync();
        }

        private async Task<string> ResolveValidCategoryCodeAsync(
            string? rawCode,
            string? rawLocation = null,
            string? fallbackCode = null)
        {
            var candidate = string.IsNullOrWhiteSpace(rawCode)
                ? TryExtractCategoryCode(rawLocation)
                : rawCode.Trim();

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                var exists = await _db.Categories
                    .AsNoTracking()
                    .AnyAsync(c => c.Code == candidate && (c.Level == 1 || c.Level == 2));

                if (exists) return candidate;
            }

            var fallback = string.IsNullOrWhiteSpace(fallbackCode) ? null : fallbackCode.Trim();
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                var fallbackExists = await _db.Categories
                    .AsNoTracking()
                    .AnyAsync(c => c.Code == fallback && (c.Level == 1 || c.Level == 2));

                if (fallbackExists) return fallback;
            }

            return "9.00.00";
        }

        private static string? TryExtractCategoryCode(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var match = CategoryCodeInTextRegex.Match(text);
            return match.Success ? match.Value : null;
        }

        private static string ToMainCategoryCode(string? code)
        {
            var parsed = TryExtractCategoryCode(code) ?? (string.IsNullOrWhiteSpace(code) ? null : code.Trim());
            if (string.IsNullOrWhiteSpace(parsed))
                return "9.00.00";

            var parts = parsed.Split('.');
            if (parts.Length != 3)
                return "9.00.00";

            return $"{parts[0]}.00.00";
        }
    }
}
