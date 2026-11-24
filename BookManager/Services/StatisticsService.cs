using BookManagerApi.Data;
using BookManagerApi.DTOs;
using Microsoft.EntityFrameworkCore;

namespace BookManagerApi.Services
{
    public interface IStatisticsService
    {
        Task<List<BookGrowthPointDto>> GetBookGrowthAsync(DateTime? fromUtc, DateTime? toUtc, string bucket = "month");
        Task<List<TopSubcategoryDto>> GetTopSubcategoriesAsync(DateTime? fromUtc, DateTime? toUtc, int top = 10);
    }

    public class StatisticsService : IStatisticsService
    {
        private readonly ApplicationDbContext _db;

        public StatisticsService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<List<BookGrowthPointDto>> GetBookGrowthAsync(DateTime? fromUtc, DateTime? toUtc, string bucket = "month")
        {
            var normalizedBucket = NormalizeBucket(bucket);

            var minCreatedUtc = await _db.BookCopies
                .AsNoTracking()
                .Where(c => c.CreatedAt.HasValue)
                .Select(c => c.CreatedAt!.Value)
                .OrderBy(d => d)
                .FirstOrDefaultAsync();

            var nowUtc = DateTime.UtcNow;

            // Če ni podatkov, vrnemo en prazen period point za trenutni period.
            if (minCreatedUtc == default)
            {
                var emptyPeriod = StartOfPeriod(nowUtc, normalizedBucket);
                return new List<BookGrowthPointDto>
                {
                    new()
                    {
                        PeriodStartUtc = emptyPeriod,
                        Added = 0,
                        Total = 0
                    }
                };
            }

            var fromBase = fromUtc.HasValue ? ToUtc(fromUtc.Value) : minCreatedUtc;
            var toBase = toUtc.HasValue ? ToUtc(toUtc.Value) : nowUtc;

            if (toBase < fromBase)
                throw new ArgumentException("'toUtc' must be greater than or equal to 'fromUtc'.");

            var fromPeriodStart = StartOfPeriod(fromBase, normalizedBucket);
            var toPeriodStart = StartOfPeriod(toBase, normalizedBucket);
            var toExclusive = AddPeriod(toPeriodStart, normalizedBucket);

            var estimatedPeriods = CountPeriodsInclusive(fromPeriodStart, toPeriodStart, normalizedBucket);
            if (estimatedPeriods > 5000)
                throw new ArgumentException("Requested range is too large for selected bucket.");

            // baseline vsebuje tudi null CreatedAt, ker gre za "stare" zapise brez datuma.
            var baseline = await _db.BookCopies
                .AsNoTracking()
                .CountAsync(c => !c.CreatedAt.HasValue || c.CreatedAt.Value < fromPeriodStart);

            var periodAdded = normalizedBucket == "day"
                ? await _db.BookCopies
                    .AsNoTracking()
                    .Where(c => c.CreatedAt.HasValue
                             && c.CreatedAt.Value >= fromPeriodStart
                             && c.CreatedAt.Value < toExclusive)
                    .GroupBy(c => new
                    {
                        c.CreatedAt!.Value.Year,
                        c.CreatedAt!.Value.Month,
                        c.CreatedAt!.Value.Day
                    })
                    .Select(g => new
                    {
                        g.Key.Year,
                        g.Key.Month,
                        g.Key.Day,
                        Added = g.Count()
                    })
                    .ToListAsync()
                : await _db.BookCopies
                    .AsNoTracking()
                    .Where(c => c.CreatedAt.HasValue
                             && c.CreatedAt.Value >= fromPeriodStart
                             && c.CreatedAt.Value < toExclusive)
                    .GroupBy(c => new
                    {
                        c.CreatedAt!.Value.Year,
                        c.CreatedAt!.Value.Month
                    })
                    .Select(g => new
                    {
                        g.Key.Year,
                        g.Key.Month,
                        Day = 1,
                        Added = g.Count()
                    })
                    .ToListAsync();

            var addedByPeriod = periodAdded.ToDictionary(
                x => new DateTime(x.Year, x.Month, x.Day, 0, 0, 0, DateTimeKind.Utc),
                x => x.Added);

            var result = new List<BookGrowthPointDto>();
            var runningTotal = baseline;

            for (var p = fromPeriodStart; p <= toPeriodStart; p = AddPeriod(p, normalizedBucket))
            {
                var added = addedByPeriod.TryGetValue(p, out var count) ? count : 0;
                runningTotal += added;

                result.Add(new BookGrowthPointDto
                {
                    PeriodStartUtc = p,
                    Added = added,
                    Total = runningTotal
                });
            }

            return result;
        }

