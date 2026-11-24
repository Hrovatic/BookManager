using BookManagerApi.Data;
using BookManagerApi.DTOs;
using BookManagerApi.Helpers;
using BookManagerApi.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace BookManagerApi.Services
{
    public class WishlistService
    {
        private readonly ApplicationDbContext _ctx;
        private readonly IEmailService _IEmailService;

        public WishlistService(ApplicationDbContext ctx, IEmailService IEmailService)
        {
            _ctx = ctx;
            _IEmailService = IEmailService;
        }

        public class WishlistBulkStatusDto
        {
            public List<int> Ids { get; set; } = new();
            public int Status { get; set; }
            public string? CategoryCode { get; set; }
            public string? OrderNumber { get; set; }
        }

        public interface IWishlistService
        {
            Task<int> BulkUpdateStatusAsync(List<int> ids, int status, string? categoryCode = null, string? orderNumber = null);
            Task<int> BulkUpdatePricesAsync(List<CenterPriceItemDto> items);
            Task<int> BulkDeleteAsync(List<int> ids);
            Task<int> AddToInventoryWithPricesAsync(WishlistAddedWithPriceDto dto, int? reviewedByUserId);
            Task<List<WishlistCenterOrderDto>> GetCenterOrdersAsync();
            Task<List<WishlistRequestDto>> GetAllRequests();
            Task<int> UpsertPriceReviewsAsync(List<int> wishlistIds, int year, decimal? price, int? reviewedByUserId);
            Task<List<WishlistPriceReviewDto>> GetPriceReviewsAsync(int? year = null);
            Task<byte[]> ExportPriceReviewsXlsxAsync(int? year = null);


        }


        public async Task<WishlistRequest?> CreateRequest(WishlistCreateDto dto)
        {
            if (dto.CenterId.HasValue)
            {
                var centerExists = await _ctx.Center.AnyAsync(c => c.Id == dto.CenterId.Value);
                if (!centerExists)
                    throw new ArgumentException("Center not found.");
            }

            var request = new WishlistRequest
            {
                ISBN = dto.ISBN,
                ISSN = dto.ISSN,
                IssnVolume = dto.IssnVolume,
                IssnNumber = dto.IssnNumber,
                Priority = dto.Priority,
                Price = dto.Price,
                Title = dto.Title,
                Author = dto.Author,
                Language = LanguageCodeHelper.NormalizeBookLanguage(dto.Language),
                Genres = dto.Genres != null ? string.Join(", ", dto.Genres) : null,
                Note = dto.Note,
                FundingSource = dto.FundingSource,
                CategoryCode = dto.CategoryCode,
                OrderNumber = dto.OrderNumber,
                CenterId = dto.CenterId,
                Status = WishlistStatus.Review,
                CreatedAt = DateTime.UtcNow
            };

            await _ctx.WishlistRequests.AddAsync(request);
            await _ctx.SaveChangesAsync();

            return request;
        }
        public async Task AddRequest(WishlistCreateDto dto, int userId)
        {
            var userCenterId = await _ctx.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.CenterId)
                .FirstOrDefaultAsync();

            var resolvedCenterId = dto.CenterId ?? userCenterId;
            if (resolvedCenterId.HasValue)
            {
                var centerExists = await _ctx.Center.AnyAsync(c => c.Id == resolvedCenterId.Value);
                if (!centerExists)
                    throw new ArgumentException("Center not found.");
            }

            var now = DateTime.UtcNow;
            var duplicateWindowStart = now.AddSeconds(-15);
            var titleKey = (dto.Title ?? string.Empty).Trim().ToLowerInvariant();
            var authorKey = (dto.Author ?? string.Empty).Trim().ToLowerInvariant();
            var isbnKey = (NormalizeIsbn(dto.ISBN) ?? string.Empty).ToLowerInvariant();
            var issnKey = (NormalizeIssn(dto.ISSN) ?? string.Empty).ToLowerInvariant();

            var duplicateExists = await _ctx.WishlistRequests
                .AsNoTracking()
                .AnyAsync(w =>
                    w.UserId == userId &&
                    w.CreatedAt >= duplicateWindowStart &&
                    w.CenterId == resolvedCenterId &&
                    (w.Title ?? string.Empty).ToLower() == titleKey &&
                    (w.Author ?? string.Empty).ToLower() == authorKey &&
                    ((w.ISBN ?? string.Empty).Replace("-", "").Replace(" ", "").ToLower() == isbnKey) &&
                    ((w.ISSN ?? string.Empty).Replace("-", "").Replace(" ", "").ToLower() == issnKey));

            if (duplicateExists)
                throw new InvalidOperationException("Duplicate wishlist request detected (same user, title/author/isbn, center, within 15 seconds).");

            var entity = new WishlistRequest
            {
                UserId = userId,    
                ISBN = dto.ISBN,
                ISSN = dto.ISSN,
                IssnVolume = dto.IssnVolume,
                IssnNumber = dto.IssnNumber,
                Priority = dto.Priority,
                Price = dto.Price,
                Title = dto.Title,  
                Author = dto.Author,
                Language = LanguageCodeHelper.NormalizeBookLanguage(dto.Language),
                Genres = dto.Genres != null ? string.Join(",", dto.Genres) : null,
                Note = dto.Note,
                FundingSource = dto.FundingSource,
                CategoryCode = dto.CategoryCode,
                OrderNumber = dto.OrderNumber,
                CenterId = resolvedCenterId,
                NotifyMe = dto.NotifyMe,
                Status = WishlistStatus.Review,
                CreatedAt = now
            };

            _ctx.WishlistRequests.Add(entity);
            await _ctx.SaveChangesAsync();

            await NotifyAdminsNewWishlistAsync(entity, userId);
        }
        public async Task<List<WishlistRequestDto>> GetAllRequests()
        {
            return await _ctx.WishlistRequests
                .AsNoTracking()
                .Include(w => w.User)
                .ThenInclude(u => u.Center)
                .Include(w => w.Center)
                .OrderByDescending(w => w.CreatedAt)
                .Select(w => new WishlistRequestDto
                {
                    Id = w.Id,
                    UserId = w.UserId,
                    Username = w.User != null ? w.User.Username : null,
                    Title = w.Title,
                    Author = w.Author,
                    ISBN = w.ISBN,
                    ISSN = w.ISSN,
                    IssnVolume = w.IssnVolume,
                    IssnNumber = w.IssnNumber,
                    Priority = w.Priority,
                    Price = w.Price,
                    Language = w.Language,
                    Genres = w.Genres,
                    Note = w.Note,
                    FundingSource = w.FundingSource,
                    CategoryCode = w.CategoryCode,
                    OrderNumber = w.OrderNumber,
                    Status = (int)w.Status,
                    CreatedAt = w.CreatedAt,
                    NotifyMe = w.NotifyMe,
                    Notified = w.Notified,

                    CenterId = w.CenterId ?? (w.User != null ? (int?)w.User.CenterId : null),
                    CenterCode = w.Center != null
                        ? w.Center.Name
                        : (w.User != null && w.User.Center != null ? w.User.Center.Name : null)
                })
                .ToListAsync();
        }


        public async Task<List<WishlistRequest>> GetMineAsync(int userId)
        {
            return await _ctx.WishlistRequests
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.Id)
                .ToListAsync();
        }

        public async Task<List<WishlistRequestDto>> GetMineRequests(int userId)
        {
            return await _ctx.WishlistRequests
                .AsNoTracking()
                .Include(w => w.User)
                .ThenInclude(u => u.Center)
                .Include(w => w.Center)
                .Where(w => w.UserId == userId)
                .OrderByDescending(w => w.CreatedAt)
                .Select(w => new WishlistRequestDto
                {
                    Id = w.Id,
                    UserId = w.UserId,
                    Username = w.User != null ? w.User.Username : null,
                    Title = w.Title,
                    Author = w.Author,
                    ISBN = w.ISBN,
                    ISSN = w.ISSN,
                    IssnVolume = w.IssnVolume,
                    IssnNumber = w.IssnNumber,
                    Priority = w.Priority,
                    Price = w.Price,
                    Language = w.Language,
                    Genres = w.Genres,
                    Note = w.Note,
                    FundingSource = w.FundingSource,
                    CategoryCode = w.CategoryCode,
                    OrderNumber = w.OrderNumber,
                    Status = (int)w.Status,
                    CreatedAt = w.CreatedAt,
                    NotifyMe = w.NotifyMe,
                    Notified = w.Notified,
                    CenterId = w.CenterId ?? (w.User != null ? (int?)w.User.CenterId : null),
                    CenterCode = w.Center != null
                        ? w.Center.Name
                        : (w.User != null && w.User.Center != null ? w.User.Center.Name : null)
                })
                .ToListAsync();
        }
        public async Task<WishlistRequest?> GetByIdAsync(int id)
        {
            return await _ctx.WishlistRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);
        }
        public async Task UpdateStatusAsync(int id, WishlistStatus newStatus)
        {
            var wishlist = await _ctx.WishlistRequests
                .Include(w => w.User)
                .FirstOrDefaultAsync(w => w.Id == id);
            if (wishlist == null)
                throw new KeyNotFoundException();

            var allowedTransitions = new Dictionary<WishlistStatus, WishlistStatus[]>
            {
                [WishlistStatus.New] = new[] { WishlistStatus.Review, WishlistStatus.Rejected },
                [WishlistStatus.Review] = new[] { WishlistStatus.Ordered, WishlistStatus.Rejected },
                [WishlistStatus.Ordered] = new[] { WishlistStatus.Added }
            };

            if (!allowedTransitions.TryGetValue(wishlist.Status, out var allowed) ||
                !allowed.Contains(newStatus))
            {
                throw new InvalidOperationException(
                    $"Prehod iz {wishlist.Status} v {newStatus} ni dovoljen."
                );
            }

            wishlist.Status = newStatus;

            if (newStatus == WishlistStatus.Added)
            {
                var book = await EnsureBookForWishlistAsync(wishlist);
                var nextNumber = await GetNextInventorySeedAsync();
                await AddCopyForBookAsync(
                    book,
                    nextNumber,
                    ResolveWishlistCenterId(wishlist),
                    wishlist.OrderNumber,
                    ToMainCategoryCode(wishlist.CategoryCode),
                    wishlist.IssnVolume,
                    wishlist.IssnNumber
                );
                await EnsureAddedSnapshotReviewAsync(wishlist, null);
                NotifyUserAddedToInventory(wishlist);
                _ctx.WishlistRequests.Remove(wishlist);
            }
            else if (newStatus == WishlistStatus.Ordered)
            {
                await EnsureBookForWishlistAsync(wishlist);
            }

            await _ctx.SaveChangesAsync();
        }
        public async Task<bool> DeleteRequest(int id)
        {
            var item = await _ctx.WishlistRequests
                .FirstOrDefaultAsync(x => x.Id == id);

            if (item == null)
                return false;

            _ctx.WishlistRequests.Remove(item);
            await _ctx.SaveChangesAsync();
            return true;
        }
        public async Task<bool> Update(int id, WishlistUpdateDto dto)
        {
            var req = await _ctx.WishlistRequests.FirstOrDefaultAsync(x => x.Id == id);
            if (req == null) return false;

            if (dto.ISBN != null) req.ISBN = dto.ISBN;
            if (dto.ISSN != null) req.ISSN = dto.ISSN;
            if (dto.IssnVolume != null) req.IssnVolume = dto.IssnVolume;
            if (dto.IssnNumber != null) req.IssnNumber = dto.IssnNumber;
            if (dto.Title != null) req.Title = dto.Title;
            if (dto.Author != null) req.Author = dto.Author;

            if (dto.Priority != null) req.Priority = dto.Priority;
            if (dto.Language != null) req.Language = LanguageCodeHelper.NormalizeBookLanguage(dto.Language);

            if (dto.Genres != null)
                req.Genres = string.Join(",",
                    dto.Genres
                      .Where(g => !string.IsNullOrWhiteSpace(g))
                      .Select(g => g.Trim()));

            if (dto.FundingSource != null) req.FundingSource = dto.FundingSource;
            if (dto.CategoryCode != null) req.CategoryCode = dto.CategoryCode;
            if (dto.OrderNumber != null) req.OrderNumber = dto.OrderNumber;
            if (dto.NotifyMe.HasValue) req.NotifyMe = dto.NotifyMe.Value;

            await _ctx.SaveChangesAsync();
            return true;
        }
        public async Task<int> BulkUpdateStatusAsync(List<int> ids, int status, string? categoryCode = null, string? orderNumber = null)
        {
            var items = await _ctx.WishlistRequests
                .Include(w => w.User)
                .Where(w => ids.Contains(w.Id))
                .ToListAsync();

            var newStatus = (WishlistStatus)status;

            foreach (var item in items)
            {
                item.Status = newStatus;
                if (categoryCode != null) item.CategoryCode = categoryCode;
                if (orderNumber != null) item.OrderNumber = orderNumber;
            }

            if (newStatus == WishlistStatus.Added)
            {
                var nextNumber = await GetNextInventorySeedAsync();
                foreach (var item in items)
                {
                    var book = await EnsureBookForWishlistAsync(item);
                    await AddCopyForBookAsync(
                        book,
                        nextNumber,
                        ResolveWishlistCenterId(item),
                        item.OrderNumber,
                        ToMainCategoryCode(categoryCode ?? item.CategoryCode),
                        item.IssnVolume,
                        item.IssnNumber
                    );
                    nextNumber++;
                    await EnsureAddedSnapshotReviewAsync(item, null);
                    NotifyUserAddedToInventory(item);
                }

                _ctx.WishlistRequests.RemoveRange(items);
            }
            else if (newStatus == WishlistStatus.Ordered)
            {
                foreach (var item in items)
                    await EnsureBookForWishlistAsync(item);
            }

            await _ctx.SaveChangesAsync();
            return items.Count;
        }

        public async Task<int> AddToInventoryWithPricesAsync(WishlistAddedWithPriceDto dto, int? reviewedByUserId)
        {
            if (dto?.WishlistIds == null || dto.WishlistIds.Count == 0) return 0;
            if (dto.Year < 1900 || dto.Year > DateTime.UtcNow.Year + 5)
                throw new ArgumentException("Invalid year.", nameof(dto.Year));

            var ids = dto.WishlistIds.Distinct().ToList();
            var items = await _ctx.WishlistRequests
                .Include(w => w.User)
                .Where(w => ids.Contains(w.Id))
                .ToListAsync();

            if (items.Count == 0) return 0;

            await using var tx = await _ctx.Database.BeginTransactionAsync();

            var existingReviews = await _ctx.WishlistPriceReviews
                .Where(r => r.Year == dto.Year && r.WishlistRequestId.HasValue && ids.Contains(r.WishlistRequestId.Value))
                .ToListAsync();

            var existingById = existingReviews
                .Where(r => r.WishlistRequestId.HasValue)
                .ToDictionary(r => r.WishlistRequestId!.Value);

            var nextNumber = await GetNextInventorySeedAsync();

            foreach (var item in items)
            {
                item.Status = WishlistStatus.Added;

                if (dto.CategoryCode != null) item.CategoryCode = dto.CategoryCode;
                if (dto.OrderNumber != null) item.OrderNumber = dto.OrderNumber;

                var resolvedCenterId = ResolveWishlistCenterId(item, dto.CenterId);
                var copyLocation = string.IsNullOrWhiteSpace(dto.Location)
                    ? ToMainCategoryCode(dto.CategoryCode ?? item.CategoryCode)
                    : dto.Location;
                var book = await EnsureBookForWishlistAsync(item);
                await AddCopyForBookAsync(
                    book,
                    nextNumber,
                    resolvedCenterId,
                    item.OrderNumber,
                    copyLocation,
                    item.IssnVolume,
                    item.IssnNumber
                );
                nextNumber++;
                NotifyUserAddedToInventory(item);

                if (existingById.TryGetValue(item.Id, out var review))
                {
                    review.Price = dto.Price;
                    review.ReviewedAtUtc = DateTime.UtcNow;
                    review.ReviewedByUserId = reviewedByUserId;
                    review.Title = item.Title;
                    review.Author = item.Author;
                    review.ISBN = item.ISBN ?? item.ISSN;
                    review.OrderNumber = item.OrderNumber;
                    review.CenterId = resolvedCenterId;
                    review.Username = item.User?.Username;
                    review.Source = WishlistPriceReview.SourceWishlist;
                }
                else
                {
                    _ctx.WishlistPriceReviews.Add(new WishlistPriceReview
                    {
                        WishlistRequestId = item.Id,
                        Year = dto.Year,
                        Price = dto.Price,
                        ReviewedAtUtc = DateTime.UtcNow,
                        ReviewedByUserId = reviewedByUserId,
                        Title = item.Title,
                        Author = item.Author,
                        ISBN = item.ISBN ?? item.ISSN,
                        OrderNumber = item.OrderNumber,
                        CenterId = resolvedCenterId,
                        Username = item.User?.Username,
                        Source = WishlistPriceReview.SourceWishlist
                    });
                }
            }

            _ctx.WishlistRequests.RemoveRange(items);
            await _ctx.SaveChangesAsync();
            await tx.CommitAsync();
            return items.Count;
        }
        private void NotifyUserAddedToInventory(WishlistRequest item)
        {
            if (item == null || !item.NotifyMe) return;

            var email = item.User?.Email;
            if (string.IsNullOrWhiteSpace(email)) return;

            var isEn = NormalizeLanguage(item.User?.PreferredLanguage) == "en";
            var title = string.IsNullOrWhiteSpace(item.Title) ? (isEn ? "Book" : "Knjiga") : item.Title!;
            var author = string.IsNullOrWhiteSpace(item.Author) ? "" : $" - {item.Author}";

            var subject = isEn
                ? "Wishlist book added to inventory"
                : "Knjiga dodana v inventar";

            var body = isEn
                ? $"<p>Your wishlist item was added to inventory:</p><p><b>{title}</b>{author}</p><p><i>This is an automated message. Please do not reply.</i></p>"
                : $"<p>Vaša želja je bila dodana v inventar:</p><p><b>{title}</b>{author}</p><p><i>To je avtomatsko sporočilo. Prosimo, ne odgovarjajte nanj.</i></p>";

            TrySendEmail(email, subject, body);
        }

        private async Task NotifyAdminsNewWishlistAsync(WishlistRequest request, int userId)
        {
            var adminsRaw = await _ctx.Users
                .AsNoTracking()
                .Where(u => u.Role == "admin" && u.Email != null && u.Email != "")
                .Select(u => new
                {
                    Email = u.Email!,
                    PreferredLanguage = u.PreferredLanguage
                })
                .ToListAsync();

            var admins = adminsRaw
                .GroupBy(a => a.Email.Trim().ToLowerInvariant())
                .Select(g => g.First())
                .ToList();

            if (admins.Count == 0) return;

            var user = await _ctx.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            var username = user?.Username ?? $"ID {userId}";
            var title = request.Title ?? "Nova želja";
            var author = string.IsNullOrWhiteSpace(request.Author) ? "" : $" - {request.Author}";

            var priority = (request.Priority ?? "").Trim().ToLowerInvariant();
            var isHigh = priority == "high";

            foreach (var admin in admins)
            {
                var isEn = NormalizeLanguage(admin.PreferredLanguage) == "en";

                var subject = isHigh
                    ? (isEn ? "URGENT wishlist request (high priority)" : "NUJNA želja na wishlist (visoka prioriteta)")
                    : (isEn ? "New wishlist request" : "Nova želja na wishlist");

                var priorityLine = isHigh
                    ? (isEn
                        ? "<p>New request with <b><span style=\"color:#dc2626;\">high priority</span></b>:</p>"
                        : "<p>Nova želja z <b><span style=\"color:#dc2626;\">visoko prioriteto</span></b>:</p>")
                    : (isEn ? "<p>New wishlist request:</p>" : "<p>Nova želja:</p>");

                var body = isEn
                    ? $"<p>User <b>{username}</b> submitted a new wishlist request.</p>{priorityLine}<p><b>{title}</b>{author}</p><p><i>This is an automated message. Please do not reply.</i></p>"
                    : $"<p>Uporabnik <b>{username}</b> je dodal novo željo.</p>{priorityLine}<p><b>{title}</b>{author}</p><p><i>To je avtomatsko sporočilo. Prosimo, ne odgovarjajte nanj.</i></p>";

                TrySendEmail(admin.Email, subject, body);
            }
        }

        private static string NormalizeLanguage(string? lang)
        {
            return string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "sl";
        }

        private void TrySendEmail(string to, string subject, string body)
        {
            try
            {
                _IEmailService.SendEmail(to, subject, body);
            }
            catch
            {
                // ignore email failures
            }
        }

        public async Task<int> BulkDeleteAsync(List<int> ids)
        {
            if (ids == null || ids.Count == 0) return 0;

            var items = await _ctx.WishlistRequests
                .Where(w => ids.Contains(w.Id))
                .ToListAsync();

            _ctx.WishlistRequests.RemoveRange(items);
            await _ctx.SaveChangesAsync();

            return items.Count;
        }

        private async Task<Book> EnsureBookForWishlistAsync(WishlistRequest wishlist)
        {
            if (wishlist == null) throw new ArgumentNullException(nameof(wishlist));

            var isbn = NormalizeIsbn(wishlist.ISBN);
            var issnNormalized = NormalizeIssn(wishlist.ISSN);
            var issnCanonical = !string.IsNullOrWhiteSpace(issnNormalized)
                ? ToCanonicalIssn(issnNormalized)
                : null;
            Book? existing = null;

            if (!string.IsNullOrWhiteSpace(isbn))
            {
                existing = _ctx.Books.Local.FirstOrDefault(b => b.ISBN == isbn)
                    ?? await _ctx.Books.FirstOrDefaultAsync(b => b.ISBN == isbn);
            }

            if (existing == null && !string.IsNullOrWhiteSpace(issnNormalized))
            {
                existing = _ctx.Books.Local.FirstOrDefault(b => b.IssnNormalized == issnNormalized)
                    ?? await _ctx.Books.FirstOrDefaultAsync(b => b.IssnNormalized == issnNormalized);
            }

            if (existing == null)
            {
                var title = wishlist.Title?.Trim();
                var author = wishlist.Author?.Trim();

                if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(author))
                {
                    existing = _ctx.Books.Local.FirstOrDefault(b =>
                        string.Equals(b.Title, title, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(b.Author, author, StringComparison.OrdinalIgnoreCase))
                        ?? await _ctx.Books.FirstOrDefaultAsync(b =>
                            b.Title.ToLower() == title.ToLower() &&
                            b.Author.ToLower() == author.ToLower());
                }
            }

            if (existing != null) return existing;

            var categoryCode = string.IsNullOrWhiteSpace(wishlist.CategoryCode)
                ? "9.00.00"
                : wishlist.CategoryCode.Trim();

            var book = new Book
            {
                ISBN = string.IsNullOrWhiteSpace(isbn) ? null : isbn,
                ISSN = issnCanonical,
                IssnNormalized = issnNormalized,
                IdentifierType = !string.IsNullOrWhiteSpace(issnNormalized)
                    ? IdentifierType.ISSN
                    : IdentifierType.ISBN,
                Title = wishlist.Title?.Trim() ?? "Unknown",
                Author = wishlist.Author?.Trim() ?? "Unknown",
                Publisher = null,
                YearPublished = null,
                CoverUrl = null,
                Language = LanguageCodeHelper.NormalizeBookLanguage(wishlist.Language),
                Genre = ParseGenres(wishlist.Genres),
                CategoryCode = categoryCode
            };

            _ctx.Books.Add(book);
            await _ctx.SaveChangesAsync();
            return book;
        }

        private static List<string> ParseGenres(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();

            return raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(g => g.Trim())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .ToList();
        }

        private static string? NormalizeIsbn(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return raw.Replace("-", "").Replace(" ", "").Trim();
        }

        private static string? NormalizeIssn(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var normalized = raw
                .Replace("-", "")
                .Replace(" ", "")
                .Trim()
                .ToUpperInvariant();

            if (normalized.Length != 8) return null;

            for (var i = 0; i < 7; i++)
            {
                if (!char.IsDigit(normalized[i])) return null;
            }

            var last = normalized[7];
            if (!char.IsDigit(last) && last != 'X') return null;

            return normalized;
        }

        private static string ToCanonicalIssn(string normalized)
            => normalized.Insert(4, "-");

        private async Task<int> GetNextInventorySeedAsync()
        {
            var numbers = await _ctx.BookCopies
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

            return next;
        }

        private Task AddCopyForBookAsync(
            Book book,
            int inventoryNumber,
            int? centerId,
            string? orderNumber,
            string? location,
            string? issnVolume,
            string? issnNumber)
        {
            var number = inventoryNumber.ToString("D6");
            var normalizedLocation = ToMainCategoryCode(book.CategoryCode);
            if (string.IsNullOrWhiteSpace(book.CategoryCode))
                book.CategoryCode = normalizedLocation;

            var copy = new BookInventory
            {
                Book = book,
                InventoryNumber = number,
                OrderNumber = string.IsNullOrWhiteSpace(orderNumber) ? null : orderNumber.Trim(),
                IssnVolume = string.IsNullOrWhiteSpace(issnVolume) ? null : issnVolume.Trim(),
                IssnNumber = string.IsNullOrWhiteSpace(issnNumber) ? null : issnNumber.Trim(),
                Location = normalizedLocation,
                CenterId = centerId,
                IsAvailable = true,
                CreatedAt = DateTime.UtcNow,
            };

            _ctx.BookCopies.Add(copy);
            return Task.CompletedTask;
        }

        private static string ToMainCategoryCode(string? code)
        {
            var raw = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return "9.00.00";

            var parts = raw.Split('.');
            if (parts.Length != 3)
                return "9.00.00";

            return $"{parts[0]}.00.00";
        }

        public async Task<int> UpsertPriceReviewsAsync(List<int> wishlistIds, int year, decimal? price, int? reviewedByUserId)
        {
            if (wishlistIds == null || wishlistIds.Count == 0) return 0;

            var ids = wishlistIds.Distinct().ToList();

            var wishlistMap = await _ctx.WishlistRequests
                .AsNoTracking()
                .Include(w => w.User)
                .Where(w => ids.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id);

            ids = wishlistMap.Keys.ToList();
            if (ids.Count == 0) return 0;

            var existing = await _ctx.WishlistPriceReviews
                .Where(r => r.Year == year && r.WishlistRequestId.HasValue && ids.Contains(r.WishlistRequestId.Value))
                .ToListAsync();

            var existingIds = existing
                .Select(r => r.WishlistRequestId!.Value)
                .ToHashSet();

            foreach (var entry in existing)
            {
                var w = wishlistMap[entry.WishlistRequestId!.Value];
                entry.Price = price;
                entry.ReviewedAtUtc = DateTime.UtcNow;
                entry.ReviewedByUserId = reviewedByUserId;
                entry.Title = w.Title;
                entry.Author = w.Author;
                entry.ISBN = w.ISBN ?? w.ISSN;
                entry.OrderNumber = w.OrderNumber;
                entry.CenterId = w.CenterId ?? w.User?.CenterId;
                entry.Username = w.User?.Username;
                entry.Source = WishlistPriceReview.SourceWishlist;
            }

            var toAdd = ids
                .Where(id => !existingIds.Contains(id))
                .Select(id => new WishlistPriceReview
                {
                    WishlistRequestId = id,
                    Year = year,
                    Price = price,
                    ReviewedAtUtc = DateTime.UtcNow,
                    ReviewedByUserId = reviewedByUserId,
                    Title = wishlistMap[id].Title,
                    Author = wishlistMap[id].Author,
                    ISBN = wishlistMap[id].ISBN ?? wishlistMap[id].ISSN,
                    OrderNumber = wishlistMap[id].OrderNumber,
                    CenterId = wishlistMap[id].CenterId ?? wishlistMap[id].User?.CenterId,
                    Username = wishlistMap[id].User?.Username,
                    Source = WishlistPriceReview.SourceWishlist
                })
                .ToList();

            if (toAdd.Count > 0)
                _ctx.WishlistPriceReviews.AddRange(toAdd);

            await _ctx.SaveChangesAsync();
            return ids.Count;
        }

        public async Task<List<WishlistPriceReviewDto>> GetPriceReviewsAsync(int? year = null)
        {
            IQueryable<WishlistPriceReview> query = _ctx.WishlistPriceReviews
                .AsNoTracking();

            if (year.HasValue)
                query = query.Where(r => r.Year == year.Value);

            query = query
                .Include(r => r.WishlistRequest)
                .ThenInclude(w => w.User)
                .Include(r => r.ReviewedByUser);

            return await query
                .OrderByDescending(r => r.Year)
                .ThenByDescending(r => r.ReviewedAtUtc)
                .Select(r => new WishlistPriceReviewDto
                {
                    Id = r.Id,
                    WishlistRequestId = r.WishlistRequestId,
                    Year = r.Year,
                    Price = r.Price,
                    ReviewedAtUtc = r.ReviewedAtUtc,
                    ReviewedByUserId = r.ReviewedByUserId,
                    ReviewedByUsername = r.ReviewedByUser != null ? r.ReviewedByUser.Username : null,
                    Title = r.Title ?? (r.WishlistRequest != null ? r.WishlistRequest.Title : null),
                    Author = r.Author ?? (r.WishlistRequest != null ? r.WishlistRequest.Author : null),
                    ISBN = r.ISBN ?? (r.WishlistRequest != null ? (r.WishlistRequest.ISBN ?? r.WishlistRequest.ISSN) : null),
                    OrderNumber = r.OrderNumber ?? (r.WishlistRequest != null ? r.WishlistRequest.OrderNumber : null),
                    CenterId = r.CenterId ?? (r.WishlistRequest != null && r.WishlistRequest.CenterId.HasValue
                        ? r.WishlistRequest.CenterId
                        : (r.WishlistRequest != null && r.WishlistRequest.User != null
                        ? r.WishlistRequest.User.CenterId
                        : null)),
                    Username = r.Username ?? (r.WishlistRequest != null && r.WishlistRequest.User != null
                        ? r.WishlistRequest.User.Username
                        : null),
                    Source = !string.IsNullOrWhiteSpace(r.Source)
                        ? r.Source
                        : (r.WishlistRequestId.HasValue
                            ? WishlistPriceReview.SourceWishlist
                            : WishlistPriceReview.SourceManual)
                })
                .ToListAsync();
        }

        public async Task<List<WishlistCenterOrderDto>> GetCenterOrdersAsync()
        {
            var ordered = await _ctx.WishlistRequests
                .AsNoTracking()
                .Include(w => w.User)
                .Where(w =>
                    w.Status == WishlistStatus.Ordered &&
                    (w.CenterId.HasValue || (w.User != null && w.User.CenterId.HasValue)))
                .Select(w => new WishlistCenterOrderDto
                {
                    CenterId = w.CenterId ?? w.User!.CenterId!.Value,
                    Status = (int)w.Status,
                    ISBN = w.ISBN ?? w.ISSN,
                    Title = w.Title,
                    Author = w.Author
                })
                .ToListAsync();

            // Added requests are deleted; use snapshot reviews to keep center mapping.
            var added = await _ctx.WishlistPriceReviews
                .AsNoTracking()
                .Where(r => r.WishlistRequestId == null && r.CenterId.HasValue)
                .Select(r => new WishlistCenterOrderDto
                {
                    CenterId = r.CenterId!.Value,
                    Status = (int)WishlistStatus.Added,
                    ISBN = r.ISBN,
                    Title = r.Title,
                    Author = r.Author
                })
                .ToListAsync();

            ordered.AddRange(added);
            return ordered;
        }

        private async Task EnsureAddedSnapshotReviewAsync(WishlistRequest item, int? reviewedByUserId)
        {
            if (item == null) return;

            var year = DateTime.UtcNow.Year;
            var existing = await _ctx.WishlistPriceReviews
                .FirstOrDefaultAsync(r => r.Year == year && r.WishlistRequestId == item.Id);

            if (existing != null)
            {
                existing.ReviewedAtUtc = DateTime.UtcNow;
                existing.ReviewedByUserId = reviewedByUserId;
                existing.Title = item.Title;
                existing.Author = item.Author;
                existing.ISBN = item.ISBN ?? item.ISSN;
                existing.OrderNumber = item.OrderNumber;
                existing.CenterId = ResolveWishlistCenterId(item);
                existing.Username = item.User?.Username;
                existing.Source = WishlistPriceReview.SourceWishlist;
                return;
            }

            _ctx.WishlistPriceReviews.Add(new WishlistPriceReview
            {
                WishlistRequestId = item.Id,
                Year = year,
                Price = null,
                ReviewedAtUtc = DateTime.UtcNow,
                ReviewedByUserId = reviewedByUserId,
                Title = item.Title,
                Author = item.Author,
                ISBN = item.ISBN ?? item.ISSN,
                OrderNumber = item.OrderNumber,
                CenterId = ResolveWishlistCenterId(item),
                Username = item.User?.Username,
                Source = WishlistPriceReview.SourceWishlist
            });
        }

        private static int? ResolveWishlistCenterId(WishlistRequest item, int? overrideCenterId = null)
            => overrideCenterId ?? item.CenterId ?? item.User?.CenterId;

        public async Task<byte[]> ExportPriceReviewsXlsxAsync(int? year = null)
        {
            var rows = await GetPriceReviewsAsync(year);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Wishlist price reviews");

            ws.Cell(1, 1).Value = "Year";
            ws.Cell(1, 2).Value = "Title";
            ws.Cell(1, 3).Value = "Author";
            ws.Cell(1, 4).Value = "ISBN";
            ws.Cell(1, 5).Value = "Order Number";
            ws.Cell(1, 6).Value = "Price";
            ws.Cell(1, 7).Value = "User";
            ws.Cell(1, 8).Value = "Source";
            ws.Cell(1, 9).Value = "Reviewed By";
            ws.Cell(1, 10).Value = "Reviewed At (UTC)";

            ws.Row(1).Style.Font.Bold = true;

            for (int i = 0; i < rows.Count; i++)
            {
                var r = i + 2;
                var row = rows[i];
                ws.Cell(r, 1).Value = row.Year;
                ws.Cell(r, 2).Value = row.Title ?? "";
                ws.Cell(r, 3).Value = row.Author ?? "";
                ws.Cell(r, 4).Value = row.ISBN ?? "";
                ws.Cell(r, 5).Value = row.OrderNumber ?? "";
                var priceCell = ws.Cell(r, 6);
                priceCell.Value = row.Price;
                priceCell.Style.NumberFormat.Format = "#,##0.00 \"EUR\"";
                ws.Cell(r, 7).Value = row.Username ?? "";
                ws.Cell(r, 8).Value = row.Source ?? "";
                ws.Cell(r, 9).Value = row.ReviewedByUsername ?? "";
                ws.Cell(r, 10).Value = row.ReviewedAtUtc;
                ws.Cell(r, 10).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
            }

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

    }
}




