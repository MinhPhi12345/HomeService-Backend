namespace HomeService.Api.Enums;

// SRS 7.8 – loại biến động ví Tasker
public enum WalletTransactionType
{
    TopUp = 0,          // Nạp tiền
    Commission = 1,     // Trừ hoa hồng (đơn tiền mặt)
    OnlineEarning = 2,  // Thu nhập đơn online
    Withdraw = 3,       // Rút tiền
    CancellationFee = 4 // Phí hủy được nhận
}
