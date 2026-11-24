using BookManagerApi.Data;
using BookManagerApi.DTOs;
using BookManagerApi.Helpers;
using BookManagerApi.Services;
using BookManagerApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;

namespace BookManagerApi.Services
{
    public class IssnProviderUnavailableException : Exception
    {
        public IssnProviderUnavailableException(string message) : base(message) { }
        public IssnProviderUnavailableException(string message, Exception innerException) : base(message, innerException) { }
    }

    public interface IBookService
    {
        Task<Book?> GetLocalByIsbn(string rawIsbn);
        Task<Book?> GetLocalByIssn(string rawIssn);
        Task<Book?> LookupByIsbnNoPersist(string rawIsbn);
        Task<Book?> LookupByIssnNoPersist(string rawIssn);
        Task<Book?> GetByIsbn(string rawIsbn);
        Task<Book?> GetByIssn(string rawIssn);
        Task<Book?> Lookup(string code);
        Task<(Book? book, bool created)> LookupWithCreated(string code);
        Task<Book?> GetById(int id);
        Task<List<Book>> GetAll();

        Task<Book?> CreateBook(CreateBookDto dto, int? reviewedByUserId = null);
        Task<bool> UpdateBook(int id, UpdateBookDto dto, int? reviewedByUserId = null);
        Task<bool> DeleteBook(int id);
        Task<bool> SetCategoryAsync(int bookId, string categoryCode);
        Task<int> NormalizeCategoriesFromInventoryAsync();

        // helperji
        int? ParseYear(string? publishDate);
        string? ExtractLanguage(List<OpenLibraryKey>? langs);
        List<string> ExtractGenres(List<string>? subjects, int max = 6);
    }
    public class BookService : IBookService
    {
        private enum IssnLookupStatus
        {
            Success,
            NotFound,
            ProviderUnavailable
        }

        private readonly IServiceProvider _provider;
        private readonly IHttpClientFactory _http;
        private readonly ApplicationDbContext _context;
        private static readonly Regex CategoryCodeInTextRegex = new(@"\b\d{1,2}\.\d{2}\.\d{2}\b", RegexOptions.Compiled);

        private static readonly HashSet<string> AllowedCategories = new()
        {
            "1.00.00","2.00.00","4.00.00","5.00.00","6.00.00","7.00.00","9.00.00","10.00.00"
        };


        public BookService(IServiceProvider provider, IHttpClientFactory http, ApplicationDbContext context)
        {
            _provider = provider;
            _http = http;
            _context = context;
        }
        public async Task<Book?> GetById(int id)
        {
            try
            {
                using var scope = _provider.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                return await ctx.Books.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            }
            catch
            {
                return null;
            }
        }
        public async Task<Book?> Lookup(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            var trimmed = code.Trim();

            // ISSN (dela z ali brez vezaja)
            if (IsIssn(trimmed))
            {
                var normalized = NormalizeIssn(trimmed);
                var canonical = ToCanonicalIssn(normalized);
                return await GetByIssn(canonical); // ali pa GetByIssn(trimmed), če GetByIssn že normalizira
            }

            // ISBN
            if (IsIsbn13(trimmed) || IsIsbn10(trimmed))
                return await GetByIsbn(trimmed);

            return null;
        }
        public async Task<(Book? book, bool created)> LookupWithCreated(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return (null, false);

            var trimmed = code.Trim();

            using var scope = _provider.CreateScope();

            // ISSN
            // ISSN
            if (IsIssn(trimmed))
            {
                var normalized = NormalizeIssn(trimmed);
                var canonical = ToCanonicalIssn(normalized);

                var existedBefore = await _context.Books.AsNoTracking()
                    .AnyAsync(b => b.IssnNormalized == normalized);

                var book = await GetByIssn(canonical);

                return (book, book != null && !existedBefore);
            }


            // ISBN
            if (IsIsbn13(trimmed) || IsIsbn10(trimmed))
            {
                var isbn = NormalizeIsbn(trimmed);

                var existedBefore = await _context.Books.AsNoTracking().AnyAsync(b => b.ISBN == isbn);
                var book = await GetByIsbn(isbn);

                return (book, book != null && !existedBefore);
            }

            return (null, false);
        }

        public async Task<Book?> GetLocalByIsbn(string rawIsbn)
        {
            var isbn = NormalizeIsbn(rawIsbn ?? string.Empty);
            if (string.IsNullOrWhiteSpace(isbn)) return null;

            return await _context.Books
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.ISBN == isbn);
        }

