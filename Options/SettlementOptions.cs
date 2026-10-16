namespace HomeService.Api.Options;

// appsettings.json → "Settlement" (FR-16)
public class SettlementOptions
{
    public const string Section = "Settlement";

    public int PaymentTimeoutMinutes { get; set; } = 15;
    public int CashMinCompletedOrders { get; set; } = 3;
    public decimal CashMaxOrderValue { get; set; } = 1_000_000m;
    public int AutoConfirmHours { get; set; } = 24;
    public int ReminderHoursBefore { get; set; } = 2;
    /// <summary>Báo Tasker khi số dư ví sau khi trừ chiết khấu thấp hơn mức này (FR-21).</summary>
    public decimal LowBalanceThreshold { get; set; } = 50_000m;
}
