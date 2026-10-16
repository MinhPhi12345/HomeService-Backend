namespace HomeService.Api.Enums;

public enum PaymentMethod
{
    Cash = 0,
    EWallet = 1,
    Card = 2
}

// SRS 7.8 – chỉ thêm giá trị mới vào cuối
public enum PaymentStatus
{
    Pending = 0,
    Success = 1,           // Tiền mặt đã thu
    Failed = 2,
    Held = 3,              // Đang tạm giữ (online đã trả trước) – US21
    Released = 4,          // Đã giải ngân cho Tasker – US34
    Refunded = 5,          // Đã hoàn toàn bộ
    PartiallyRefunded = 6  // Đã hoàn một phần
}
