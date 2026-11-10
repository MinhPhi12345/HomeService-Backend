using HomeService.Api.Common;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Data;

/// <summary>
/// Dữ liệu mẫu để demo hệ thống (bật bằng cấu hình <c>Seed:DemoData = true</c>, mật khẩu chung <c>Seed:DemoPassword</c>).
/// Chạy MỘT lần: nếu database đã có tài khoản đuôi <see cref="Domain"/> thì bỏ qua – không bao giờ đụng dữ liệu thật.
/// Tạo: 6 khách hàng, 6 Tasker (4 đã duyệt, 2 chờ duyệt), ~75 ngày lịch sử đơn (đủ trạng thái), thanh toán, ví, đánh giá,
/// khiếu nại, mã giảm giá, quà tặng, thông báo, nhật ký kiểm toán. Dữ liệu tháng trước đủ để hệ thống tự chốt
/// bảng xếp hạng Tasker xuất sắc (FR-31). Mọi số liệu tiền khớp các bất biến của SettlementService (BR-P07, BR-V03).
/// </summary>
public static class DemoDataSeeder
{
    public const string Domain = "@demo.vn";
    public const string DefaultPassword = "Demo@1234";
    private static readonly string[] AssetNames = ["checkin.jpg", "checkout.jpg", "evidence1.jpg", "evidence2.jpg", "cccd.jpg"];

    private sealed record Person(string Name, string Email, string Phone, string Address, double Lat, double Lng);
    private sealed record WalletEvent(int TaskerId, DateTime At, WalletTransactionType Type, decimal Amount, Booking? Booking, string Note);
    private sealed record AuditEvent(string Action, Booking? Booking, Payment? Payment, int? Actor, decimal Amount, string Detail, DateTime At);

    public static async Task<bool> SeedAsync(AppDbContext db, IConfiguration config, string? uploadsPath, ILogger logger, DateTime? nowUtc = null)
    {
        if (await db.Users.AnyAsync(u => u.Email.EndsWith(Domain)))
        {
            logger.LogInformation("Dữ liệu demo đã có (tài khoản {Domain}) – bỏ qua.", Domain);
            return false;
        }
        var admin = await db.Users.Where(u => u.Role == UserRole.Admin).OrderBy(u => u.Id).FirstOrDefaultAsync();
        if (admin is null) return false;

        var s = new Session(db, config, uploadsPath, nowUtc ?? DateTime.UtcNow, admin.Id);
        await s.RunAsync();
        logger.LogInformation("Đã tạo dữ liệu demo: {Users} tài khoản, {Bookings} đơn. Mật khẩu chung: cấu hình Seed:DemoPassword.",
            s.UserCount, s.BookingCount);
        return true;
    }

    private sealed class Session(AppDbContext db, IConfiguration config, string? uploadsPath, DateTime now, int adminId)
    {
        private readonly Random _rnd = new(15);
        private readonly decimal _rate = config.GetValue("Business:CommissionRate", 0.2m);
        private readonly List<WalletEvent> _wallet = [];
        private readonly List<AuditEvent> _audit = [];
        private readonly List<Booking> _bookings = [];
        private readonly Dictionary<string, string?> _img = [];
        private Dictionary<string, ServiceCategory> _svc = [];
        private readonly Dictionary<string, User> _c = [];
        private readonly Dictionary<string, User> _t = [];
        public int UserCount => _c.Count + _t.Count;
        public int BookingCount => _bookings.Count;

        /// <summary>Giờ VN (ngày lệch so với hôm nay theo giờ VN) → UTC.</summary>
        private DateTime Vn(int dayOffset, int hour, int minute = 0) =>
            now.AddHours(7).Date.AddDays(dayOffset).AddHours(hour).AddMinutes(minute).AddHours(-7);
        private T Pick<T>(IReadOnlyList<T> xs) => xs[_rnd.Next(xs.Count)];

        public async Task RunAsync()
        {
            CopyAssets();
            await LoadServicesAsync();
            await CreateUsersAsync();
            var vouchers = CreateVouchers();
            CreateHistory(vouchers);
            CreateLiveOrders(vouchers);
            await db.SaveChangesAsync();          // có Id đơn / thanh toán cho nhật ký, giao dịch ví

            ApplyWallets();
            UpdateRatings();
            foreach (var a in _audit)
                db.AuditLogs.Add(new AuditLog { Action = a.Action, BookingId = a.Booking?.Id, PaymentId = a.Payment?.Id, ActorUserId = a.Actor,
                    Amount = a.Amount, Detail = a.Detail, CreatedAt = a.At });
            CreateNotifications();
            await db.SaveChangesAsync();
        }

        // ---------- ảnh minh họa (check-in/out, khiếu nại, CCCD) ----------
        private void CopyAssets()
        {
            var asm = typeof(DemoDataSeeder).Assembly;
            foreach (var name in AssetNames)
            {
                _img[name] = null;
                if (string.IsNullOrEmpty(uploadsPath)) continue;
                var res = asm.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith("DemoAssets." + name, StringComparison.OrdinalIgnoreCase));
                if (res is null) continue;
                try
                {
                    var dir = Path.Combine(uploadsPath, "demo");
                    Directory.CreateDirectory(dir);
                    using var src = asm.GetManifestResourceStream(res)!;
                    using var dst = File.Create(Path.Combine(dir, name));
                    src.CopyTo(dst);
                    _img[name] = "/uploads/demo/" + name;
                }
                catch (IOException) { /* không ghi được ảnh → để trống, dữ liệu vẫn tạo */ }
                catch (UnauthorizedAccessException) { }
            }
        }

        // ---------- dịch vụ: dùng danh mục sẵn có, thiếu thì tạo 5 dịch vụ mặc định ----------
        private async Task LoadServicesAsync()
        {
            var cats = await db.ServiceCategories.Where(c => c.IsActive).OrderBy(c => c.Id).ToListAsync();
            if (cats.Count == 0)
            {
                cats = ServiceCatalog.Defaults();
                db.ServiceCategories.AddRange(cats);
                await db.SaveChangesAsync();
            }
            ServiceCategory Find(string key, int fallback) =>
                cats.FirstOrDefault(c => c.Name.Contains(key, StringComparison.OrdinalIgnoreCase)) ?? cats[fallback % cats.Count];
            _svc = new()
            {
                ["clean"] = Find("Dọn", 0), ["laundry"] = Find("Giặt", 1), ["repair"] = Find("điện", 2),
                ["plant"] = Find("cây", 3), ["cook"] = Find("Nấu", 4),
            };
        }

