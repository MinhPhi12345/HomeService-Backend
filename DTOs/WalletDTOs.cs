using System.ComponentModel.DataAnnotations;
using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// US23 – SRS 9.7
public class TopUpRequest
{
    [Required(ErrorMessage = "Số tiền nạp không hợp lệ.")]
    [Range(50_000, 10_000_000, ErrorMessage = "Số tiền nạp từ 50.000 đ đến 10.000.000 đ.")]
    public decimal? Amount { get; set; }
}

public class WithdrawRequest
{
    [Required(ErrorMessage = "Số tiền rút không hợp lệ.")]
    [Range(50_000, double.MaxValue, ErrorMessage = "Số tiền rút không hợp lệ.")]
    public decimal? Amount { get; set; }
}

public record WalletResponse(decimal Balance, decimal ReservedAmount, decimal AvailableBalance);

public record WalletTransactionResponse(
    int Id,
    WalletTransactionType Type,
    decimal Amount,
    decimal BalanceAfter,
    int? BookingId,
    string? Note,
    DateTime CreatedAt
);
