using System.ComponentModel.DataAnnotations;
using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// US23 – SRS 9.7
public class TopUpRequest
{
    [Required(ErrorMessage = "Số tiền nạp không hợp lệ.")]
    [Range(50_000, 10_000_000, ErrorMessage = "Số tiền nạp từ 50.000 đ đến 10.000.000 đ.")]
    public decimal? Amount { get; set; }

    /// <summary>Kênh nạp ở cổng thanh toán mô phỏng: EWallet | Card | BankTransfer (để ghi chú lịch sử ví).</summary>
    [MaxLength(20)]
    public string? Channel { get; set; }
}

/// <summary>Bước 1 rút tiền: kiểm tra số tiền và gửi mã OTP xác nhận về email Tasker.</summary>
public class WithdrawOtpRequest
{
    [Required(ErrorMessage = "Số tiền rút không hợp lệ.")]
    [Range(50_000, double.MaxValue, ErrorMessage = "Số tiền rút không hợp lệ.")]
    public decimal? Amount { get; set; }
}

/// <summary>Bước 2 rút tiền: cùng số tiền + mã OTP đã nhận.</summary>
public class WithdrawRequest
{
    [Required(ErrorMessage = "Số tiền rút không hợp lệ.")]
    [Range(50_000, double.MaxValue, ErrorMessage = "Số tiền rút không hợp lệ.")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã OTP.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Mã OTP gồm 6 chữ số.")]
    public string? OtpCode { get; set; }
}

// MinDeposit: mức ký quỹ tối thiểu; MeetsDeposit: số dư khả dụng đã đạt mức đó (được bật/nhận đơn)
public record WalletResponse(decimal Balance, decimal ReservedAmount, decimal AvailableBalance,
    decimal MinDeposit = 0, bool MeetsDeposit = true);

public record WalletTransactionResponse(
    int Id,
    WalletTransactionType Type,
    decimal Amount,
    decimal BalanceAfter,
    int? BookingId,
    string? Note,
    DateTime CreatedAt
);
