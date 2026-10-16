namespace HomeService.Api.Options;

// appsettings.json → "Settlement" (FR-16)
public class SettlementOptions
{
    public const string Section = "Settlement";

    public int PaymentTimeoutMinutes { get; set; } = 15;
    public int CashMinCompletedOrders { get; set; } = 3;
    public decimal CashMaxOrderValue { get; set; } = 1_000_000m;
}