        private string Skills(params string[] keys) => string.Join(", ", keys.Select(k => _svc[k].Name).Distinct());

        // ---------- tài khoản ----------
        private async Task CreateUsersAsync()
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(config["Seed:DemoPassword"] ?? DefaultPassword);
            Person[] customers =
            [
                new("Nguyễn Minh Anh", "minhanh" + Domain, "0903123456", "12 Nguyễn Huệ, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh", 10.7743, 106.7038),
                new("Trần Bảo Ngọc", "baongoc" + Domain, "0912345678", "78 Nam Kỳ Khởi Nghĩa, Phường Võ Thị Sáu, Quận 3, TP. Hồ Chí Minh", 10.7785, 106.6919),
                new("Lê Quốc Huy", "quochuy" + Domain, "0934567890", "23 Phan Đăng Lưu, Phường 3, Bình Thạnh, TP. Hồ Chí Minh", 10.8030, 106.6955),
                new("Phạm Thu Trang", "thutrang" + Domain, "0987123456", "156 Hoàng Văn Thụ, Phường 9, Phú Nhuận, TP. Hồ Chí Minh", 10.7990, 106.6760),
                new("Võ Hoàng Long", "hoanglong" + Domain, "0978654321", "301 Nguyễn Thị Thập, Phường Tân Phú, Quận 7, TP. Hồ Chí Minh", 10.7380, 106.7180),
                new("Đỗ Khánh Linh", "khanhlinh" + Domain, "0965432109", "45 Lê Thánh Tôn, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh", 10.7765, 106.7030),
            ];
            var i = 0;
            foreach (var p in customers)
            {
                var u = new User { FullName = p.Name, Email = p.Email, PasswordHash = hash, PhoneNumber = p.Phone, Address = p.Address,
                    Role = UserRole.Customer, EmailVerified = true, CreatedAt = now.AddDays(-80).AddHours(i++ * 5) };
                db.Users.Add(u); _c[p.Name] = u;
            }

