using System.ComponentModel.DataAnnotations;
using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// ================= EP09 – Tasker xuất sắc tháng (FR-31) =================

public record AwardBadgeResponse(int Year, int Month, int Rank, string Label);

public record TaskerAwardResponse(
    int Id, int Year, int Month, int TaskerId, string TaskerName, string? Email, string? PhoneNumber, string? AvatarUrl,
    string? WorkingArea, int Rank, decimal Score, int CompletedBookings, int ReviewCount, decimal RatingAverage,
    decimal AcceptanceRate, decimal OnTimeRate, int DisputeCount, TaskerAwardStatus Status, string? ExcludeReason,
    decimal BonusAmount, decimal? PreferentialRate, bool HasGift, string? GiftDescription, bool RewardCustomized,
    DateTime? ApprovedAt, DateTime? BonusPaidAt, DateTime? PreferentialFrom, DateTime? PreferentialTo,
    DateTime? GiftEmailSentAt, string? GiftInvitation, DateTime? GiftDeliveredAt);

/// <summary>State: NotComputed | Proposed | Approved. Score hiển thị theo thang 100.</summary>
public record TaskerAwardMonthResponse(
    int Year, int Month, string State, bool MonthEnded, DateTime? ComputedAt, DateTime? ApprovedAt, string? ApprovedByName,
    decimal DefaultCommissionRate, IReadOnlyList<TaskerAwardResponse> Items);

public class AwardMonthRequest
{
    [Range(2020, 2100, ErrorMessage = "Tháng không hợp lệ.")]
    public int Year { get; set; }

    [Range(1, 12, ErrorMessage = "Tháng không hợp lệ.")]
    public int Month { get; set; }
}

public class UpdateTaskerAwardRequest
{
    [Range(0, 50_000_000, ErrorMessage = "Tiền thưởng không hợp lệ.")]
    public decimal? BonusAmount { get; set; }

    /// <summary>Tỷ lệ chiết khấu ưu đãi tháng sau (vd 0.1 = 10%); null = không có.</summary>
    [Range(0.0, 1.0, ErrorMessage = "Chiết khấu ưu đãi không hợp lệ.")]
    public decimal? PreferentialRate { get; set; }

    public bool HasGift { get; set; }

    [MaxLength(200, ErrorMessage = "Tên quà tối đa 200 ký tự.")]
    public string? GiftDescription { get; set; }
}

