using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// US31 – GET /api/reports/revenue (SRS 9.10)
public record RevenueReport(
    DateTime From,
    DateTime To,
    decimal TotalRevenue,     // tổng tiền thực thu (amount − refundedAmount) của các giao dịch đã kết thúc
    decimal TotalCommission,  // doanh thu nền tảng
    int TransactionCount,
    decimal CashRevenue,
    decimal OnlineRevenue,
    decimal HeldAmount,       // tiền đang tạm giữ tại thời điểm "to" (chưa phải doanh thu)
    decimal RefundedAmount,   // tổng tiền đã hoàn trong kỳ
    // Bổ sung
    decimal TaskerPayout,     // tổng tiền Tasker nhận
    string GroupBy,
    IReadOnlyList<RevenuePoint> Series
);

/// <summary>Một điểm trên biểu đồ doanh thu theo ngày/tháng/năm (giờ Việt Nam).</summary>
public record RevenuePoint(string Period, decimal Revenue, decimal Commission, int TransactionCount);

// US32
public record BookingSummaryItem(BookingStatus Status, int Count);

public record TaskerPerformanceItem(
    int TaskerId,
    int CompletedCount,
    int CancelledCount,
    // Bổ sung
    string TaskerName,
    decimal RatingAverage,
    int AcceptedOffers,
    int TotalOffers,
    decimal? AcceptanceRate,
    int NoShowCount,
    int DisputeCount,
    int ViolationPoints,
    decimal TotalEarnings
);
