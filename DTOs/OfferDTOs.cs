using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// Phần tử của GET /api/offers/my (SRS 9.4)
public record OfferResponse(
    int OfferId,
    int BookingId,
    string ServiceCategoryName,
    string Address,
    DateTime ScheduledTime,
    decimal DistanceKm,
    decimal TotalPrice,
    PaymentMethod PaymentMethod,
    decimal EstimatedCommission,
    DateTime ExpiresAt,
    OfferStatus Status,
    // Bổ sung: hiển thị thêm cho Tasker
    int Wave,
    string? Note,
    int Quantity
);