            // (tên, email, sđt, CCCD, kỹ năng, địa chỉ, lat, lng, bán kính, trạng thái, điểm vi phạm)
            var taskers = new (string Name, string Email, string Phone, string Cccd, string Skills, string Addr, double Lat, double Lng, int Radius,
                TaskerVerificationStatus Status, int Violations)[]
            {
                ("Trần Thị Lan", "lan" + Domain, "0901234567", "079287858845", Skills("clean", "laundry", "cook"),
                    "45 Lý Tự Trọng, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh", 10.7769, 106.7009, 5, TaskerVerificationStatus.Approved, 0),
                ("Lê Văn Hùng", "hung" + Domain, "0902345678", "079285173353", Skills("repair", "clean"),
                    "120 Võ Văn Tần, Phường Võ Thị Sáu, Quận 3, TP. Hồ Chí Minh", 10.7766, 106.6886, 8, TaskerVerificationStatus.Approved, 0),
                ("Phạm Minh Tuấn", "tuan" + Domain, "0903456789", "079172065329", Skills("cook", "plant"),
                    "9 Điện Biên Phủ, Phường 15, Bình Thạnh, TP. Hồ Chí Minh", 10.8008, 106.7110, 8, TaskerVerificationStatus.Approved, 1),
                ("Ngô Thị Mai", "mai" + Domain, "0904567890", "079122505518", Skills("clean", "laundry"),
                    "210 Phan Xích Long, Phường 2, Phú Nhuận, TP. Hồ Chí Minh", 10.7997, 106.6870, 5, TaskerVerificationStatus.Approved, 0),
                ("Võ Văn Nam", "nam" + Domain, "0905678901", "079205676377", Skills("repair"),
                    "88 Quang Trung, Phường 10, Gò Vấp, TP. Hồ Chí Minh", 10.8380, 106.6650, 8, TaskerVerificationStatus.Pending, 0),
                ("Đỗ Thị Hạnh", "hanh" + Domain, "0906789012", "079199887766", Skills("clean", "plant"),
                    "15 Cộng Hòa, Phường 4, Tân Bình, TP. Hồ Chí Minh", 10.8015, 106.6520, 5, TaskerVerificationStatus.Pending, 0),
            };
            i = 0;
            foreach (var t in taskers)
            {
                var approved = t.Status == TaskerVerificationStatus.Approved;
                var u = new User { FullName = t.Name, Email = t.Email, PasswordHash = hash, PhoneNumber = t.Phone, Address = t.Addr,
                    Role = UserRole.Tasker, EmailVerified = true, CreatedAt = approved ? now.AddDays(-85).AddHours(i * 3) : now.AddDays(-3).AddHours(i) };
                u.TaskerProfile = new TaskerProfile
                {
                    IdCardNumber = t.Cccd, IdCardImageUrl = _img["cccd.jpg"], Skills = t.Skills, BaseAddress = t.Addr,
                    WorkingArea = WorkingAreas.FromAddress(t.Addr), BaseLatitude = t.Lat, BaseLongitude = t.Lng, ServiceRadiusKm = t.Radius,
                    VerificationStatus = t.Status, IsAvailable = approved, ViolationPoints = t.Violations, CreatedAt = u.CreatedAt,
                    SubmittedAt = approved ? u.CreatedAt.AddHours(2) : now.AddHours(-30 + i * 6),
                };
                db.Users.Add(u); _t[t.Name] = u; i++;
            }
            await db.SaveChangesAsync();
            foreach (var t in _t.Values.Where(t => t.TaskerProfile!.VerificationStatus == TaskerVerificationStatus.Approved))
                _wallet.Add(new(t.Id, t.CreatedAt.AddDays(1), WalletTransactionType.TopUp, 500_000m, null, "Nạp tiền ký quỹ vào ví (mô phỏng)"));
        }

        // ---------- mã giảm giá & quà tặng ----------
        private Dictionary<string, Voucher> CreateVouchers()
        {
            var monthStart = Vn(0, 0).AddDays(-(now.AddHours(7).Day - 1));            // đầu tháng này (giờ VN → UTC)
            var prevStart = monthStart.AddHours(7).AddMonths(-1).AddHours(-7);
            var nextStart = monthStart.AddHours(7).AddMonths(1).AddHours(-7);
            string Mm(DateTime utc) => utc.AddHours(7).ToString("MM");
            var v = new Dictionary<string, Voucher>
            {
                ["prev"] = new() { Code = "THANG" + Mm(prevStart), Name = $"Ưu đãi tháng {Mm(prevStart)}", Description = "Giảm 10% tối đa 40.000 đ cho mọi dịch vụ.",
                    DiscountType = VoucherDiscountType.Percent, DiscountValue = 10, MaxDiscount = 40_000, MinOrderValue = 150_000,
                    StartsAt = prevStart, EndsAt = monthStart.AddMinutes(-1), TotalLimit = 300, PerCustomerLimit = 1, CreatedById = adminId, CreatedAt = prevStart.AddDays(-2) },
                ["month"] = new() { Code = "THANG" + Mm(monthStart), Name = $"Ưu đãi tháng {Mm(monthStart)}", Description = "Giảm 15% tối đa 60.000 đ cho mọi dịch vụ.",
                    DiscountType = VoucherDiscountType.Percent, DiscountValue = 15, MaxDiscount = 60_000, MinOrderValue = 150_000,
                    StartsAt = monthStart, EndsAt = nextStart.AddMinutes(-1), TotalLimit = 500, PerCustomerLimit = 1, CreatedById = adminId, CreatedAt = monthStart.AddDays(-1) },
                ["clean"] = new() { Code = "DONNHA30K", Name = "Giảm 30K dọn nhà", Description = $"Giảm 30.000 đ cho đơn {_svc["clean"].Name} từ 300.000 đ.",
                    DiscountType = VoucherDiscountType.FixedAmount, DiscountValue = 30_000, MinOrderValue = 300_000, ServiceCategoryIds = _svc["clean"].Id.ToString(),
                    StartsAt = monthStart, EndsAt = nextStart.AddMonths(1), TotalLimit = 200, PerCustomerLimit = 2, CreatedById = adminId, CreatedAt = monthStart },
                ["new"] = new() { Code = "KHACHMOI", Name = "Chào khách mới", Description = "Giảm 25.000 đ cho đơn đầu tiên của khách hàng mới.",
                    DiscountType = VoucherDiscountType.FixedAmount, DiscountValue = 25_000, MinOrderValue = 100_000, Audience = VoucherAudience.NewCustomers,
                    StartsAt = monthStart, EndsAt = nextStart.AddMonths(2), PerCustomerLimit = 1, CreatedById = adminId, CreatedAt = monthStart },
                ["vip"] = new() { Code = "VIP50K", Name = "Mã ẩn tri ân", Description = "Mã ẩn gửi riêng cho khách thân thiết – giảm 50.000 đ.",
                    DiscountType = VoucherDiscountType.FixedAmount, DiscountValue = 50_000, MinOrderValue = 250_000, IsPublic = false,
                    StartsAt = monthStart, EndsAt = nextStart, TotalLimit = 50, PerCustomerLimit = 1, CreatedById = adminId, CreatedAt = monthStart },
            };
            foreach (var x in v.Values)
            {
                db.Vouchers.Add(x);
                _audit.Add(new("VoucherCreated", null, null, adminId, 0, $"Tạo chương trình {x.Code} – {x.Name}", x.CreatedAt));
            }
            return v;
        }

        private Voucher Gift(User customer, VoucherSource source, string name, VoucherDiscountType type, decimal value, decimal? max, decimal minOrder, DateTime at)
        {
            var g = new Voucher
            {
                Code = "QUA" + new string(Enumerable.Range(0, 6).Select(_ => "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"[_rnd.Next(32)]).ToArray()),
                Name = name, Description = "Quà tặng tự động từ HomeService.", DiscountType = type, DiscountValue = value, MaxDiscount = max,
                MinOrderValue = minOrder, StartsAt = at, EndsAt = at.AddDays(30), TotalLimit = 1, PerCustomerLimit = 1, IsPublic = false,
                AssignedCustomer = customer, Source = source, CreatedAt = at,
            };
            db.Vouchers.Add(g);
            return g;
        }

        private static readonly string[] SampleRepairItems =
        {
            "Vòi nước bồn rửa bếp bị rỉ", "Ổ cắm phòng khách không có điện", "Thay bóng đèn nhà tắm",
            "Bồn cầu xả nước yếu", "Công tắc đèn phòng ngủ bị lỏng",
        };

        // ---------- một đơn hoàn chỉnh (thanh toán, lời mời, ví, nhật ký) ----------
        private Booking NewBooking(User customer, string svcKey, BookingStatus status, User? tasker, DateTime sched, PaymentMethod method,
            int? qty = null, DateTime? created = null, string? note = null)
        {
            var svc = _svc[svcKey];
            var q = qty ?? (svc.Unit == "giờ" ? _rnd.Next(2, 5) : 1);
            var b = new Booking
            {
                Customer = customer, ServiceCategory = svc, Tasker = tasker, ScheduledTime = sched, Address = customer.Address ?? "TP. Hồ Chí Minh",
                Latitude = 10.7769 + (_rnd.NextDouble() - 0.5) * 0.06, Longitude = 106.7009 + (_rnd.NextDouble() - 0.5) * 0.06,
                Note = note, ContactPhone = customer.PhoneNumber, Quantity = q, TotalPrice = svc.BasePrice * q, Status = status,
                CreatedAt = created ?? sched.AddDays(-1).AddHours(-_rnd.Next(1, 30)), PaymentMethod = method, CommissionRate = _rate,
                CustomerPresent = method == PaymentMethod.Cash, MatchRound = 1, CurrentWave = tasker is null ? 0 : 1,
                EstimatedMinutes = (svc.MinutesPerUnit > 0 ? svc.MinutesPerUnit : 120) * q,
            };
            // SRS v1.8: dịch vụ tính theo hạng mục → đơn mẫu có danh sách hạng mục
            if (svc.RequiresItemList)
                for (var i = 0; i < q; i++)
                    b.Items.Add(new BookingItem
                    {
                        ItemNo = i + 1,
                        // Đơn 1 hạng mục có ghi chú → dùng luôn ghi chú làm mô tả hạng mục cho khớp
                        Description = q == 1 && !string.IsNullOrWhiteSpace(note) && note.Length <= 200
                            ? note
                            : SampleRepairItems[(_bookings.Count + i) % SampleRepairItems.Length],
                    });
            if (method != PaymentMethod.Cash) b.PaymentDueAt = b.CreatedAt.AddMinutes(15);
            b.UpdatedAt = b.CreatedAt;
            if (tasker is not null)
                b.Offers.Add(new BookingOffer { Tasker = tasker, Round = 1, Wave = 1, DistanceKm = Math.Round((decimal)(0.6 + _rnd.NextDouble() * 4), 2),
                    Score = Math.Round((decimal)(0.65 + _rnd.NextDouble() * 0.3), 4), Status = OfferStatus.Accepted, SentAt = b.CreatedAt.AddMinutes(16),
                    ExpiresAt = b.CreatedAt.AddMinutes(16).AddSeconds(45), RespondedAt = b.CreatedAt.AddMinutes(16).AddSeconds(_rnd.Next(5, 40)) });
            db.Bookings.Add(b); _bookings.Add(b);
            return b;
        }

        private void ApplyVoucher(Booking b, Voucher v)
        {
            if (b.TotalPrice < v.MinOrderValue) return;
            var d = v.DiscountType == VoucherDiscountType.Percent
                ? Math.Min(Math.Round(b.TotalPrice * v.DiscountValue / 100m, 0, MidpointRounding.AwayFromZero), v.MaxDiscount ?? decimal.MaxValue)
                : v.DiscountValue;
            d = Math.Min(d, b.TotalPrice - 10_000m);
            b.Voucher = v; b.VoucherCode = v.Code; b.DiscountAmount = d; v.UsedCount++;
            db.VoucherRedemptions.Add(new VoucherRedemption { Voucher = v, CustomerId = b.Customer!.Id, Booking = b, DiscountAmount = d,
                Status = VoucherRedemptionStatus.Used, CreatedAt = b.CreatedAt, UsedAt = b.CompletedAt ?? b.CreatedAt });
        }

        /// <summary>Đơn đã hoàn tất (5 Paid): ảnh, giờ bắt đầu, thanh toán + giải ngân / thu tiền mặt, ví, nhật ký.</summary>
        private Booking PaidBooking(User customer, User tasker, string svcKey, DateTime sched, PaymentMethod method, Voucher? voucher = null, int? qty = null)
        {
            var b = NewBooking(customer, svcKey, BookingStatus.Paid, tasker, sched, method, qty);
            b.StartedAt = sched.AddMinutes(_rnd.Next(-5, 12) + (_rnd.Next(10) == 0 ? 18 : 0));   // ~10% đến trễ quá 15 phút
            b.CompletedAt = b.StartedAt.Value.AddHours(b.Quantity > 1 && _svc[svcKey].Unit == "giờ" ? b.Quantity : 2).AddMinutes(_rnd.Next(0, 20));
            b.AutoConfirmAt = b.CompletedAt.Value.AddHours(24);
            b.CheckInPhotoUrl = _img["checkin.jpg"]; b.CheckOutPhotoUrl = _img["checkout.jpg"];
            if (voucher is not null && method != PaymentMethod.Cash) ApplyVoucher(b, voucher);
            var payable = b.TotalPrice - b.DiscountAmount;

            if (method == PaymentMethod.Cash)
            {
                var comm = Math.Round(b.TotalPrice * _rate, 0, MidpointRounding.AwayFromZero);
                var p = new Payment { Booking = b, Amount = b.TotalPrice, CommissionAmount = comm, Method = method, Status = PaymentStatus.Success,
                    PaidAt = b.CompletedAt, CreatedAt = b.CompletedAt.Value };
                db.Payments.Add(p);
                b.UpdatedAt = b.CompletedAt;
                _wallet.Add(new(tasker.Id, b.CompletedAt.Value.AddMinutes(5), WalletTransactionType.Commission, -comm, b, $"Chiết khấu đơn tiền mặt #{{id}}"));
                _audit.Add(new("CashCommission", b, p, null, comm, $"Đơn tiền mặt {b.TotalPrice:0}: trừ chiết khấu {comm:0} từ ví Tasker #{tasker.Id}", b.CompletedAt.Value.AddMinutes(5)));
            }
            else
            {
                var autoConfirm = _rnd.Next(4) == 0;
                b.IsAutoConfirmed = autoConfirm;
                var released = autoConfirm ? b.AutoConfirmAt!.Value : b.CompletedAt.Value.AddHours(_rnd.Next(1, 6));
                var basis = payable + b.DiscountAmount;              // = giá gốc (BR-V03)
                var comm = Math.Round(basis * _rate, 0, MidpointRounding.AwayFromZero);
                var p = new Payment { Booking = b, Amount = payable, Method = method, Status = PaymentStatus.Released, PaidAt = b.CreatedAt.AddMinutes(3),
                    CreatedAt = b.CreatedAt.AddMinutes(3), ReleasedAt = released, CommissionAmount = comm, TaskerAmount = basis - comm,
                    PromoSubsidy = b.DiscountAmount, GatewayTransactionId = $"SIM{b.CreatedAt:yyyyMMddHHmmss}{_rnd.Next(1000, 9999)}" };
                db.Payments.Add(p);
                b.UpdatedAt = released;
                _wallet.Add(new(tasker.Id, released, WalletTransactionType.OnlineEarning, basis - comm, b, "Thu nhập đơn online #{id}"));
                _audit.Add(new("PaymentHeld", b, p, customer.Id, payable, $"Khách trả trước {payable:0} qua {method} – hệ thống tạm giữ", p.PaidAt!.Value));
                _audit.Add(new("Released", b, p, autoConfirm ? null : customer.Id, basis - comm,
                    $"Giải ngân: Tasker {basis - comm:0}, chiết khấu {comm:0}" + (b.DiscountAmount > 0 ? $", nền tảng bù {b.DiscountAmount:0} (mã {b.VoucherCode})" : ""), released));
            }
            return b;
        }

        private void Review(Booking b, User from, User to, int rating, string? comment, DateTime? at = null) =>
            db.Reviews.Add(new Review { Booking = b, Reviewer = from, Reviewee = to, Rating = rating, Comment = comment,
                CreatedAt = at ?? (b.CompletedAt ?? b.ScheduledTime).AddHours(_rnd.Next(1, 8)) });

        private static readonly string[] Good =
        [
            "Làm rất kỹ, đúng giờ, sẽ đặt lại.", "Nhà sạch bong, cảm ơn chị!", "Thợ nhiệt tình, giải thích rõ ràng.", "Nấu ăn ngon, dọn bếp sạch sẽ.",
            "Rất hài lòng, thái độ lịch sự.", "Làm nhanh, gọn gàng, giá hợp lý.", "Tasker chu đáo, mang đủ dụng cụ.",
        ];
        private static readonly string[] Ok = ["Ổn, nhưng đến trễ 10 phút.", "Làm được, còn vài góc chưa kỹ.", "Tạm được."];

        // ---------- ~75 ngày lịch sử ----------
        private void CreateHistory(Dictionary<string, Voucher> v)
        {
            User lan = _t["Trần Thị Lan"], hung = _t["Lê Văn Hùng"], tuan = _t["Phạm Minh Tuấn"], mai = _t["Ngô Thị Mai"];
            var plan = new (User T, string[] Skills, int[] Ratings, int Prev, int Month, int Before)[]
            {
                // tháng trước: đủ ≥ 10 đơn, ≥ 5 đánh giá để hệ thống chốt Tasker xuất sắc (FR-31)
                (mai,  ["clean", "laundry"],        [5, 5, 5, 5, 4], 14, 3, 5),
                (tuan, ["cook", "plant"],           [5, 5, 4, 5, 4], 12, 2, 4),
                (lan,  ["clean", "laundry", "cook"], [5, 4, 5, 4, 5], 13, 3, 6),
                (hung, ["repair", "clean"],         [4, 5, 4, 3, 5], 11, 2, 5),
            };
            var customers = _c.Values.ToList();
            var thisMonth = now.AddHours(7);
            var daysThisMonth = thisMonth.Day - 1;                    // số ngày đã qua của tháng này (không tính hôm nay)
            var prevMonthDays = DateTime.DaysInMonth(thisMonth.AddMonths(-1).Year, thisMonth.AddMonths(-1).Month);
            int prevUsed = 0, monthUsed = 0;

            foreach (var p in plan)
            {
                void Make(int dayOffset, int idx, bool mustReview)
                {
                    var sched = Vn(dayOffset, Pick(new[] { 8, 9, 10, 14, 15, 18 }));
                    var method = _rnd.Next(5) == 0 ? PaymentMethod.Cash : (_rnd.Next(3) == 0 ? PaymentMethod.Card : PaymentMethod.EWallet);
                    Voucher? voucher = null;
                    var svcKey = Pick(p.Skills);
                    if (method != PaymentMethod.Cash && _svc[svcKey].BasePrice >= 150_000 && _rnd.Next(2) == 0)
                    {
                        if (dayOffset < -daysThisMonth && dayOffset >= -daysThisMonth - prevMonthDays && prevUsed < 3) { voucher = v["prev"]; prevUsed++; }
                        else if (dayOffset >= -daysThisMonth && monthUsed < 2) { voucher = v["month"]; monthUsed++; }
                    }
                    var cu = Pick(customers);
                    if (voucher is not null && _bookings.Any(x => x.Voucher == voucher && x.Customer == cu))
                        cu = customers.FirstOrDefault(c => !_bookings.Any(x => x.Voucher == voucher && x.Customer == c)) ?? cu;
                    var b = PaidBooking(cu, p.T, svcKey, sched, method, voucher);
                    // lời mời Tasker khác đã từ chối / để hết hạn (tỷ lệ nhận lời mời – BR-T02)
                    if (_rnd.Next(3) == 0)
                    {
                        var other = Pick(plan.Select(x => x.T).Where(x => x != p.T).ToList());
                        b.Offers.Add(new BookingOffer { Tasker = other, Round = 1, Wave = 1, DistanceKm = 2.4m, Score = 0.7m,
                            Status = _rnd.Next(2) == 0 ? OfferStatus.Rejected : OfferStatus.Expired, SentAt = b.CreatedAt.AddMinutes(16),
                            ExpiresAt = b.CreatedAt.AddMinutes(16).AddSeconds(45), RespondedAt = b.CreatedAt.AddMinutes(16).AddSeconds(20) });
                    }
                    if (mustReview || _rnd.Next(10) < 7)
                    {
                        var r = idx < p.Ratings.Length ? p.Ratings[idx] : Pick(p.Ratings);
                        Review(b, cu, p.T, r, r >= 4 ? Pick(Good) : Pick(Ok));
                    }
                    if (_rnd.Next(2) == 0) Review(b, p.T, cu, Pick(new[] { 5, 5, 4 }), "Khách thân thiện, nhà dễ tìm.");
                }
                // trước tháng trước (ngày −75 … đầu tháng trước)
                for (var k = 0; k < p.Before; k++) Make(-daysThisMonth - prevMonthDays - _rnd.Next(1, 75 - prevMonthDays - daysThisMonth + 1), 10 + k, false);
                // tháng trước
                for (var k = 0; k < p.Prev; k++) Make(-daysThisMonth - 2 - (k * (prevMonthDays - 3) / p.Prev) - _rnd.Next(0, 2), k, k < 6);
                // tháng này (trừ hôm nay)
                for (var k = 0; k < p.Month && daysThisMonth > 0; k++) Make(-_rnd.Next(1, daysThisMonth + 1), 20 + k, false);
            }

            // Đơn đã hủy / hoàn tiền / hết hạn
            for (var k = 0; k < 6; k++)
            {
                var cu = Pick(customers);
                var sched = Vn(-_rnd.Next(2, 60), 9);
                var b = NewBooking(cu, Pick(new[] { "clean", "laundry", "repair", "plant", "cook" }), BookingStatus.Cancelled, null, sched, PaymentMethod.EWallet,
                    created: sched.AddDays(-2));
                var cancelAt = b.CreatedAt.AddHours(4);
                var p = new Payment { Booking = b, Amount = b.TotalPrice, Method = PaymentMethod.EWallet, Status = PaymentStatus.Refunded,
                    PaidAt = b.CreatedAt.AddMinutes(4), CreatedAt = b.CreatedAt.AddMinutes(4), RefundedAmount = b.TotalPrice, RefundedAt = cancelAt,
                    GatewayTransactionId = $"SIM{b.CreatedAt:yyyyMMddHHmmss}{k}" };
                db.Payments.Add(p); b.UpdatedAt = cancelAt;
                _audit.Add(new("Refund", b, p, cu.Id, b.TotalPrice, "Khách hàng đã hủy đơn (miễn phí) – hoàn 100%.", cancelAt));
            }
            var exp = NewBooking(_c["Lê Quốc Huy"], "plant", BookingStatus.Expired, null, Vn(-6, 8), PaymentMethod.Cash, created: Vn(-8, 9));
            exp.UpdatedAt = exp.ScheduledTime;

            // Khiếu nại đã xử lý (trước tháng trước): hoàn 50.000 đ – Lan
            var dOld = PaidBooking(_c["Võ Hoàng Long"], lan, "clean", Vn(-daysThisMonth - prevMonthDays - 5, 9), PaymentMethod.EWallet, qty: 3);
            var pay = db.Payments.Local.Single(x => x.Booking == dOld);
            var refund = 50_000m;
            pay.Status = PaymentStatus.PartiallyRefunded; pay.RefundedAmount = refund; pay.RefundedAt = dOld.CompletedAt!.Value.AddHours(30);
            pay.CommissionAmount = Math.Round((pay.Amount - refund) * _rate, 0, MidpointRounding.AwayFromZero);
            pay.TaskerAmount = pay.Amount - refund - pay.CommissionAmount; pay.ReleasedAt = pay.RefundedAt;
            _wallet.RemoveAll(w => w.Booking == dOld);
            _audit.RemoveAll(a => a.Booking == dOld && a.Action == "Released");
            _wallet.Add(new(lan.Id, pay.ReleasedAt!.Value, WalletTransactionType.OnlineEarning, pay.TaskerAmount, dOld, "Thu nhập đơn online #{id} (sau khiếu nại)"));
            db.Disputes.Add(new Dispute { Booking = dOld, Customer = dOld.Customer!, Tasker = lan, Reason = DisputeReason.Incomplete,
                Description = "Chưa lau cửa kính như đã thỏa thuận khi đặt đơn.", EvidenceUrls = _img["evidence1.jpg"] ?? "",
                TaskerResponse = "Tôi xin lỗi, hôm đó thiếu thời gian, sẽ rút kinh nghiệm.", TaskerRespondedAt = dOld.CompletedAt!.Value.AddHours(5),
                Status = DisputeStatus.Resolved, Resolution = DisputeResolution.PartialRefund, RefundAmount = refund,
                AdminNote = "Đối chiếu ảnh check-out, hạng mục cửa kính chưa làm – hoàn 50.000 đ cho khách.", ResolvedById = adminId,
                CreatedAt = dOld.CompletedAt!.Value.AddHours(2), ResolvedAt = pay.RefundedAt });
            _audit.Add(new("DisputeResolved", dOld, pay, adminId, refund, "Khiếu nại: PartialRefund – hoàn 50.000 đ, giải ngân phần còn lại", pay.RefundedAt!.Value));

            // Rút tiền (ví không bao giờ âm – số rút lớn hơn số dư sẽ được cắt bớt)
            foreach (var t in new[] { lan, hung, tuan, mai })
            {
                _wallet.Add(new(t.Id, Vn(-45, 20), WalletTransactionType.Withdraw, -800_000m, null, "Rút tiền về tài khoản ngân hàng (mô phỏng)"));
                _wallet.Add(new(t.Id, Vn(-daysThisMonth - 3, 20), WalletTransactionType.Withdraw, -1_500_000m, null, "Rút tiền về tài khoản ngân hàng (mô phỏng)"));
            }

            // Đánh giá bị báo cáo (tháng này) – chờ Admin xử lý
            void Flag(User tasker, int rating, string comment, string reason)
            {
                var b = PaidBooking(_c["Phạm Thu Trang"], tasker, tasker == hung ? "repair" : "clean", Vn(-Math.Max(1, Math.Min(3, daysThisMonth)), 14), PaymentMethod.EWallet);
                db.Reviews.Add(new Review { Booking = b, Reviewer = b.Customer, Reviewee = tasker, Rating = rating, Comment = comment, IsFlagged = true,
                    ReportedById = tasker.Id, ReportReason = reason, ReportedAt = b.CompletedAt!.Value.AddHours(20), CreatedAt = b.CompletedAt.Value.AddHours(3) });
            }
            Flag(mai, 1, "Làm ẩu, thái độ không tốt, không bao giờ thuê lại!!!",
                "Tôi làm đủ các hạng mục, khách đã nghiệm thu và không phàn nàn gì. Ảnh sau khi làm có trong đơn, nhận xét này xúc phạm và sai sự thật.");
            Flag(hung, 2, "Sửa xong 2 ngày lại hỏng, liên hệ không nghe máy.",
                "Ổ điện hỏng lại do khách tự lắp thêm thiết bị công suất lớn, tôi đã quay lại kiểm tra miễn phí hôm sau. Tôi luôn nghe máy.");
        }

        // ---------- đơn "đang diễn ra" để demo ----------
        private void CreateLiveOrders(Dictionary<string, Voucher> v)
        {
            User anh = _c["Nguyễn Minh Anh"], ngoc = _c["Trần Bảo Ngọc"];
            User lan = _t["Trần Thị Lan"], hung = _t["Lê Văn Hùng"], tuan = _t["Phạm Minh Tuấn"], mai = _t["Ngô Thị Mai"];

            // Lịch sử riêng của khách demo: 5 đơn hoàn tất → đủ mốc quà khách thân thiết
            var anhPaid = new[] { ("clean", lan, -4), ("repair", hung, -9), ("cook", tuan, -15), ("laundry", mai, -22), ("clean", mai, -30) }
                .Select((x, i) =>
                {
                    var b = PaidBooking(anh, x.Item2, x.Item1, Vn(x.Item3, 9), PaymentMethod.EWallet);
                    if (i < 4) Review(b, anh, x.Item2, i == 1 ? 4 : 5, Good[i]);
                    Review(b, x.Item2, anh, 5, "Khách thân thiện, nhà dễ tìm.");
                    return b;
                }).ToList();
            var loyalty = Gift(anh, VoucherSource.Loyalty, "Quà khách thân thiết", VoucherDiscountType.FixedAmount, 30_000, null, 150_000, anhPaid[0].UpdatedAt!.Value);
            loyalty.SourceBookingId = null;
            Gift(ngoc, VoucherSource.Welcome, "Quà chào mừng", VoucherDiscountType.Percent, 20, 50_000, 100_000, ngoc.CreatedAt.AddMinutes(10)).EndsAt = now.AddDays(25);

            Payment Held(Booking b, DateTime paidAt)
            {
                var p = new Payment { Booking = b, Amount = b.TotalPrice - b.DiscountAmount, Method = b.PaymentMethod, Status = PaymentStatus.Held,
                    PaidAt = paidAt, CreatedAt = paidAt, GatewayTransactionId = $"SIM{paidAt:yyyyMMddHHmmss}{b.Quantity}" };
                db.Payments.Add(p);
                _audit.Add(new("PaymentHeld", b, p, b.Customer!.Id, p.Amount, $"Khách trả trước {p.Amount:0} – hệ thống tạm giữ", paidAt));
                return p;
            }

            // Đã có Tasker nhận – sắp làm (3 ngày tới)
            var acc = NewBooking(anh, "repair", BookingStatus.Accepted, hung, Vn(3, 9), PaymentMethod.EWallet, created: now.AddHours(-5),
                note: "Ổ cắm phòng khách bị chập, nhờ kiểm tra cả bình nóng lạnh.");
            Held(acc, acc.CreatedAt.AddMinutes(3));
            // Đang thực hiện
            var prog = NewBooking(anh, "clean", BookingStatus.InProgress, lan, now.AddMinutes(-50), PaymentMethod.Card, qty: 3, created: now.AddDays(-1),
                note: "Nhà có nuôi mèo, vui lòng đóng cửa phòng ngủ.");
            prog.StartedAt = now.AddMinutes(-45); prog.CheckInPhotoUrl = _img["checkin.jpg"]; prog.UpdatedAt = prog.StartedAt;
            Held(prog, prog.CreatedAt.AddMinutes(2));
            // Tasker đã hoàn thành – chờ khách xác nhận
            var done = NewBooking(anh, "cook", BookingStatus.Completed, tuan, now.AddHours(-4), PaymentMethod.EWallet, created: now.AddDays(-2));
            done.StartedAt = done.ScheduledTime.AddMinutes(3); done.CompletedAt = now.AddHours(-2); done.AutoConfirmAt = done.CompletedAt.Value.AddHours(24);
            done.CheckInPhotoUrl = _img["checkin.jpg"]; done.CheckOutPhotoUrl = _img["checkout.jpg"]; done.UpdatedAt = done.CompletedAt;
            Held(done, done.CreatedAt.AddMinutes(4));
            // Đang khiếu nại (Tasker đã phản hồi) – chờ Admin xử lý
            var disp = NewBooking(anh, "clean", BookingStatus.Disputed, mai, now.AddHours(-9), PaymentMethod.EWallet, qty: 2, created: now.AddDays(-3));
            disp.StartedAt = disp.ScheduledTime.AddMinutes(5); disp.CompletedAt = now.AddHours(-6); disp.AutoConfirmAt = disp.CompletedAt.Value.AddHours(24);
            disp.CheckInPhotoUrl = _img["checkin.jpg"]; disp.CheckOutPhotoUrl = _img["checkin.jpg"]; disp.UpdatedAt = now.AddHours(-5);
            var dp = Held(disp, disp.CreatedAt.AddMinutes(5));
            db.Disputes.Add(new Dispute { Booking = disp, Customer = anh, Tasker = mai, Reason = DisputeReason.PoorQuality,
                Description = "Phòng tắm và bếp chưa được lau sạch, sàn còn nhiều vết bẩn như trong ảnh.",
                EvidenceUrls = string.Join(",", new[] { _img["evidence1.jpg"], _img["evidence2.jpg"] }.Where(x => x is not null)),
                TaskerResponse = "Tôi đã lau toàn bộ sàn và bếp, vết bẩn là vết ố cũ không tẩy được. Ảnh sau khi làm đính kèm.",
                TaskerEvidenceUrls = _img["checkout.jpg"], TaskerRespondedAt = now.AddHours(-3), CreatedAt = now.AddHours(-5) });
            _audit.Add(new("DisputeOpened", disp, dp, anh.Id, dp.Amount, "Khách mở khiếu nại – tạm dừng giải ngân", now.AddHours(-5)));
            // Đã hủy (hoàn tiền)
            var cxl = NewBooking(anh, "laundry", BookingStatus.Cancelled, null, Vn(-3, 10), PaymentMethod.EWallet, created: Vn(-5, 9));
            var cp = new Payment { Booking = cxl, Amount = cxl.TotalPrice, Method = PaymentMethod.EWallet, Status = PaymentStatus.Refunded, PaidAt = cxl.CreatedAt.AddMinutes(2),
                CreatedAt = cxl.CreatedAt.AddMinutes(2), RefundedAmount = cxl.TotalPrice, RefundedAt = cxl.CreatedAt.AddHours(6), GatewayTransactionId = "SIMCXL" + cxl.CreatedAt.ToString("yyyyMMddHHmm") };
            db.Payments.Add(cp); cxl.UpdatedAt = cp.RefundedAt;
            _audit.Add(new("Refund", cxl, cp, anh.Id, cxl.TotalPrice, "Khách hàng đã hủy đơn (miễn phí) – hoàn 100%.", cp.RefundedAt!.Value));

            // Lan có thêm 1 đơn tiền mặt sắp làm (app Tasker có lịch) – ví tạm giữ hoa hồng (BR-P05)
            var lanNext = NewBooking(ngoc, "clean", BookingStatus.Accepted, lan, Vn(2, 14), PaymentMethod.Cash, qty: 2, created: now.AddHours(-20));
            lan.TaskerProfile!.ReservedAmount = Math.Round(lanNext.TotalPrice * _rate, 0, MidpointRounding.AwayFromZero);
        }

        // ---------- ví Tasker: giao dịch theo thời gian, số dư chạy ----------
        private void ApplyWallets()
        {
            foreach (var g in _wallet.GroupBy(w => w.TaskerId))
            {
                decimal bal = 0;
                foreach (var w in g.OrderBy(w => w.At))
                {
                    var amt = w.Amount;
                    if (bal + amt < 0) amt = -bal;           // ví luôn ≥ 0 (BR-P04)
                    if (amt == 0 && w.Type == WalletTransactionType.Withdraw) continue;
                    bal += amt;
                    db.WalletTransactions.Add(new WalletTransaction { TaskerId = g.Key, BookingId = w.Booking?.Id, Type = w.Type, Amount = amt,
                        BalanceAfter = bal, Note = w.Note.Replace("{id}", w.Booking?.Id.ToString() ?? ""), CreatedAt = w.At });
                    if (w.Type is WalletTransactionType.TopUp or WalletTransactionType.Withdraw)
                        _audit.Add(new(w.Type == WalletTransactionType.TopUp ? "WalletTopUp" : "WalletWithdraw", null, null, g.Key, Math.Abs(amt),
                            $"Tasker #{g.Key} {(w.Type == WalletTransactionType.TopUp ? "nạp" : "rút")} ví {Math.Abs(amt):0}", w.At));
                }
                _t.Values.First(t => t.Id == g.Key).TaskerProfile!.WalletBalance = bal;
            }
        }

        private void UpdateRatings()
        {
            foreach (var t in _t.Values)
            {
                var r = db.Reviews.Local.Where(x => x.RevieweeId == t.Id && !x.IsFlagged).Select(x => x.Rating).ToList();
                t.TaskerProfile!.RatingAverage = r.Count == 0 ? 5.00m : Math.Round((decimal)r.Average(), 2, MidpointRounding.AwayFromZero);
            }
        }

        private void CreateNotifications()
        {
            User anh = _c["Nguyễn Minh Anh"], lan = _t["Trần Thị Lan"];
            void N(User u, string title, string msg, bool read, DateTime at) => NId(u.Id, title, msg, read, at);
            void NId(int userId, string title, string msg, bool read, DateTime at) =>
                db.Notifications.Add(new Notification { UserId = userId, Title = title, Message = msg, IsRead = read, CreatedAt = at });
            var acc = _bookings.First(b => b.Customer == anh && b.Status == BookingStatus.Accepted);
            var disp = _bookings.First(b => b.Status == BookingStatus.Disputed);
            var gift = db.Vouchers.Local.First(x => x.Source == VoucherSource.Loyalty);
            N(anh, "Đã có Tasker nhận đơn", $"Lê Văn Hùng đã nhận đơn #{acc.Id} lúc {VnTime.Format(acc.ScheduledTime)}.", false, acc.CreatedAt.AddMinutes(20));
            N(anh, "Tasker đã bắt đầu làm việc", "Đơn Dọn dẹp đang được thực hiện. Ảnh check-in đã được cập nhật.", false, now.AddMinutes(-45));
            N(anh, "Tasker đã hoàn thành công việc", "Vui lòng kiểm tra và xác nhận trong 24 giờ, nếu không hệ thống sẽ tự xác nhận.", false, now.AddHours(-2));
            N(anh, "Tasker đã phản hồi khiếu nại", $"Ngô Thị Mai đã gửi phản hồi cho khiếu nại đơn #{disp.Id}. Quản trị viên sẽ xử lý sớm.", false, now.AddHours(-3));
            N(anh, "Bạn nhận được quà", $"Quà khách thân thiết: giảm 30.000 đ cho đơn từ 150.000 đ. Mã {gift.Code}, dùng trong 30 ngày.", true, gift.CreatedAt);
            N(anh, "Đã hoàn tiền", "Đơn Giặt ủi đã hủy: hoàn 100% về phương thức bạn đã thanh toán.", true, now.AddDays(-5).AddHours(6));
            N(lan, "Bạn có lịch làm việc", "Đơn Dọn dẹp lúc 14:00 (2 ngày tới) của Trần Bảo Ngọc – thanh toán tiền mặt.", false, now.AddHours(-20));
            N(lan, "Đánh giá mới", "Khách hàng vừa đánh giá 5★ cho đơn bạn đã làm.", true, now.AddDays(-4));
            foreach (var (title, msg, mins) in new[]
                     {
                         ("Khiếu nại mới", $"Đơn #{disp.Id} có khiếu nại mới cần xử lý.", 300),
                         ("Hồ sơ Tasker chờ duyệt", "Tasker Võ Văn Nam và Đỗ Thị Hạnh vừa gửi hồ sơ xác thực.", 600),
                         ("Đánh giá bị báo cáo", "Có 2 đánh giá bị Tasker báo cáo, cần kiểm duyệt.", 1200),
                     })
                NId(adminId, title, msg, false, now.AddMinutes(-mins));
        }
    }
}
