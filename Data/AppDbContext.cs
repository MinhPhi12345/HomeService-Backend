using HomeService.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HomeService.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<TaskerProfile> TaskerProfiles => Set<TaskerProfile>();
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<BookingOffer> BookingOffers => Set<BookingOffer>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Dispute> Disputes => Set<Dispute>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SRS 8.5: BE lưu và trả thời gian theo UTC (JSON có hậu tố Z).
        // SQL Server không lưu "Kind" của DateTime nên phải gắn lại Kind = Utc khi đọc ra.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();

        // Tiền tệ: decimal(18,2) mặc định (NFR-14)
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        // Chỉ dùng khi nhóm chạy thử bằng SQLite (SQLite không hỗ trợ so sánh/tính tổng decimal).
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            configurationBuilder.Properties<decimal>().HaveConversion<double>();
            configurationBuilder.Properties<decimal?>().HaveConversion<double?>();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.FullName).HasMaxLength(100);
            e.Property(u => u.Email).HasMaxLength(256);
            e.Property(u => u.PasswordHash).HasMaxLength(256);
            e.Property(u => u.PhoneNumber).HasMaxLength(15).IsUnicode(false);
            e.Property(u => u.Address).HasMaxLength(255);
            e.Property(u => u.AvatarUrl).HasMaxLength(500);
        });

        modelBuilder.Entity<User>()
            .HasOne(u => u.TaskerProfile)
            .WithOne(t => t.User)
            .HasForeignKey<TaskerProfile>(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TaskerProfile>(e =>
        {
            e.Property(t => t.IdCardNumber).HasMaxLength(12).IsUnicode(false);
            e.Property(t => t.IdCardImageUrl).HasMaxLength(500);
            e.Property(t => t.Skills).HasMaxLength(500);
            e.Property(t => t.WorkingArea).HasMaxLength(100);
            e.Property(t => t.RatingAverage).HasPrecision(3, 2);
            e.Property(t => t.BaseAddress).HasMaxLength(255);
            e.Ignore(t => t.AvailableBalance);
            // Chống ghi đè số dư khi 2 thao tác ví chạy cùng lúc (NFR-14)
            e.Property(t => t.WalletBalance).IsConcurrencyToken();
            e.Property(t => t.ReservedAmount).IsConcurrencyToken();
        });

        modelBuilder.Entity<ServiceCategory>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.Description).HasMaxLength(1000);
            e.Property(s => s.Unit).HasMaxLength(20);
            e.Property(s => s.IconUrl).HasMaxLength(500);
        });

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.Customer)
            .WithMany()
            .HasForeignKey(b => b.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.Tasker)
            .WithMany()
            .HasForeignKey(b => b.TaskerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.ServiceCategory)
            .WithMany()
            .HasForeignKey(b => b.ServiceCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Booking>(e =>
        {
            e.Property(b => b.Address).HasMaxLength(255);
            e.Property(b => b.Note).HasMaxLength(500);
            e.Property(b => b.CommissionRate).HasPrecision(5, 4);
            e.HasIndex(b => new { b.Status, b.NextMatchAt });
            e.HasIndex(b => new { b.TaskerId, b.Status });
            e.Property(b => b.CheckInPhotoUrl).HasMaxLength(500);
            e.Property(b => b.CheckOutPhotoUrl).HasMaxLength(500);
            // Chống 2 thao tác đổi trạng thái đơn cùng lúc (vd: khách hủy đúng lúc Tasker bắt đầu)
            e.Property(b => b.Status).IsConcurrencyToken();
        });

        // US16 – lời mời nhận đơn
        modelBuilder.Entity<BookingOffer>(e =>
        {
            e.HasOne(o => o.Booking).WithMany(b => b.Offers).HasForeignKey(o => o.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(o => o.Tasker).WithMany().HasForeignKey(o => o.TaskerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(o => new { o.BookingId, o.TaskerId, o.Round }).IsUnique(); // không gửi lại cùng Tasker trong 1 vòng
            e.HasIndex(o => new { o.TaskerId, o.Status });
            e.Property(o => o.DistanceKm).HasPrecision(6, 2);
            e.Property(o => o.Score).HasPrecision(5, 4);
        });

        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Booking)
            .WithOne(b => b.Payment)
            .HasForeignKey<Payment>(p => p.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Payment>(e =>
        {
            e.Property(p => p.GatewayTransactionId).HasMaxLength(64).IsUnicode(false);
            e.Property(p => p.Status).IsConcurrencyToken(); // NFR-17: không giải ngân/hoàn tiền 2 lần
        });

        modelBuilder.Entity<WalletTransaction>(e =>
        {
            e.HasOne(w => w.Tasker).WithMany().HasForeignKey(w => w.TaskerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(w => w.Booking).WithMany().HasForeignKey(w => w.BookingId).OnDelete(DeleteBehavior.SetNull);
            e.Property(w => w.Note).HasMaxLength(255);
            e.HasIndex(w => new { w.TaskerId, w.CreatedAt });
        });

        // US35 – khiếu nại
        modelBuilder.Entity<Dispute>(e =>
        {
            e.HasOne(d => d.Booking).WithOne(b => b.Dispute).HasForeignKey<Dispute>(d => d.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Customer).WithMany().HasForeignKey(d => d.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.Tasker).WithMany().HasForeignKey(d => d.TaskerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.ResolvedBy).WithMany().HasForeignKey(d => d.ResolvedById).OnDelete(DeleteBehavior.Restrict);
            e.Property(d => d.Description).HasMaxLength(1000);
            e.Property(d => d.EvidenceUrls).HasMaxLength(2600);
            e.Property(d => d.TaskerResponse).HasMaxLength(1000);
            e.Property(d => d.TaskerEvidenceUrls).HasMaxLength(2600);
            e.Property(d => d.AdminNote).HasMaxLength(1000);
            e.Property(d => d.Status).IsConcurrencyToken(); // không xử lý 1 khiếu nại 2 lần
            e.HasIndex(d => d.Status);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.Property(a => a.Action).HasMaxLength(50).IsUnicode(false);
            e.Property(a => a.Detail).HasMaxLength(500);
            e.HasIndex(a => a.BookingId);
            e.HasIndex(a => a.CreatedAt);
        });

        modelBuilder.Entity<Review>()
            .HasOne(r => r.Booking)
            .WithMany(b => b.Reviews)
            .HasForeignKey(r => r.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Review>()
            .HasOne(r => r.Reviewer)
            .WithMany()
            .HasForeignKey(r => r.ReviewerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Review>()
            .HasOne(r => r.Reviewee)
            .WithMany()
            .HasForeignKey(r => r.RevieweeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Review>()
            .Property(r => r.Comment).HasMaxLength(500);
        modelBuilder.Entity<Review>()
            .HasIndex(r => new { r.BookingId, r.ReviewerId }).IsUnique(); // BR-R01: mỗi người 1 đánh giá / đơn
        modelBuilder.Entity<Review>()
            .ToTable(t => t.HasCheckConstraint("CK_Reviews_Rating", "[Rating] BETWEEN 1 AND 5"));

        modelBuilder.Entity<Notification>(e =>
        {
            e.Property(n => n.Title).HasMaxLength(150);
            e.Property(n => n.Message).HasMaxLength(500);
            e.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(n => new { n.UserId, n.IsRead });
        });
    }
}

/// <summary>Lưu DateTime dạng UTC; khi đọc ra gắn Kind = Utc để JSON có hậu tố "Z".</summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
               v => DateTime.SpecifyKind(v, DateTimeKind.Utc)) { }
}

public sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter()
        : base(v => v.HasValue && v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : v,
               v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v) { }
}
