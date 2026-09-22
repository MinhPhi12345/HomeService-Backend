namespace HomeService.Api.Enums;

// US12-US20: vòng đời một đơn đặt dịch vụ
public enum BookingStatus
{
    Pending = 0,       // Vừa tạo, chờ ghép nối Tasker (US16)
    Matched = 1,        // Đã ghép nối, chờ Tasker xác nhận (US17)
    Accepted = 2,        // Tasker đã nhận đơn (US17)
    InProgress = 3,      // Tasker đang thực hiện (US18)
    Completed = 4,       // Hoàn thành, chờ thanh toán/đánh giá
    Paid = 5,            // Đã thanh toán (US21)
    Cancelled = 6,        // Khách hàng hủy đơn (US14)
    Rejected = 7          // Tasker từ chối (US17)
}
