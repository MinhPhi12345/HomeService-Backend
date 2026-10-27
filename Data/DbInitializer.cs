using HomeService.Api.Common;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Data;

/// <summary>
/// Chạy khi Backend khởi động:
/// 1) Tự áp dụng migration (không cần gõ "dotnet ef database update" – kể cả khi chạy bằng Docker).
/// 2) Tạo sẵn tài khoản Admin (SRS 2.4: Admin không tự đăng ký được).
/// 3) (Tùy chọn) Tạo vài danh mục dịch vụ mẫu để test nhanh.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

        // SQL Server trong Docker có thể khởi động chậm hơn API → thử lại vài lần
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (db.Database.IsSqlServer()) await db.Database.MigrateAsync();
                else await db.Database.EnsureCreatedAsync();
                break;
            }
            catch (Exception ex) when (attempt < 10)
            {
                logger.LogWarning("Chưa kết nối được database (lần {Attempt}): {Message}. Thử lại sau 5 giây...", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }

        await SeedAdminAsync(db, config, logger);
        await NormalizeUploadUrlsAsync(db, logger);
        if (config.GetValue("Seed:SampleData", true)) await SeedSampleServicesAsync(db);
    }

    /// <summary>
    /// Chuyển ảnh cũ đã lưu dạng "http://localhost:8080/uploads/…" sang "/uploads/…" (chạy mỗi lần khởi động,
    /// chỉ cập nhật bản ghi còn dạng cũ) – để ảnh tải lên lúc chạy máy cá nhân vẫn hiện sau khi deploy.
    /// </summary>
    private static async Task NormalizeUploadUrlsAsync(AppDbContext db, ILogger logger)
    {
        const string marker = "/uploads/";
        var changed = 0;

        foreach (var s in await db.ServiceCategories.Where(x => x.IconUrl != null && x.IconUrl.Contains(marker)).ToListAsync())
            if (UploadUrl.NeedsNormalize(s.IconUrl)) { s.IconUrl = UploadUrl.Normalize(s.IconUrl); changed++; }

        foreach (var u in await db.Users.Where(x => x.AvatarUrl != null && x.AvatarUrl.Contains(marker)).ToListAsync())
            if (UploadUrl.NeedsNormalize(u.AvatarUrl)) { u.AvatarUrl = UploadUrl.Normalize(u.AvatarUrl); changed++; }

        foreach (var p in await db.TaskerProfiles.Where(x => x.IdCardImageUrl != null && x.IdCardImageUrl.Contains(marker)).ToListAsync())
            if (UploadUrl.NeedsNormalize(p.IdCardImageUrl)) { p.IdCardImageUrl = UploadUrl.Normalize(p.IdCardImageUrl); changed++; }

        foreach (var b in await db.Bookings.Where(x => (x.CheckInPhotoUrl != null && x.CheckInPhotoUrl.Contains(marker))
                     || (x.CheckOutPhotoUrl != null && x.CheckOutPhotoUrl.Contains(marker))).ToListAsync())
        {
            if (UploadUrl.NeedsNormalize(b.CheckInPhotoUrl)) { b.CheckInPhotoUrl = UploadUrl.Normalize(b.CheckInPhotoUrl); changed++; }
            if (UploadUrl.NeedsNormalize(b.CheckOutPhotoUrl)) { b.CheckOutPhotoUrl = UploadUrl.Normalize(b.CheckOutPhotoUrl); changed++; }
        }

        foreach (var d in await db.Disputes.Where(x => x.EvidenceUrls.Contains(marker)
                     || (x.TaskerEvidenceUrls != null && x.TaskerEvidenceUrls.Contains(marker))).ToListAsync())
        {
            var ev = UploadUrl.NormalizeJoined(d.EvidenceUrls) ?? string.Empty;
            var tev = UploadUrl.NormalizeJoined(d.TaskerEvidenceUrls);
            if (ev != d.EvidenceUrls || tev != d.TaskerEvidenceUrls) { d.EvidenceUrls = ev; d.TaskerEvidenceUrls = tev; changed++; }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Đã chuẩn hóa {Count} đường dẫn ảnh sang dạng /uploads/…", changed);
        }
    }

    private static async Task SeedAdminAsync(AppDbContext db, IConfiguration config, ILogger logger)
    {
        var email = (config["Seed:AdminEmail"] ?? "admin@homeservice.vn").Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email)) return;

        db.Users.Add(new User
        {
            FullName = config["Seed:AdminFullName"] ?? "Quản trị viên",
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(config["Seed:AdminPassword"] ?? "Admin@123"),
            Role = UserRole.Admin,
        });
        await db.SaveChangesAsync();
        logger.LogInformation("Đã tạo tài khoản Admin mặc định: {Email}", email);
    }

    private static async Task SeedSampleServicesAsync(AppDbContext db)
    {
        if (await db.ServiceCategories.AnyAsync()) return;

        db.ServiceCategories.AddRange(
            new ServiceCategory { Name = "Dọn dẹp nhà", Description = "Quét dọn, lau nhà, vệ sinh bếp và nhà tắm theo giờ.", BasePrice = 150000, Unit = "giờ" },
            new ServiceCategory { Name = "Giặt ủi", Description = "Giặt, sấy và ủi quần áo tại nhà.", BasePrice = 80000, Unit = "lần" },
            new ServiceCategory { Name = "Sửa điện nước", Description = "Sửa chữa điện, nước, thay thiết bị cơ bản.", BasePrice = 200000, Unit = "lần" },
            new ServiceCategory { Name = "Chăm sóc cây", Description = "Tưới, cắt tỉa, bón phân cây cảnh và vườn nhỏ.", BasePrice = 120000, Unit = "buổi" },
            new ServiceCategory { Name = "Nấu ăn gia đình", Description = "Đi chợ và nấu bữa ăn cho gia đình.", BasePrice = 180000, Unit = "buổi" }
        );
        await db.SaveChangesAsync();
    }
}
