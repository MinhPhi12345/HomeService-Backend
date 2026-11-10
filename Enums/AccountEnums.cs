namespace HomeService.Api.Enums;

// US30 / US48 – khóa tài khoản & yêu cầu mở khóa (SRS v1.8). Gửi/nhận dưới dạng SỐ, chỉ thêm giá trị mới vào CUỐI.
public enum AccountLockReason
{
    Fraud = 0,              // Gian lận
    NonPayment = 1,         // Không thanh toán
    RepeatedViolations = 2, // Vi phạm nhiều lần
    CustomerComplaints = 3, // Phản ánh của khách hàng / Tasker
    Misconduct = 4,         // Hành vi không phù hợp
    Other = 5               // Khác
}

/// <summary>Cách khóa khi người dùng còn đơn đang dở.</summary>
public enum AccountLockMode
{
    AfterOrders = 0, // Khóa sau khi hoàn tất các đơn đang dở (mặc định)
    Immediate = 1    // Khóa ngay, hệ thống tự xử lý các đơn đang dở
}

public enum AppealStatus
{
    Pending = 0,  // Chờ xử lý
    Approved = 1, // Đã chấp nhận – tài khoản được mở khóa
    Rejected = 2  // Đã từ chối
}

public static class AccountLockReasonText
{
    public static string Of(AccountLockReason? reason) => reason switch
    {
        AccountLockReason.Fraud => "Gian lận",
        AccountLockReason.NonPayment => "Không thanh toán",
        AccountLockReason.RepeatedViolations => "Vi phạm nhiều lần",
        AccountLockReason.CustomerComplaints => "Bị phản ánh nhiều lần",
        AccountLockReason.Misconduct => "Hành vi không phù hợp",
        _ => "Khác",
    };
}