        public async Task<Book?> GetLocalByIssn(string rawIssn)
        {
            var normalized = NormalizeIssn(rawIssn);
            if (normalized.Length != 8) return null;

            var canonical = ToCanonicalIssn(normalized);
            return await _context.Books
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.IssnNormalized == normalized || b.ISSN == canonical);
        }

        public Task<Book?> LookupByIsbnNoPersist(string rawIsbn)
            => GetByIsbnCore(rawIsbn, persist: false);

        public Task<Book?> LookupByIssnNoPersist(string rawIssn)
            => GetByIssnCore(rawIssn, persist: false);

        public Task<Book?> GetByIsbn(string rawIsbn)
            => GetByIsbnCore(rawIsbn, persist: true);

        public Task<Book?> GetByIssn(string rawIssn)
            => GetByIssnCore(rawIssn, persist: true);

        private async Task<Book?> GetByIsbnCore(string rawIsbn, bool persist)
        {
            try
            {
                var isbn = NormalizeIsbn(rawIsbn);
                if (string.IsNullOrWhiteSpace(isbn))
                    return null;

                var client = _http.CreateClient();

                // 1) DB cache
                var existing = await _context.Books.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.ISBN == isbn);

                if (existing != null)
                {
                    Console.WriteLine($"ISBN LOOKUP [{isbn}] source=db-cache");
                    return existing;
                }

                OpenLibraryEdition? edition = null;
                OpenLibraryWork? work = null;
                string? authorName = null;
                var genres = new List<string>();
                var openLibraryOk = false;

                // 2) OpenLibrary edition (primary)
                var editionRes = await client.GetAsync($"https://openlibrary.org/isbn/{isbn}.json");
                if (editionRes.IsSuccessStatusCode)
                {
                    openLibraryOk = true;
                    var editionJson = await editionRes.Content.ReadAsStringAsync();
                    edition = JsonSerializer.Deserialize<OpenLibraryEdition>(
                        editionJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );

                    // 3) Work
                    var workKey = edition?.Works?.FirstOrDefault()?.Key;
                    if (!string.IsNullOrWhiteSpace(workKey))
                    {
                        var workRes = await client.GetAsync($"https://openlibrary.org{workKey}.json");
                        if (workRes.IsSuccessStatusCode)
                        {
                            var workJson = await workRes.Content.ReadAsStringAsync();
                            work = JsonSerializer.Deserialize<OpenLibraryWork>(
                                workJson,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                            );
                        }
                    }

                    // 4) Authors (collect all, not just first)
                    var authorNames = new List<string>();
                    var authorKeys = (work?.Authors ?? new List<OpenLibraryAuthorRef>())
                        .Select(a => a.Author?.Key)
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (var authorKey in authorKeys)
                    {
                        var authorRes = await client.GetAsync($"https://openlibrary.org{authorKey}.json");
                        if (!authorRes.IsSuccessStatusCode) continue;

                        var authorJson = await authorRes.Content.ReadAsStringAsync();
                        var author = JsonSerializer.Deserialize<OpenLibraryAuthor>(
                            authorJson,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                        );

                        if (!string.IsNullOrWhiteSpace(author?.Name))
                            authorNames.Add(author.Name.Trim());
                    }

                    if (authorNames.Count > 0)
                        authorName = string.Join(", ", authorNames);

                    // 5) Genres
                    genres = ExtractGenres(work?.Subjects);
                    if (genres.Count == 0)
                        genres = ExtractGenres(edition?.Subjects);
                }

                // 6) Google Books fallback/completion
                var google = await TryFetchGoogleBooksAsync(client, isbn);
                var googleHasData =
                    !string.IsNullOrWhiteSpace(google.title) ||
                    !string.IsNullOrWhiteSpace(google.author) ||
                    !string.IsNullOrWhiteSpace(google.publisher);

                var resolvedTitle = FirstNonEmpty(work?.Title, edition?.Title, google.title);
                if (string.IsNullOrWhiteSpace(resolvedTitle))
                {
                    Console.WriteLine($"ISBN LOOKUP [{isbn}] source=none openlibrary={openLibraryOk} google={googleHasData}");
                    return null;
                }

                var resolvedAuthor = FirstNonEmpty(authorName, google.author) ?? "Unknown";
                var resolvedPublisher = FirstNonEmpty(edition?.Publishers?.FirstOrDefault(), google.publisher);
                var resolvedYear = ParseYear(edition?.Publish_Date) ?? google.year;
                var resolvedLanguage = ExtractLanguage(work?.Languages) ?? ExtractLanguage(edition?.Languages) ?? google.language;

                if (genres.Count == 0 && google.genres.Count > 0)
                    genres = google.genres;

                // Temporarily disable external cover lookup (use fallback icon in UI).
                string? coverUrl = null;

                var book = new Book
                {
                    ISBN = isbn,
                    ISSN = null,
                    IdentifierType = IdentifierType.ISBN,
                    Title = resolvedTitle,
                    Author = resolvedAuthor,
                    Publisher = resolvedPublisher,
                    YearPublished = resolvedYear,
                    CoverUrl = coverUrl,
                    Language = resolvedLanguage,
                    Genre = genres,
                    CategoryCode = "9.00.00"
                };

                if (persist)
                {
                    _context.Books.Add(book);
                    await _context.SaveChangesAsync();
                }

                var source = openLibraryOk
                    ? (googleHasData ? "openlibrary+google" : "openlibrary")
                    : (googleHasData ? "google-fallback" : "unknown");
                Console.WriteLine($"ISBN LOOKUP [{isbn}] source={source}");

                return book;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ISBN LOOKUP [{rawIsbn}] failed: {ex.Message}");
                return null;
            }
        }

        private async Task<Book?> GetByIssnCore(string rawIssn, bool persist)
        {
            try
            {
                var normalized = NormalizeIssn(rawIssn);
                if (normalized.Length != 8) return null;

                var canonical = normalized.Insert(4, "-"); // 12345678 -> 1234-5678
                var client = _http.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(12);

                // 1) DB cache (format-agnostic)
                var existing = persist
                    ? await _context.Books.FirstOrDefaultAsync(b => b.IssnNormalized == normalized)
                    : await _context.Books.AsNoTracking().FirstOrDefaultAsync(b => b.IssnNormalized == normalized);

                if (existing != null && !NeedsIssnEnrichment(existing))
                    return existing;

                string? title = null;
                string? publisher = null;

                // 2) ISSN portal primary
                var portal = await TryFetchIssnPortalAsync(client, canonical);
                if (portal.status == IssnLookupStatus.Success)
                {
                    title = portal.title;
                    publisher = portal.publisher;
                }

                // 3) Crossref fallback (or data completion if portal was partial)
                var crossref = (status: IssnLookupStatus.NotFound, title: (string?)null, publisher: (string?)null, canonicalIssn: (string?)null);
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(publisher))
                {
                    crossref = await TryFetchCrossrefJournalAsync(client, canonical);
                    if (crossref.status == IssnLookupStatus.Success)
                    {
                        title ??= crossref.title;
                        publisher ??= crossref.publisher;

                        if (!string.IsNullOrWhiteSpace(crossref.canonicalIssn))
                        {
                            canonical = crossref.canonicalIssn;
                            normalized = NormalizeIssn(canonical);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(title))
                {
                    var bothUnavailable =
                        portal.status == IssnLookupStatus.ProviderUnavailable &&
                        crossref.status == IssnLookupStatus.ProviderUnavailable;

                    if (bothUnavailable)
                        throw new IssnProviderUnavailableException("ISSN providers are currently unavailable.");

                    return null;
                }

                if (existing != null)
                {
                    if (persist)
                    {
                        existing.ISSN = canonical;
                        existing.IssnNormalized = normalized;
                        existing.IdentifierType = IdentifierType.ISSN;
                        existing.Title = title ?? existing.Title;
                        existing.Author = string.IsNullOrWhiteSpace(existing.Author) ? "Unknown" : existing.Author;
                        existing.Publisher = publisher;
                        existing.YearPublished = null;
                        existing.CoverUrl = null;
                        existing.Language = null;
                        existing.Genre = new List<string>();
                        existing.CategoryCode ??= "9.00.00";

                        await _context.SaveChangesAsync();
                        return existing;
                    }

                    return new Book
                    {
                        Id = existing.Id,
                        ISBN = existing.ISBN,
                        ISSN = canonical,
                        IssnNormalized = normalized,
                        IdentifierType = IdentifierType.ISSN,
                        Title = title ?? existing.Title ?? "Unknown",
                        Author = string.IsNullOrWhiteSpace(existing.Author) ? "Unknown" : existing.Author,
                        Publisher = publisher,
                        YearPublished = null,
                        CoverUrl = null,
                        Language = null,
                        Genre = new List<string>(),
                        CategoryCode = existing.CategoryCode ?? "9.00.00"
                    };
                }

                var serial = new Book
                {
                    ISBN = null,
                    ISSN = canonical,
                    IssnNormalized = normalized,
                    IdentifierType = IdentifierType.ISSN,
                    Title = title ?? "Unknown",
                    Author = "Unknown",
                    Publisher = publisher,
                    YearPublished = null,
                    CoverUrl = null,
                    Language = null,
                    Genre = new List<string>(),
                    CategoryCode = "9.00.00"
                };

                if (persist)
                {
                    _context.Books.Add(serial);
                    await _context.SaveChangesAsync();
                }

                return serial;
            }
            catch (IssnProviderUnavailableException)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                Console.WriteLine("GetByIssn timeout: " + ex.Message);
                throw new IssnProviderUnavailableException("ISSN provider timeout.", ex);
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine("GetByIssn network error: " + ex.Message);
                throw new IssnProviderUnavailableException("ISSN provider network error.", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetByIssn exception: " + ex);
                return null;
            }
        }

        public async Task<List<Book>> GetAll()
            => await _context.Books.OrderBy(b => b.Title).ToListAsync();

        public async Task<Book?> CreateBook(CreateBookDto dto, int? reviewedByUserId = null)
        {
            try
            {
                using var scope = _provider.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var book = new Book
                {
                    ISBN = dto.ISBN,
                    ISSN = dto.ISSN,
                    IdentifierType = string.IsNullOrWhiteSpace(dto.ISSN)
                        ? IdentifierType.ISBN
                        : IdentifierType.ISSN,

                    Title = dto.Title,
                    Author = dto.Author,
                    Publisher = dto.Publisher,
                    YearPublished = dto.YearPublished,
                    Price = dto.Price,
                    CoverUrl = NormalizeCoverUrl(dto.CoverUrl),
                    Language = dto.Language,
                    Genre = dto.Genre ?? new List<string>(),
                    CategoryCode = await ResolveValidCategoryCodeAsync(ctx, dto.CategoryCode, dto.Location),
                };

                ctx.Books.Add(book);
                await ctx.SaveChangesAsync();

                var inventoryNumber = await GenerateNextInventoryAsync(ctx);
                var reviewerUsername = await GetReviewerUsernameAsync(ctx, reviewedByUserId);

                var copy = new BookInventory
                {
                    BookId = book.Id,
                    InventoryNumber = inventoryNumber,
                    // Inventory location is always the main category (x.00.00).
                    Location = ToMainCategoryCode(book.CategoryCode),
                    IsAvailable = true,
                    CreatedAt = DateTime.UtcNow,
                    IssnVolume = string.IsNullOrWhiteSpace(dto.IssnVolume) ? null : dto.IssnVolume.Trim(),
                    IssnNumber = string.IsNullOrWhiteSpace(dto.IssnNumber) ? null : dto.IssnNumber.Trim(),
                };

                ctx.BookCopies.Add(copy);

                if (dto.Price.HasValue)
                {
                    ctx.WishlistPriceReviews.Add(BuildManualPriceReview(
                        book,
                        dto.Price.Value,
                        reviewedByUserId,
                        reviewerUsername,
                        null,
                        WishlistPriceReview.SourceManual
                    ));
                }

                await ctx.SaveChangesAsync();
                return book;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> UpdateBook(int id, UpdateBookDto dto, int? reviewedByUserId = null)
        {
            try
            {
                using var scope = _provider.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var book = await ctx.Books.FirstOrDefaultAsync(x => x.Id == id);
                if (book == null) return false;
                var previousPrice = book.Price;

                book.Title = dto.Title;
                book.Author = dto.Author;
                book.Publisher = dto.Publisher;
                book.YearPublished = dto.YearPublished;
                book.Price = dto.Price;
                book.CoverUrl = NormalizeCoverUrl(dto.CoverUrl);
                book.Language = dto.Language;
                book.Genre = dto.Genre ?? new List<string>();

                if (HasPriceChanged(previousPrice, dto.Price))
                {
                    var reviewerUsername = await GetReviewerUsernameAsync(ctx, reviewedByUserId);
                    ctx.WishlistPriceReviews.Add(BuildManualPriceReview(
                        book,
                        dto.Price!.Value,
                        reviewedByUserId,
                        reviewerUsername,
                        null,
                        WishlistPriceReview.SourceEdited
                    ));
                }

                await ctx.SaveChangesAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteBook(int id)
        {
            try
            {
                using var scope = _provider.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var book = await ctx.Books.FirstOrDefaultAsync(x => x.Id == id);
                if (book == null) return false;

                ctx.Books.Remove(book);
                await ctx.SaveChangesAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }
        public async Task<bool> SetCategoryAsync(int bookId, string categoryCode)
        {
            if (string.IsNullOrWhiteSpace(categoryCode))
                throw new ArgumentException("CategoryCode is required.", nameof(categoryCode));

            var code = categoryCode.Trim();

            // validiraj proti DB (dovoli level 1 ali 2)
            var cat = await _context.Categories
                .AsNoTracking()
                .Where(c => c.Code == code)
                .Select(c => new { c.Code, c.Level })
                .FirstOrDefaultAsync();

            if (cat == null)
                throw new ArgumentException("Invalid category code.", nameof(categoryCode));

            if (cat.Level is not (1 or 2))
                throw new ArgumentException("Only level 1 or level 2 categories are allowed.", nameof(categoryCode));

            var book = await _context.Books.FirstOrDefaultAsync(b => b.Id == bookId);
            if (book == null) return false;

            book.CategoryCode = cat.Code;
            var mainLocationCode = ToMainCategoryCode(cat.Code);
            var copies = await _context.BookCopies.Where(c => c.BookId == bookId).ToListAsync();
            foreach (var copy in copies)
            {
                copy.Location = mainLocationCode;
            }
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> NormalizeCategoriesFromInventoryAsync()
        {
            using var scope = _provider.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var books = await ctx.Books
                .Where(b => string.IsNullOrWhiteSpace(b.CategoryCode) || b.CategoryCode == "9.00.00")
                .ToListAsync();

            if (books.Count == 0) return 0;

            var validCodes = await ctx.Categories
                .AsNoTracking()
                .Where(c => c.Level == 1 || c.Level == 2)
                .Select(c => c.Code)
                .ToHashSetAsync();

            var targetIds = books.Select(b => b.Id).ToHashSet();

            var firstCopyLocations = await ctx.BookCopies
                .AsNoTracking()
                .Where(c => targetIds.Contains(c.BookId) && !string.IsNullOrWhiteSpace(c.Location))
                .OrderBy(c => c.BookId)
                .ThenBy(c => c.CreatedAt ?? DateTime.MaxValue)
                .ThenBy(c => c.Id)
                .Select(c => new { c.BookId, c.Location })
                .ToListAsync();

            var locationByBookId = new Dictionary<int, string>();
            foreach (var row in firstCopyLocations)
            {
                if (!locationByBookId.ContainsKey(row.BookId) && !string.IsNullOrWhiteSpace(row.Location))
                    locationByBookId[row.BookId] = row.Location!;
            }

            var updated = 0;
            foreach (var book in books)
            {
                if (!locationByBookId.TryGetValue(book.Id, out var location))
                    continue;

                var parsedCode = TryExtractCategoryCode(location);
                if (string.IsNullOrWhiteSpace(parsedCode))
                    continue;

                if (!validCodes.Contains(parsedCode))
                    continue;

                book.CategoryCode = parsedCode;
                updated++;
            }

            if (updated > 0)
                await ctx.SaveChangesAsync();

            return updated;
        }

        private async Task<string> ResolveValidCategoryCodeAsync(
            ApplicationDbContext ctx,
            string? rawCode,
            string? rawLocation = null)
        {
            var candidate = string.IsNullOrWhiteSpace(rawCode)
                ? TryExtractCategoryCode(rawLocation)
                : rawCode.Trim();

            if (string.IsNullOrWhiteSpace(candidate))
                return "9.00.00";

            var exists = await ctx.Categories
                .AsNoTracking()
                .AnyAsync(c => c.Code == candidate && (c.Level == 1 || c.Level == 2));

            return exists ? candidate : "9.00.00";
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


        // ======================================================
        // HELPERS
        // ======================================================

        private static bool IsIssn(string? raw)
        {
            var n = NormalizeIssn(raw);
            if (n.Length != 8) return false;

            // prvih 7 mora bit digit, zadnji digit ali X
            for (int i = 0; i < 7; i++)
                if (!char.IsDigit(n[i])) return false;

            return char.IsDigit(n[7]) || n[7] == 'X';
        }

        private static bool IsIsbn13(string s)
            => Regex.IsMatch(NormalizeIsbn(s), @"^\d{13}$");

        private static bool IsIsbn10(string s)
            => Regex.IsMatch(NormalizeIsbn(s), @"^\d{9}[\dXx]$");

        private static string NormalizeIsbn(string s)
            => s.Replace("-", "").Replace(" ", "");

        public int? ParseYear(string? publishDate)
        {
            if (string.IsNullOrWhiteSpace(publishDate))
                return null;

            if (int.TryParse(publishDate, out var y))
                return y;

            var parts = publishDate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (int.TryParse(parts.Last(), out y))
                return y;

            return null;
        }

        public string? ExtractLanguage(List<OpenLibraryKey>? langs)
            => LanguageCodeHelper.NormalizeBookLanguage(
                langs?.FirstOrDefault()?.Key?.Replace("/languages/", ""));

        // ---------- GENRES (human readable only) ----------
        public List<string> ExtractGenres(List<string>? subjects, int max = 6)
        {
            if (subjects == null || subjects.Count == 0)
                return new();

            var keywords = new[]
            {
                "fantasy", "fiction", "novel", "literature", "epic",
                "romance", "thriller", "mystery",
                "science fiction", "sci-fi", "horror", "adventure"
            };

            var result = new List<string>();

            foreach (var s in subjects)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;

                var lower = s.ToLowerInvariant();

                // skip noisy OpenLibrary metadata
                if (lower.Contains(":") && !lower.StartsWith("form:") && !lower.StartsWith("genre:"))
                    continue;

                if (keywords.Any(k => lower.Contains(k)))
                {
                    var clean = s
                        .Replace("form:", "", StringComparison.OrdinalIgnoreCase)
                        .Replace("genre:", "", StringComparison.OrdinalIgnoreCase)
                        .Trim();

                    clean = char.ToUpper(clean[0]) + clean[1..];

                    if (!result.Any(x => x.Equals(clean, StringComparison.OrdinalIgnoreCase)))
                        result.Add(clean);

                    if (result.Count >= max) break;
                }
            }

            return result;
        }

        // ---------- ISSN JSON-LD helpers ----------
        private static Dictionary<string, JsonElement> BuildGraphIndex(JsonElement root)
        {
            var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("@graph", out var graph) &&
                graph.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in graph.EnumerateArray())
                {
                    if (node.ValueKind != JsonValueKind.Object) continue;

                    if (node.TryGetProperty("@id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    {
                        var id = idEl.GetString();
                        if (!string.IsNullOrWhiteSpace(id) && !dict.ContainsKey(id))
                            dict.Add(id, node);
                    }
                }
            }

            return dict;
        }
        private static JsonElement? FindIssnNode(
            Dictionary<string, JsonElement> index,
            string issn)
        {
            var normalized = NormalizeIssn(issn);
            if (normalized.Length != 8) return null;

            // 1) direct hit (obe obliki)
            var canonical = normalized.Insert(4, "-"); // 1234-5678

            var candidates = new[]
            {
                $"resource/ISSN/{canonical}",
                $"resource/ISSN/{normalized}",
                $"https://issn.org/resource/ISSN/{canonical}",
                $"https://issn.org/resource/ISSN/{normalized}"
            };

            foreach (var key in candidates)
            {
                if (index.TryGetValue(key, out var node))
                    return node;
            }

            // 2) fallback: normaliziran brute scan
            foreach (var kv in index)
            {
                if (!kv.Key.Contains("/ISSN/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var keyNormalized = NormalizeIssn(kv.Key);
                if (keyNormalized == normalized)
                    return kv.Value;
            }

            return null;
        }

        private static string? ExtractIssnTitle(JsonElement issnNode)
        {
            // mainTitle je v tvojem primeru top
            return FirstNonEmpty(
                GetString(issnNode, "mainTitle"),
                GetString(issnNode, "label"),
                GetString(issnNode, "title"),
                // name je array, v tvojem JSON-u obstaja
                GetFirstStringFromArray(issnNode, "name")
            );
        }
        private static string? ExtractPublicationRef(JsonElement issnNode)
        {
            return GetString(issnNode, "publication");
        }
        private static string? ExtractIssnPublisher(Dictionary<string, JsonElement> index, string? publicationRef)
        {
            if (string.IsNullOrWhiteSpace(publicationRef)) return null;
            if (!index.TryGetValue(publicationRef, out var pubNode)) return null;

            // pogoste variante: publishedBy / publisher / agent / organization
            var publisherRef = FirstNonEmpty(
                GetString(pubNode, "publishedBy"),
                GetString(pubNode, "publisher"),
                GetString(pubNode, "agent")
            );

            if (string.IsNullOrWhiteSpace(publisherRef))
            {
                // včasih je publisher kar string
                return FirstNonEmpty(
                    GetString(pubNode, "publisherName"),
                    GetString(pubNode, "name"),
                    GetString(pubNode, "label")
                );
            }

            // če je to referenca na drug node, poskusi iz njega potegniti name/label
            if (index.TryGetValue(publisherRef, out var orgNode))
            {
                return FirstNonEmpty(
                    GetString(orgNode, "name"),
                    GetString(orgNode, "label")
                );
            }

            // če reference ni v indexu, vrni vsaj reference string (bolje kot prazno)
            return publisherRef;
        }
        private static string? GetString(JsonElement obj, string prop)
        {
            if (obj.ValueKind != JsonValueKind.Object) return null;
            if (!obj.TryGetProperty(prop, out var el)) return null;

            if (el.ValueKind == JsonValueKind.String) return el.GetString();

            if (el.ValueKind == JsonValueKind.Object &&
                el.TryGetProperty("@value", out var v) &&
                v.ValueKind == JsonValueKind.String)
                return v.GetString();

            // včasih je array referenc/stringov
            if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in el.EnumerateArray())
                    if (it.ValueKind == JsonValueKind.String)
                        return it.GetString();
            }

            return null;
        }

        private static string? GetFirstStringFromArray(JsonElement obj, string prop)
        {
            if (obj.ValueKind != JsonValueKind.Object) return null;
            if (!obj.TryGetProperty(prop, out var el)) return null;
            if (el.ValueKind != JsonValueKind.Array) return null;

            foreach (var it in el.EnumerateArray())
                if (it.ValueKind == JsonValueKind.String)
                    return it.GetString();

            return null;
        }

        private static string? FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        private static string NormalizeIssn(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            return Regex.Replace(raw.ToUpperInvariant(), @"[^0-9X]", "");
        }

        private static string ToCanonicalIssn(string normalized)
        {
            // pričakujemo 8 znakov (zadnji je lahko X)
            if (normalized.Length != 8) return normalized;
            return normalized.Insert(4, "-"); // 12345678 -> 1234-5678
        }

        private static HttpRequestMessage CreateIssnRequest(string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
            req.Headers.Accept.ParseAdd("application/ld+json, application/json;q=0.9, */*;q=0.8");
            req.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            req.Headers.Referrer = new Uri("https://portal.issn.org/");
            return req;
        }

        private async Task<(IssnLookupStatus status, string? title, string? publisher)> TryFetchIssnPortalAsync(HttpClient client, string canonical)
        {
            try
            {
                var url = $"https://portal.issn.org/resource/ISSN/{Uri.EscapeDataString(canonical)}?format=json";
                using var req = CreateIssnRequest(url);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                using var res = await client.SendAsync(req, cts.Token);

                if (!res.IsSuccessStatusCode)
                {
                    Console.WriteLine($"ISSN PORTAL FAIL status={(int)res.StatusCode} url='{url}'");
                    if (res.StatusCode == HttpStatusCode.GatewayTimeout ||
                        res.StatusCode == HttpStatusCode.BadGateway ||
                        res.StatusCode == HttpStatusCode.ServiceUnavailable ||
                        res.StatusCode == HttpStatusCode.RequestTimeout ||
                        res.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        return (IssnLookupStatus.ProviderUnavailable, null, null);
                    }
                    return (IssnLookupStatus.NotFound, null, null);
                }

                var json = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                var index = BuildGraphIndex(doc.RootElement);
                var issnNode = FindIssnNode(index, canonical);
                if (!issnNode.HasValue)
                    return (IssnLookupStatus.NotFound, null, null);

                var title = ExtractIssnTitle(issnNode.Value);
                var pubRef = ExtractPublicationRef(issnNode.Value);
                var publisher = ExtractIssnPublisher(index, pubRef);
                return (IssnLookupStatus.Success, title, publisher);
            }
            catch (TaskCanceledException)
            {
                return (IssnLookupStatus.ProviderUnavailable, null, null);
            }
            catch (HttpRequestException)
            {
                return (IssnLookupStatus.ProviderUnavailable, null, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine("ISSN portal lookup exception: " + ex.Message);
                return (IssnLookupStatus.NotFound, null, null);
            }
        }

        private async Task<(IssnLookupStatus status, string? title, string? publisher, string? canonicalIssn)> TryFetchCrossrefJournalAsync(HttpClient client, string canonical)
        {
            try
            {
                var url = $"https://api.crossref.org/journals/{Uri.EscapeDataString(canonical)}";
                using var res = await client.GetAsync(url);

                if (!res.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Crossref FAIL status={(int)res.StatusCode} url='{url}'");
                    if (res.StatusCode == HttpStatusCode.GatewayTimeout ||
                        res.StatusCode == HttpStatusCode.BadGateway ||
                        res.StatusCode == HttpStatusCode.ServiceUnavailable ||
                        res.StatusCode == HttpStatusCode.RequestTimeout ||
                        res.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        return (IssnLookupStatus.ProviderUnavailable, null, null, null);
                    }
                    return (IssnLookupStatus.NotFound, null, null, null);
                }

                using var stream = await res.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;

                if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
                    return (IssnLookupStatus.NotFound, null, null, null);

                var title = GetString(message, "title");
                var publisher = GetString(message, "publisher");
                var canonicalIssn = ExtractCrossrefCanonicalIssn(message) ?? canonical;

                if (string.IsNullOrWhiteSpace(title))
                    return (IssnLookupStatus.NotFound, null, null, null);

                return (IssnLookupStatus.Success, title, publisher, canonicalIssn);
            }
            catch (TaskCanceledException)
            {
                return (IssnLookupStatus.ProviderUnavailable, null, null, null);
            }
            catch (HttpRequestException)
            {
                return (IssnLookupStatus.ProviderUnavailable, null, null, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Crossref lookup exception: " + ex.Message);
                return (IssnLookupStatus.NotFound, null, null, null);
            }
        }

        private static string? ExtractCrossrefCanonicalIssn(JsonElement message)
        {
            if (message.TryGetProperty("ISSN", out var issnArray) && issnArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in issnArray.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var raw = item.GetString();
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    var normalized = NormalizeIssn(raw);
                    if (normalized.Length == 8)
                        return ToCanonicalIssn(normalized);
                }
            }

            if (message.TryGetProperty("issn-type", out var typeArray) && typeArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in typeArray.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var raw = GetString(item, "value");
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    var normalized = NormalizeIssn(raw);
                    if (normalized.Length == 8)
                        return ToCanonicalIssn(normalized);
                }
            }

            return null;
        }

        private static bool NeedsIssnEnrichment(Book book)
        {
            var unknownTitle = string.IsNullOrWhiteSpace(book.Title) ||
                               book.Title.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
            var missingPublisher = string.IsNullOrWhiteSpace(book.Publisher);
            return unknownTitle || missingPublisher;
        }

        private async Task<(string? title, string? author, string? publisher, int? year, string? language, List<string> genres)>
            TryFetchGoogleBooksAsync(HttpClient client, string isbn)
        {
            try
            {
                var url = $"https://www.googleapis.com/books/v1/volumes?q=isbn:{Uri.EscapeDataString(isbn)}&maxResults=1";
                using var res = await client.GetAsync(url);
                if (!res.IsSuccessStatusCode)
                    return (null, null, null, null, null, new List<string>());

                using var stream = await res.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);

                if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                    return (null, null, null, null, null, new List<string>());

                var first = items.EnumerateArray().FirstOrDefault();
                if (first.ValueKind != JsonValueKind.Object ||
                    !first.TryGetProperty("volumeInfo", out var volumeInfo) ||
                    volumeInfo.ValueKind != JsonValueKind.Object)
                {
                    return (null, null, null, null, null, new List<string>());
                }

                var title = GetString(volumeInfo, "title");
                var author = GetArrayJoined(volumeInfo, "authors");
                var publisher = GetString(volumeInfo, "publisher");
                var year = ParseYear(GetString(volumeInfo, "publishedDate"));
                var language = NormalizeGoogleLanguage(GetString(volumeInfo, "language"));
                var genres = GetStringList(volumeInfo, "categories", 6);

                return (title, author, publisher, year, language, genres);
            }
            catch
            {
                return (null, null, null, null, null, new List<string>());
            }
        }

        private static string? GetArrayJoined(JsonElement obj, string prop)
        {
            if (obj.ValueKind != JsonValueKind.Object) return null;
            if (!obj.TryGetProperty(prop, out var el)) return null;
            if (el.ValueKind != JsonValueKind.Array) return null;

            var values = el.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            return values.Count == 0 ? null : string.Join(", ", values);
        }

        private static List<string> GetStringList(JsonElement obj, string prop, int max)
        {
            if (obj.ValueKind != JsonValueKind.Object) return new List<string>();
            if (!obj.TryGetProperty(prop, out var el)) return new List<string>();
            if (el.ValueKind != JsonValueKind.Array) return new List<string>();

            return el.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(max)
                .Cast<string>()
                .ToList();
        }

        private static string? NormalizeGoogleLanguage(string? lang)
            => LanguageCodeHelper.NormalizeBookLanguage(lang);

        private static string? NormalizeCoverUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return value.Trim();
        }

        private static bool HasPriceChanged(decimal? previousPrice, decimal? incomingPrice)
        {
            if (!incomingPrice.HasValue) return false;
            if (!previousPrice.HasValue) return true;
            return previousPrice.Value != incomingPrice.Value;
        }

        private static WishlistPriceReview BuildManualPriceReview(
            Book book,
            decimal price,
            int? reviewedByUserId,
            string? reviewerUsername,
            int? centerId,
            string source)
        {
            return new WishlistPriceReview
            {
                WishlistRequestId = null,
                Year = DateTime.UtcNow.Year,
                Price = price,
                ReviewedAtUtc = DateTime.UtcNow,
                ReviewedByUserId = reviewedByUserId,
                Title = book.Title,
                Author = book.Author,
                ISBN = !string.IsNullOrWhiteSpace(book.ISBN) ? book.ISBN : book.ISSN,
                CenterId = centerId,
                Username = reviewerUsername,
                Source = source
            };
        }

        private static async Task<string?> GetReviewerUsernameAsync(ApplicationDbContext ctx, int? reviewedByUserId)
        {
            if (!reviewedByUserId.HasValue) return null;
            return await ctx.Users
                .AsNoTracking()
                .Where(u => u.Id == reviewedByUserId.Value)
                .Select(u => u.Username)
                .FirstOrDefaultAsync();
        }

        private static async Task<string> GenerateNextInventoryAsync(ApplicationDbContext ctx)
        {
            var numbers = await ctx.BookCopies
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

            return next.ToString("D6");
        }

    }
}
