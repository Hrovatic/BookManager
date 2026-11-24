using BookManagerApi.Data;
using BookManagerApi.DTOs;
using BookManagerApi.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic;
using System;
using System.Net;
using System.Net.NetworkInformation;

namespace BookManagerApi.Services
{
    public class BorrowingService
    {
        private readonly ApplicationDbContext _ctx;
        private readonly IEmailService _emailService;
        public BorrowingService(ApplicationDbContext ctx, IEmailService emailService)
        { 
            _ctx = ctx;
            _emailService = emailService;
        }

        public async Task<int> RefreshOverdueStatusesAsync(DateTime utcNow)
        {
            var openBorrowings = await _ctx.Borrowings
                .Where(b => b.ReturnedAt == null)
                .ToListAsync();

            var changedCount = 0;

            foreach (var borrowing in openBorrowings)
            {
                var nextStatus = borrowing.DueDate < utcNow ? "overdue" : "active";
                if (string.Equals(borrowing.Status, nextStatus, StringComparison.OrdinalIgnoreCase))
                    continue;

                borrowing.Status = nextStatus;
                changedCount++;
            }

            if (changedCount > 0)
            {
                await _ctx.SaveChangesAsync();
            }

            return changedCount;
        }

        public async Task<List<BorrowingDto>> GetAll() 
        { 
            await RefreshOverdueStatusesAsync(DateTime.UtcNow);

            return await _ctx.Borrowings
                .Select(b => new BorrowingDto 
                { 
                    Id = b.Id, 
                    BookTitle = b.BookTitle, 
                    UserName = b.UserName, 
                    BorrowedAt = b.BorrowedAt, 
                    DueDate = b.DueDate, 
                    Status = b.Status 
                }).ToListAsync(); 
        }
        public async Task<int> ReturnBooks(List<int> ids)
        {
            var returnedCount = 0;
            var affectedBookIds = new HashSet<int>();

            foreach (var id in ids.Distinct())
            {
                var borrowing = await _ctx.Borrowings
                    .Include(b => b.BookInventory)
                    .FirstOrDefaultAsync(b => b.Id == id);

                if (borrowing == null) continue;
                if (borrowing.ReturnedAt != null) continue;

                borrowing.ReturnedAt = DateTime.UtcNow;
                borrowing.Status = "returned";

                borrowing.BookInventory.IsAvailable = true;
                affectedBookIds.Add(borrowing.BookInventory.BookId);

                returnedCount++;
            }

            if (returnedCount > 0)
            {
                await _ctx.SaveChangesAsync();
                await NotifyAvailableBooksAsync(affectedBookIds);
            }

            return returnedCount;
        }

        public async Task<BorrowingDto?> Borrow(BorrowingCreateDto dto)
        {
            // Preveri, ali knjiga obstaja
            var book = await _ctx.Books.FirstOrDefaultAsync(b => b.Id == dto.BookId);
            if (book == null) return null;


            int userId = int.Parse(dto.UserId);
            // Preveri, ali uporabnik obstaja
            var user = await _ctx.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return null;

            // Najdi prosto knjigo
            var copy = await _ctx.BookCopies.FirstOrDefaultAsync(c => c.BookId == dto.BookId && c.IsAvailable);
            if (copy == null) return null;

            // Označi kopijo kot zasedeno
            copy.IsAvailable = false;

            // Ustvari borrowing, ki kaže na copy.Id
            var borrowing = new Borrowing
            {
                BookInventoryId = copy.Id,
                BookTitle = book.Title,
                UserId = user.Id,
                UserName = user.Username,
                BorrowedAt = DateTime.UtcNow,
                DueDate = DateTime.UtcNow.AddDays(14),
                ReturnedAt = null,
                Status = "active"
            };

            _ctx.Borrowings.Add(borrowing);
            await _ctx.SaveChangesAsync();

            // 4) Vrni DTO
            return new BorrowingDto
            {
                Id = borrowing.Id,
                BookTitle = borrowing.BookTitle,
                UserName = borrowing.UserName,
                BorrowedAt = borrowing.BorrowedAt,
                DueDate = borrowing.DueDate,
                Status = borrowing.Status
            };
        }

        public async Task<(bool ok, string message)> SubscribeAvailabilityAlertAsync(int userId, int bookId)
        {
            if (bookId <= 0)
                return (false, "BookId is required.");

            var user = await _ctx.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return (false, "User not found.");

            if (string.IsNullOrWhiteSpace(user.Email))
                return (false, "User has no email.");

            var book = await _ctx.Books
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == bookId);
            if (book == null)
                return (false, "Book not found.");

            var isCurrentlyAvailable = await _ctx.BookCopies
                .AnyAsync(c => c.BookId == bookId && c.IsAvailable);
            if (isCurrentlyAvailable)
                return (false, "Book is already available.");

            var alreadySubscribed = await _ctx.BookAvailabilityNotifications
                .AnyAsync(n => n.UserId == userId && n.BookId == bookId && n.IsActive);
            if (alreadySubscribed)
                return (true, "Already subscribed.");