        public async Task<List<TopSubcategoryDto>> GetTopSubcategoriesAsync(DateTime? fromUtc, DateTime? toUtc, int top = 10)
        {
            top = Math.Clamp(top, 1, 50);

            var nowUtc = DateTime.UtcNow;
            var hasFrom = fromUtc.HasValue;
            var hasTo = toUtc.HasValue;
            var fromBase = hasFrom ? ToUtc(fromUtc!.Value) : (DateTime?)null;
            var toBase = hasTo ? ToUtc(toUtc!.Value) : nowUtc;

            if (hasFrom && toBase < fromBase)
                throw new ArgumentException("'toUtc' must be greater than or equal to 'fromUtc'.");

            var rawCountsInRange = await (
                from c in _db.BookCopies.AsNoTracking()
                join b in _db.Books.AsNoTracking() on c.BookId equals b.Id
                where c.CreatedAt.HasValue
                   && (!hasFrom || c.CreatedAt.Value >= fromBase!.Value)
                   && c.CreatedAt.Value <= toBase
                group c by b.CategoryCode into g
                select new
                {
                    CategoryCode = g.Key,
                    AddedCopies = g.Count()
                }
            ).ToListAsync();

            var filtered = rawCountsInRange
                .Where(x => IsSubcategoryCode(x.CategoryCode))
                .OrderByDescending(x => x.AddedCopies)
                .ThenBy(x => x.CategoryCode)
                .Take(top)
                .ToList();

            // Fallback 1: if there are no subcategories in selected range, show top main categories.
            if (filtered.Count == 0)
            {
                filtered = rawCountsInRange
                    .Where(x => IsMainCategoryCode(x.CategoryCode))
                    .OrderByDescending(x => x.AddedCopies)
                    .ThenBy(x => x.CategoryCode)
                    .Take(top)
                    .ToList();
            }

            // Fallback 2: if range has no dated rows (or empty result), use all copies by category.
            if (filtered.Count == 0)
            {
                var rawCountsAll = await (
                    from c in _db.BookCopies.AsNoTracking()
                    join b in _db.Books.AsNoTracking() on c.BookId equals b.Id
                    group c by b.CategoryCode into g
                    select new
                    {
                        CategoryCode = g.Key,
                        AddedCopies = g.Count()
                    }
                ).ToListAsync();

                filtered = rawCountsAll
                    .Where(x => IsSubcategoryCode(x.CategoryCode))
                    .OrderByDescending(x => x.AddedCopies)
                    .ThenBy(x => x.CategoryCode)
                    .Take(top)
                    .ToList();

                if (filtered.Count == 0)
                {
                    filtered = rawCountsAll
                        .Where(x => IsMainCategoryCode(x.CategoryCode))
                        .OrderByDescending(x => x.AddedCopies)
                        .ThenBy(x => x.CategoryCode)
                        .Take(top)
                        .ToList();
                }
            }

            var codes = filtered
                .Select(x => x.CategoryCode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            var categoryNames = codes.Count == 0
                ? new Dictionary<string, string>()
                : await _db.Categories
                    .AsNoTracking()
                    .Where(c => codes.Contains(c.Code))
                    .ToDictionaryAsync(c => c.Code, c => c.Name);

            return filtered
                .Where(x => !string.IsNullOrWhiteSpace(x.CategoryCode))
                .Select(x => new TopSubcategoryDto
                {
                    CategoryCode = x.CategoryCode!,
                    CategoryName = categoryNames.TryGetValue(x.CategoryCode!, out var name) ? name : null,
                    AddedCopies = x.AddedCopies
                })
                .ToList();
        }

        private static string NormalizeBucket(string? bucket)
        {
            var value = (bucket ?? "month").Trim().ToLowerInvariant();
            return value is "day" or "month" ? value : "month";
        }

        private static DateTime ToUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        private static DateTime StartOfPeriod(DateTime valueUtc, string bucket)
        {
            var utc = ToUtc(valueUtc);
            return bucket == "day"
                ? new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc)
                : new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        private static DateTime AddPeriod(DateTime periodStartUtc, string bucket)
        {
            return bucket == "day"
                ? periodStartUtc.AddDays(1)
                : periodStartUtc.AddMonths(1);
        }

        private static int CountPeriodsInclusive(DateTime fromPeriodUtc, DateTime toPeriodUtc, string bucket)
        {
            if (bucket == "day")
                return (int)(toPeriodUtc - fromPeriodUtc).TotalDays + 1;

            return ((toPeriodUtc.Year - fromPeriodUtc.Year) * 12) + (toPeriodUtc.Month - fromPeriodUtc.Month) + 1;
        }

        private static bool IsSubcategoryCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;

            var parts = code.Split('.');
            if (parts.Length != 3) return false;

            return !string.Equals(parts[1], "00", StringComparison.Ordinal);
        }

        private static bool IsMainCategoryCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;

            var parts = code.Split('.');
            if (parts.Length != 3) return false;

            return string.Equals(parts[1], "00", StringComparison.Ordinal)
                && string.Equals(parts[2], "00", StringComparison.Ordinal);
        }
    }
}
