namespace HomeService.Api.Models;

// EP06 - Đánh giá & Nhận xét (US25-US27)
public class Review
{
    public int Id { get; set; }

    public int BookingId { get; set; }
    public Booking? Booking { get; set; }

    public int ReviewerId { get; set; } // người đánh giá
    public User? Reviewer { get; set; }

    public int RevieweeId { get; set; } // người được đánh giá
    public User? Reviewee { get; set; }

    public int Rating { get; set; } // 1-5
    public string? Comment { get; set; }
    public bool IsFlagged { get; set; } = false; // US27: đánh dấu để Admin xử lý khiếu nại

    // US27 – người trong đơn báo cáo đánh giá sai sự thật: bắt buộc nêu lý do để Admin có căn cứ phân xử
    public int? ReportedById { get; set; }
    public string? ReportReason { get; set; }
    public DateTime? ReportedAt { get; set; }

    // US27 – kết quả kiểm duyệt của Admin (giữ lại hoặc gỡ), kèm lý do
    public int? ModeratedById { get; set; }
    public string? ModerationNote { get; set; }
    public DateTime? ModeratedAt { get; set; }

    // US27 / BR-R01 – đánh giá bị Admin gỡ chỉ bị ẩn (không xóa hẳn) để người viết không đánh giá lại được đơn đó
    public DateTime? RemovedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
