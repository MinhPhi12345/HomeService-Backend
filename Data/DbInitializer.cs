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
        if (config.GetValue("Seed:SampleData", true)) await SeedSampleServicesAsync(db);
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
