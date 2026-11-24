
using BookManagerApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BookManagerApi.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Book> Books => Set<Book>();
        public DbSet<Borrowing> Borrowings => Set<Borrowing>();
        public DbSet<WishlistRequest> WishlistRequests => Set<WishlistRequest>();
        public DbSet<WishlistPriceReview> WishlistPriceReviews => Set<WishlistPriceReview>();
        public DbSet<BookAvailabilityNotification> BookAvailabilityNotifications => Set<BookAvailabilityNotification>();
        public DbSet<BookInventory> BookCopies => Set<BookInventory>();
        public DbSet<Center> Center => Set<Center>();
        public DbSet<Category> Categories => Set<Category>();

        public DbSet<AuditLog> AuditLogs { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<WishlistRequest>()
                .Property(w => w.Status)
                .HasConversion<int>();

            modelBuilder.Entity<WishlistRequest>()
                .HasOne(w => w.Center)
                .WithMany()
                .HasForeignKey(w => w.CenterId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<WishlistRequest>()
                .HasIndex(w => w.CenterId);

            modelBuilder.Entity<WishlistPriceReview>()
                .HasIndex(r => new { r.WishlistRequestId, r.Year })
                .IsUnique();

            modelBuilder.Entity<WishlistPriceReview>()
                .HasOne(r => r.WishlistRequest)
                .WithMany()
                .HasForeignKey(r => r.WishlistRequestId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<WishlistPriceReview>()
                .HasOne(r => r.ReviewedByUser)
                .WithMany()
                .HasForeignKey(r => r.ReviewedByUserId)
                .OnDelete(DeleteBehavior.SetNull);


            modelBuilder.Entity<Book>(b =>
            {
                b.Property(x => x.Genre)
                    .HasColumnType("text[]")
                    .HasDefaultValueSql("ARRAY[]::text[]");

                b.HasIndex(x => x.ISBN).IsUnique(false);
                b.HasIndex(x => x.ISSN).IsUnique(false);
            });

            modelBuilder.Entity<BookInventory>()
                    .HasOne(bi => bi.Book)
                    .WithMany(b => b.Copies)
                    .HasForeignKey(b => b.BookId);

            modelBuilder.Entity<BookInventory>()
                .HasOne(bi => bi.Center)
                .WithMany(c => c.BookCopies)
                .HasForeignKey(bi => bi.CenterId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<BookInventory>()
                .HasIndex(bi => bi.InventoryNumber)
                .IsUnique();

            modelBuilder.Entity<BookInventory>()
                .HasIndex(bi => bi.CenterId);

            modelBuilder.Entity<BookInventory>()
                .Property(bi => bi.CreatedAt)
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("(NOW() AT TIME ZONE 'UTC')");


            modelBuilder.Entity<Borrowing>()
                .HasOne(b => b.BookInventory)
                .WithMany(bi => bi.Borrowings)
                .HasForeignKey(b => b.BookInventoryId);

            modelBuilder.Entity<BookAvailabilityNotification>()
                .HasIndex(n => new { n.UserId, n.BookId, n.IsActive });

            modelBuilder.Entity<BookAvailabilityNotification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<BookAvailabilityNotification>()
                .HasOne(n => n.Book)
                .WithMany()
                .HasForeignKey(n => n.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.ToTable("audit_logs");

                entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
                entity.Property(x => x.EntityName).HasColumnName("entity_name");
                entity.Property(x => x.ControllerName).HasColumnName("controller_name");
                entity.Property(x => x.ActionName).HasColumnName("action_name");
                entity.Property(x => x.RequestPath).HasColumnName("request_path");
                entity.Property(x => x.StatusCode).HasColumnName("status_code");
                entity.Property(x => x.Succeeded).HasColumnName("succeeded");
                entity.Property(x => x.UserId).HasColumnName("user_id");
                entity.Property(x => x.UserAgent).HasColumnName("user_agent");
                entity.Property(x => x.DetailsJson).HasColumnName("details_json");
            });
        }
    }

}
