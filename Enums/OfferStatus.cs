namespace HomeService.Api.Enums;

// Trạng thái lời mời nhận đơn – BookingOffer (SRS 7.8, 7.11)
public enum OfferStatus
{
    Sent = 0,      // Đang chờ Tasker trả lời
    Accepted = 1,  // Tasker đã nhận
    Rejected = 2,  // Tasker từ chối
    Expired = 3,   // Hết hạn trả lời
    Closed = 4     // Đã đóng (Tasker khác nhận / đơn bị hủy)
}
