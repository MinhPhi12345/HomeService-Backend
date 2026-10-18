using HomeService.Api.Enums;

namespace HomeService.Api.Models;

// EP03 + EP04 - Đặt dịch vụ, ghép nối & theo dõi (SRS 7.4)
public class Booking
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public User? Customer { get; set; }

    public int ServiceCategoryId { get; set; }
    public ServiceCategory? ServiceCategory { get; set; }

    public int? TaskerId { get; set; } // null cho đến khi có Tasker nhận đơn (Accepted)
    public User? Tasker { get; set; }

    public DateTime ScheduledTime { get; set; } // US13 (UTC)
    public string Address { get; set; } = string.Empty; // US13
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public string? Note { get; set; }
    public int Quantity { get; set; } = 1;        // số giờ / số lần (1–12)
    public decimal TotalPrice { get; set; }       // = basePrice × quantity (BR-B01)

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // US12 – thanh toán & chiết khấu chốt tại lúc tạo đơn (BR-P01, BR-P06)
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.EWallet;
    public decimal CommissionRate { get; set; }
    public bool CustomerPresent { get; set; }
    public DateTime? PaymentDueAt { get; set; } // hạn thanh toán trước (đơn online)

    // US16 – trạng thái ghép nối lưu trong DB để Backend khởi động lại vẫn chạy tiếp
    public int MatchRound { get; set; } = 1;  // tăng khi khách bấm Thử lại
    public int CurrentWave { get; set; } = 0; // 0 = chưa bắt đầu, 1–3
    public DateTime? NextMatchAt { get; set; } // lần xử lý ghép nối kế tiếp

    // US18 – ảnh check-in/check-out (bằng chứng khi khiếu nại, không sửa/xóa được)
    public string? CheckInPhotoUrl { get; set; }
    public string? CheckOutPhotoUrl { get; set; }

    // US34 – xác nhận hoàn thành & giải ngân
    public DateTime? CompletedAt { get; set; }     // lúc Tasker bấm Hoàn thành; mốc tính hạn khiếu nại
    public DateTime? AutoConfirmAt { get; set; }   // = CompletedAt + 24 giờ
    public bool IsAutoConfirmed { get; set; }
    public DateTime? AutoConfirmReminderSentAt { get; set; } // đã nhắc khách trước hạn tự xác nhận

    // US37 – Tasker không đến (quá 30 phút sau giờ hẹn chưa Bắt đầu)
    public bool IsTaskerNoShow { get; set; }
    public DateTime? NoShowAt { get; set; }

    // US14 – phí hủy đã tính khi khách hủy (BR-C01)
    public decimal CancellationFee { get; set; }

    public Payment? Payment { get; set; }
    public Dispute? Dispute { get; set; }
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<BookingOffer> Offers { get; set; } = new List<BookingOffer>();
}
