namespace HomeService.Api.Enums;

// US12-US20, US33-US37: vòng đời một đơn đặt dịch vụ (SRS 7.8 & 10.2).
// Gửi/nhận dưới dạng SỐ. Chỉ được thêm giá trị mới vào CUỐI enum, không đổi thứ tự.
public enum BookingStatus
{
    Pending = 0,          // Đang tìm Tasker (đã thanh toán trước, hoặc đơn tiền mặt) – US16
    Matched = 1,          // Đã gửi lời mời – chờ Tasker nhận (US17)
    Accepted = 2,         // Tasker đã nhận đơn (US17)
    InProgress = 3,       // Đang thực hiện (US18)
    Completed = 4,        // Hoàn thành – chờ khách xác nhận (US34)
    Paid = 5,             // Đã hoàn tất thanh toán (US21, US34)
    Cancelled = 6,        // Đã hủy (US14)
    Rejected = 7,         // Không còn gán cho đơn – chỉ giữ để tương thích
    NoTaskerFound = 8,    // Chưa có Tasker nhận (US33)
    Expired = 9,          // Đã hết hạn (US33)
    AwaitingPayment = 10, // Chờ thanh toán trước (US21)
    Disputed = 11,        // Đang khiếu nại (US35)
    Refunded = 12,        // Đã hoàn tiền (US35)
    PaymentOverdue = 13   // Khách chưa thanh toán tiền mặt (US36)
}