public class ExcludeTaskerAwardRequest
{
    [Required(ErrorMessage = "Lý do loại cần ít nhất 10 ký tự.")]
    [StringLength(500, MinimumLength = 10, ErrorMessage = "Lý do loại cần ít nhất 10 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}

public class GiftInvitationRequest
{
    [Required(ErrorMessage = "Vui lòng nhập thời gian nhận quà.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Vui lòng nhập thời gian nhận quà.")]
    public string Time { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa điểm nhận quà.")]
    [StringLength(300, MinimumLength = 5, ErrorMessage = "Vui lòng nhập địa điểm nhận quà.")]
    public string Place { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    public string? Note { get; set; }
}

// ================= EP10 – Mã giảm giá & quà tặng (FR-32, FR-33) =================

public class CheckVoucherRequest
{
    [Required(ErrorMessage = "Vui lòng nhập mã giảm giá.")]
    [StringLength(30, MinimumLength = 1, ErrorMessage = "Mã giảm giá không tồn tại.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Dịch vụ không tồn tại.")]
    public int? ServiceCategoryId { get; set; }

    [Range(1, 12, ErrorMessage = "Số lượng từ 1 đến 12.")]
    public int? Quantity { get; set; }

    public PaymentMethod? PaymentMethod { get; set; }
}

public record VoucherCheckResponse(int VoucherId, string Code, string Name, string? Description, decimal OriginalPrice,
    decimal DiscountAmount, decimal PayableAmount, string Message);

/// <summary>Một ưu đãi khách đang dùng được (quà riêng hoặc chương trình công khai).</summary>
public record MyVoucherResponse(int Id, string Code, string Name, string? Description, VoucherDiscountType DiscountType,
    decimal DiscountValue, decimal? MaxDiscount, decimal MinOrderValue, IReadOnlyList<string> ServiceNames, DateTime EndsAt,
    VoucherSource Source, bool IsGift, int RemainingUses);

public class SaveVoucherRequest
{
    [Required(ErrorMessage = "Mã chỉ gồm chữ không dấu, số, gạch ngang (3–30 ký tự).")]
    [RegularExpression(@"^[A-Za-z0-9\-]{3,30}$", ErrorMessage = "Mã chỉ gồm chữ không dấu, số, gạch ngang (3–30 ký tự).")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên chương trình.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Tên chương trình từ 3 đến 100 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300, ErrorMessage = "Mô tả tối đa 300 ký tự.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Loại giảm giá không hợp lệ.")]
    public VoucherDiscountType? DiscountType { get; set; }

    [Range(1, 10_000_000, ErrorMessage = "Giá trị giảm không hợp lệ.")]
    public decimal DiscountValue { get; set; }

    [Range(0, 10_000_000, ErrorMessage = "Mức giảm tối đa không hợp lệ.")]
    public decimal? MaxDiscount { get; set; }

    [Range(0, 100_000_000, ErrorMessage = "Giá trị đơn tối thiểu không hợp lệ.")]
    public decimal MinOrderValue { get; set; }

    /// <summary>Rỗng = mọi dịch vụ.</summary>
    public List<int>? ServiceCategoryIds { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn thời gian bắt đầu.")]
    public DateTime? StartsAt { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn thời gian kết thúc.")]
    public DateTime? EndsAt { get; set; }

    [Range(1, 1_000_000, ErrorMessage = "Tổng lượt dùng không hợp lệ.")]
    public int? TotalLimit { get; set; }

    [Range(1, 100, ErrorMessage = "Số lượt mỗi khách từ 1 đến 100.")]
    public int PerCustomerLimit { get; set; } = 1;

    public VoucherAudience Audience { get; set; } = VoucherAudience.AllCustomers;
    public bool IsPublic { get; set; } = true;
}

public class VoucherStatusRequest
{
    /// <summary>pause | resume | end</summary>
    [Required(ErrorMessage = "Thao tác không hợp lệ.")]
    [RegularExpression("^(pause|resume|end)$", ErrorMessage = "Thao tác không hợp lệ.")]
    public string Action { get; set; } = string.Empty;
}

/// <summary>Status: Scheduled | Active | Paused | Ended | SoldOut.</summary>
public record VoucherAdminResponse(int Id, string Code, string Name, string? Description, VoucherDiscountType DiscountType,
    decimal DiscountValue, decimal? MaxDiscount, decimal MinOrderValue, IReadOnlyList<int> ServiceCategoryIds,
    IReadOnlyList<string> ServiceNames, DateTime StartsAt, DateTime EndsAt, int? TotalLimit, int PerCustomerLimit,
    int UsedCount, int CompletedCount, decimal TotalDiscount, VoucherAudience Audience, bool IsPublic, bool IsActive,
    string Status, DateTime CreatedAt);

public record GiftStatResponse(VoucherSource Kind, string Name, int Granted, int Used, decimal TotalDiscount);

public record PromotionSummaryResponse(int ActiveCampaigns, int TotalCampaigns, int CampaignUses, decimal CampaignDiscount,
    decimal GiftDiscount, decimal TotalDiscount, IReadOnlyList<GiftStatResponse> Gifts);

public record GiftRuleResponse(VoucherSource Kind, string Name, bool IsEnabled, VoucherDiscountType DiscountType,
    decimal DiscountValue, decimal? MaxDiscount, decimal MinOrderValue, int ValidDays, int EveryBookings, string Summary,
    DateTime? UpdatedAt);

public class SaveGiftRuleRequest
{
    public bool IsEnabled { get; set; } = true;

    [Required(ErrorMessage = "Loại giảm giá không hợp lệ.")]
    public VoucherDiscountType? DiscountType { get; set; }

    [Range(1, 10_000_000, ErrorMessage = "Giá trị giảm không hợp lệ.")]
    public decimal DiscountValue { get; set; }

    [Range(0, 10_000_000, ErrorMessage = "Mức giảm tối đa không hợp lệ.")]
    public decimal? MaxDiscount { get; set; }

    [Range(0, 100_000_000, ErrorMessage = "Giá trị đơn tối thiểu không hợp lệ.")]
    public decimal MinOrderValue { get; set; }

    [Range(1, 365, ErrorMessage = "Hạn dùng từ 1 đến 365 ngày.")]
    public int ValidDays { get; set; } = 30;

    [Range(0, 100, ErrorMessage = "Số đơn để nhận quà từ 1 đến 100.")]
    public int EveryBookings { get; set; }
}