            _ctx.BookAvailabilityNotifications.Add(new BookAvailabilityNotification
            {
                UserId = userId,
                BookId = bookId,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });

            await _ctx.SaveChangesAsync();
            return (true, "Subscription created.");
        }

        public async Task<BorrowingActiveDto?> GetActiveByBookId(int bookId)
        {
            await RefreshOverdueStatusesAsync(DateTime.UtcNow);

            return await _ctx.Borrowings
                .Where(b => b.BookInventoryId == bookId && b.ReturnedAt == null)
                .Select(b => new BorrowingActiveDto
                {
                    BorrowingId = b.Id,
                    BookId = b.BookInventoryId,
                    BorrowedAt = b.BorrowedAt,
                    DueDate = b.DueDate,
                    Status = b.Status,
                }).FirstOrDefaultAsync();
        }

        public async Task<bool> ExtendBorrowings(List<int> ids) 
        { 
            var items = await _ctx.Borrowings.Where(b => ids.Contains(b.Id)).ToListAsync(); 
            var utcNow = DateTime.UtcNow;

            foreach (var item in items)
            {
                item.DueDate = item.DueDate.AddDays(14);
                if (item.ReturnedAt == null)
                {
                    item.Status = item.DueDate < utcNow ? "overdue" : "active";
                }
            }
            
            await _ctx.SaveChangesAsync(); 
            return true; 
        }

        public async Task<bool> DeleteBorrowings(List<int> ids) 
        { 
            var items = await _ctx.Borrowings.Where(b => ids.Contains(b.Id)).ToListAsync(); 
            foreach (var borrowing in items)
            {
                var book = await _ctx.Books.FirstOrDefaultAsync(b => b.Id == borrowing.BookInventoryId);
            
                borrowing.Status = "returned";
                borrowing.ReturnedAt = DateTime.UtcNow;
            }

            _ctx.Borrowings.RemoveRange(items); 
            await _ctx.SaveChangesAsync(); 
            return true; 
        }

        public async Task<List<LastActivityDto>> GetLastActivities(int userId, int count = 5)
        {
            await RefreshOverdueStatusesAsync(DateTime.UtcNow);

            return await _ctx.Borrowings
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.ReturnedAt ?? b.BorrowedAt)
                .Take(count)
                .Select(b => new LastActivityDto
                {
                    Id = b.Id,
                    BookId = b.BookInventoryId,
                    BookTitle = b.BookTitle,
                    Status = b.Status,
                    BorrowedAt = b.BorrowedAt,
                    ReturnedAt = b.ReturnedAt
                })
                .ToListAsync();
        }

        private async Task NotifyAvailableBooksAsync(IEnumerable<int> bookIds)
        {
            var ids = (bookIds ?? Enumerable.Empty<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (ids.Count == 0) return;

            var availableBookIds = await _ctx.BookCopies
                .Where(c => ids.Contains(c.BookId) && c.IsAvailable)
                .Select(c => c.BookId)
                .Distinct()
                .ToListAsync();
            if (availableBookIds.Count == 0) return;

            var notifications = await _ctx.BookAvailabilityNotifications
                .Include(n => n.User)
                .Include(n => n.Book)
                .Where(n => n.IsActive && availableBookIds.Contains(n.BookId))
                .ToListAsync();
            if (notifications.Count == 0) return;

            foreach (var n in notifications)
            {
                var to = n.User?.Email?.Trim();
                if (string.IsNullOrWhiteSpace(to)) continue;

                var isEn = NormalizeLanguage(n.User?.PreferredLanguage) == "en";
                var title = string.IsNullOrWhiteSpace(n.Book?.Title) ? (isEn ? "Book" : "Knjiga") : n.Book.Title!;
                var username = n.User?.Username ?? (isEn ? "user" : "uporabnik");

                var subject = isEn
                    ? $"Book is available again: {title}"
                    : $"Knjiga je ponovno na voljo: {title}";

                var body = isEn
                    ? $@"
<p>Hello {username},</p>
<p>The book <b>{title}</b> is available for borrowing again.</p>
<p>Sign in to Rudolfovo Library and check availability.</p>
<p style='margin-top:12px;color:#6b7280;font-size:12px;'>This is an automated message. Please do not reply to this email.</p>"
                    : $@"
<p>Pozdravljeni {username},</p>
<p>Knjiga <b>{title}</b> je ponovno na voljo za izposojo.</p>
<p>Prijavite se v Rudolfovo Library in preverite stanje knjige.</p>
<p style='margin-top:12px;color:#6b7280;font-size:12px;'>To je avtomatsko sporočilo, prosimo ne odgovarjajte na to e-pošto.</p>";

                TrySendEmail(to, subject, body);
                n.IsActive = false;
                n.NotifiedAtUtc = DateTime.UtcNow;
            }

            await _ctx.SaveChangesAsync();
        }

        private static string NormalizeLanguage(string? lang)
        {
            return string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "sl";
        }

        private void TrySendEmail(string to, string subject, string body)
        {
            try
            {
                _emailService.SendEmail(to, subject, body);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EMAIL] Borrowing notify failed to {to}: {ex.Message}");
            }
        }

    }
}
