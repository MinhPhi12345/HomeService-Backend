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
    // US14 – phí hủy (BR-C01)
    public double LateCancelHours { get; set; } = 2;
    public decimal LateCancelFeePercent { get; set; } = 20;
    public decimal ArrivedCancelFeePercent { get; set; } = 50;
    public double ArrivedDistanceMeters { get; set; } = 200;
    public int ArrivedLocationFreshMinutes { get; set; } = 30;

    // US35 – khiếu nại & vi phạm (BR-D01, BR-D02)
    public int DisputeHours { get; set; } = 24;
    public int TaskerResponseHours { get; set; } = 24;
    public int ViolationPointsToSuspend { get; set; } = 3;
    public int SuspendDays { get; set; } = 7;

    // US37 – Tasker không đến (FR-28)
    public int NoShowMinutes { get; set; } = 30;
    public int NoShowAutoCancelHours { get; set; } = 2;

    public decimal LowBalanceThreshold { get; set; } = 50_000m;
}
