using HomeService.Api.Enums;

namespace HomeService.Api.Models;

// EP10 – Mã giảm giá (FR-32) & quà tặng tự động (FR-33), SRS 7.12.
// Quà tặng = mã giảm giá gán riêng cho 1 khách (AssignedCustomerId), dùng 1 lần.
public class Voucher
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;   // chữ HOA, không dấu, duy nhất
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public VoucherDiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }   // % (1–100) hoặc số tiền
    public decimal? MaxDiscount { get; set; }    // chỉ với giảm theo %
    public decimal MinOrderValue { get; set; }
    public string? ServiceCategoryIds { get; set; } // "1,3" – null = mọi dịch vụ

    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public int? TotalLimit { get; set; }          // null = không giới hạn
    public int PerCustomerLimit { get; set; } = 1;
    public int UsedCount { get; set; }            // lượt đang giữ + đã dùng

    public VoucherAudience Audience { get; set; } = VoucherAudience.AllCustomers;
    public bool IsPublic { get; set; } = true;    // hiện trong "Ưu đãi của tôi" của mọi khách phù hợp
    public bool IsActive { get; set; } = true;    // false = tạm dừng

    public int? AssignedCustomerId { get; set; }  // quà tặng riêng
    public User? AssignedCustomer { get; set; }
    public VoucherSource Source { get; set; } = VoucherSource.Campaign;
    public int? SourceBookingId { get; set; }     // đơn làm phát sinh quà (thân thiết / xin lỗi)

    public int? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<VoucherRedemption> Redemptions { get; set; } = new List<VoucherRedemption>();
}

// Mỗi đơn dùng tối đa 1 mã (UNIQUE BookingId)
public class VoucherRedemption
{
    public int Id { get; set; }
    public int VoucherId { get; set; }
    public Voucher? Voucher { get; set; }
    public int CustomerId { get; set; }
    public int BookingId { get; set; }
    public Booking? Booking { get; set; }
    public decimal DiscountAmount { get; set; }
    public VoucherRedemptionStatus Status { get; set; } = VoucherRedemptionStatus.Applied;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
}

// Cài đặt quà tặng tự động (Admin chỉnh trên trang Khuyến mãi). Thiếu dòng trong CSDL → dùng giá trị mặc định.
public class GiftRule
{
    public int Id { get; set; }
    public VoucherSource Kind { get; set; }      // Welcome / Loyalty / Apology
    public bool IsEnabled { get; set; } = true;
    public VoucherDiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public decimal? MaxDiscount { get; set; }
    public decimal MinOrderValue { get; set; }
    public int ValidDays { get; set; } = 30;
    public int EveryBookings { get; set; }       // chỉ quà thân thiết: tặng sau mỗi N đơn hoàn tất
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedById { get; set; }
}
