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
    int Quantity,
    // US26 – uy tín của khách (điểm trung bình Tasker đánh giá khách; null nếu chưa có)
    decimal? CustomerRatingAverage,
    // US26 – để Tasker mở chi tiết lời mời và xem nhận xét về khách (GET /api/reviews/user/{customerId})
    int CustomerId = 0,
    int CustomerReviewCount = 0,
    // SRS v1.8 – thời lượng ước tính, đơn vị và hạng mục khách liệt kê (Tasker biết trước việc cần làm)
    int EstimatedMinutes = 0,
    string? ServiceUnit = null,
    IReadOnlyList<string>? Items = null
);
