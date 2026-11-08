using HomeService.Api.Enums;

namespace HomeService.Api.Models;

// EP09 – Tasker xuất sắc tháng (FR-31, SRS 7.12). Mỗi dòng = 1 Tasker trong bảng xếp hạng đã chốt của 1 tháng.
// Kết quả được LƯU CỐ ĐỊNH lúc chốt: dữ liệu đơn/đánh giá thay đổi về sau không làm đổi bảng đã công bố.
public class TaskerMonthlyAward
{
    public int Id { get; set; }

    public int Year { get; set; }
    public int Month { get; set; } // 1–12, theo giờ Việt Nam

    public int TaskerId { get; set; }
    public User? Tasker { get; set; }

    public int Rank { get; set; }      // hạng trong số Tasker chưa bị loại (1 = cao nhất)
    public decimal Score { get; set; } // điểm tổng 0–1 (hiển thị ×100)

    // Chỉ số của tháng tại thời điểm chốt
    public int CompletedBookings { get; set; }
    public int ReviewCount { get; set; }
    public decimal RatingAverage { get; set; }  // trung bình sao thật của tháng
    public decimal AcceptanceRate { get; set; } // 0–1
    public decimal OnTimeRate { get; set; }     // 0–1
    public int DisputeCount { get; set; }

    public TaskerAwardStatus Status { get; set; } = TaskerAwardStatus.Proposed;
    public string? ExcludeReason { get; set; }

    // Phần thưởng (mặc định theo hạng – Awards:Tiers; Admin chỉnh được trước khi duyệt)
    public decimal BonusAmount { get; set; }
    public decimal? PreferentialRate { get; set; } // tỷ lệ chiết khấu ưu đãi tháng sau, null = không có
    public bool HasGift { get; set; }
    public string? GiftDescription { get; set; }
    public bool RewardCustomized { get; set; }     // Admin đã tự chỉnh → không tự gán lại theo hạng

    // Sau khi duyệt
    public DateTime? ApprovedAt { get; set; }
    public int? ApprovedById { get; set; }
    public DateTime? BonusPaidAt { get; set; }
    public DateTime? PreferentialFrom { get; set; }
    public DateTime? PreferentialTo { get; set; }
    public DateTime? GiftEmailSentAt { get; set; }
    public string? GiftInvitation { get; set; }    // thời gian, địa điểm, ghi chú đã gửi trong email mời nhận quà
    public DateTime? GiftDeliveredAt { get; set; }

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}
