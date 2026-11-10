using HomeService.Api.Data;
using HomeService.Api.Enums;
using HomeService.Api.Models;

namespace HomeService.Api.Tests.Infrastructure;

/// <summary>Tạo nhanh dữ liệu mẫu (đã lưu xuống CSDL) cho test.</summary>
public static class Seed
{
    // Toạ độ mẫu ở TP.HCM
    public const double BenThanhLat = 10.7725, BenThanhLng = 106.6980;   // Quận 1
    public const double Q3Lat = 10.7843, Q3Lng = 106.6844;               // cách Bến Thành ~2 km
    public const double ThuDucLat = 10.8494, ThuDucLng = 106.7537;       // cách Bến Thành ~10 km

    private static int _seq;
    private static string NextEmail(string prefix) => $"{prefix}{Interlocked.Increment(ref _seq)}@test.vn";

    public static User Customer(this AppDbContext db, string fullName = "Nguyễn Văn Khách", string? phone = "0901234567",
        Action<User>? configure = null)
    {
        var user = new User
        {
            FullName = fullName,
            Email = NextEmail("khach"),
            PasswordHash = "hash",
            PhoneNumber = phone,
            Role = UserRole.Customer,
        };
        configure?.Invoke(user);
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    public static User Admin(this AppDbContext db, string fullName = "Quản trị viên", bool isActive = true)
    {
        var user = new User { FullName = fullName, Email = NextEmail("admin"), PasswordHash = "hash", Role = UserRole.Admin, IsActive = isActive };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    /// <summary>Tasker đã được duyệt, đang bật nhận đơn, điểm làm việc ở Bến Thành, ví 500.000 đ.</summary>
    public static User Tasker(this AppDbContext db, string fullName = "Trần Thị Tasker", string skills = "Dọn dẹp",
        double lat = BenThanhLat, double lng = BenThanhLng, decimal wallet = 500_000m,
        TaskerVerificationStatus status = TaskerVerificationStatus.Approved, Action<TaskerProfile>? configure = null)
    {
        var user = new User
        {
            FullName = fullName,
            Email = NextEmail("tasker"),
            PasswordHash = "hash",
            PhoneNumber = "0987654321",
            Role = UserRole.Tasker,
            TaskerProfile = new TaskerProfile
            {
                IdCardNumber = "079123456789",
                Skills = skills,
                WorkingArea = "Quận 1",
                VerificationStatus = status,
                BaseAddress = "Chợ Bến Thành",
                BaseLatitude = lat,
                BaseLongitude = lng,
                ServiceRadiusKm = 8,
                WalletBalance = wallet,
                IsAvailable = true,
            },
        };
        configure?.Invoke(user.TaskerProfile);
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    public static ServiceCategory Service(this AppDbContext db, string name = "Dọn dẹp nhà", decimal price = 150_000m,
        bool isActive = true, string unit = "giờ", Action<ServiceCategory>? configure = null)
    {
        var s = new ServiceCategory { Name = name, BasePrice = price, Unit = unit, IsActive = isActive, Description = "Mô tả" };
        configure?.Invoke(s);
        db.ServiceCategories.Add(s);
        db.SaveChanges();
        return s;
    }

    /// <summary>Đơn đặt dịch vụ; mặc định online (ví điện tử), hẹn sau 3 giờ, ở Bến Thành.</summary>
    public static Booking Booking(this AppDbContext db, User customer, ServiceCategory service,
        BookingStatus status = BookingStatus.Pending, User? tasker = null, PaymentMethod method = PaymentMethod.EWallet,
        Action<Booking>? configure = null)
    {
        var b = new Booking
        {
            CustomerId = customer.Id,
            ServiceCategoryId = service.Id,
            TaskerId = tasker?.Id,
            ScheduledTime = DateTime.UtcNow.AddHours(3),
            Address = "1 Lê Lợi, Quận 1",
            Latitude = BenThanhLat,
            Longitude = BenThanhLng,
            Quantity = 1,
            TotalPrice = service.BasePrice,
            Status = status,
            PaymentMethod = method,
            CommissionRate = 0.2m,
            CustomerPresent = method == PaymentMethod.Cash,
            CreatedAt = DateTime.UtcNow,
        };
        configure?.Invoke(b);
        db.Bookings.Add(b);
        db.SaveChanges();
        return b;
    }

    /// <summary>Khoản thanh toán online đang được hệ thống tạm giữ.</summary>
    public static Payment HeldPayment(this AppDbContext db, Booking booking, decimal? amount = null)
    {
        var p = new Payment
        {
            BookingId = booking.Id,
            Amount = amount ?? booking.TotalPrice,
            Method = booking.PaymentMethod == PaymentMethod.Cash ? PaymentMethod.EWallet : booking.PaymentMethod,
            Status = PaymentStatus.Held,
            PaidAt = DateTime.UtcNow,
        };
        db.Payments.Add(p);
        db.SaveChanges();
        return p;
    }

    public static BookingOffer Offer(this AppDbContext db, Booking booking, User tasker,
        OfferStatus status = OfferStatus.Sent, int wave = 1, DateTime? expiresAt = null)
    {
        var o = new BookingOffer
        {
            BookingId = booking.Id,
            TaskerId = tasker.Id,
            Round = booking.MatchRound,
            Wave = wave,
            DistanceKm = 1.5m,
            Score = 0.8m,
            Status = status,
            SentAt = DateTime.UtcNow,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddSeconds(45),
        };
        db.BookingOffers.Add(o);
        db.SaveChanges();
        return o;
    }

    public static Review Review(this AppDbContext db, Booking booking, User reviewer, User reviewee, int rating,
        bool flagged = false, string? comment = "Tốt")
    {
        var r = new Review
        {
            BookingId = booking.Id, ReviewerId = reviewer.Id, RevieweeId = reviewee.Id,
            Rating = rating, IsFlagged = flagged, Comment = comment,
        };
        db.Reviews.Add(r);
        db.SaveChanges();
        return r;
    }

    public static Dispute Dispute(this AppDbContext db, Booking booking, Action<Dispute>? configure = null)
    {
        var d = new Dispute
        {
            BookingId = booking.Id,
            CustomerId = booking.CustomerId,
            TaskerId = booking.TaskerId!.Value,
            Reason = DisputeReason.PoorQuality,
            Description = "Làm chưa sạch phòng khách",
            EvidenceUrls = "/uploads/202610/a.png",
            CreatedAt = DateTime.UtcNow,
        };
        configure?.Invoke(d);
        db.Disputes.Add(d);
        db.SaveChanges();
        return d;
    }
}
