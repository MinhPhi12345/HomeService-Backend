using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;

namespace HomeService.Api.Services;

public static class BookingMapper
{
    public const int DefaultDisputeHours = 24;

    /// <summary>Cần Include: Customer, Tasker, ServiceCategory, Payment, Dispute, Reviews.</summary>
    public static BookingResponse ToResponse(Booking b, DateTime nowUtc, int? viewerId = null, int disputeHours = DefaultDisputeHours)
    {
        var activeJob = b.Status is BookingStatus.Accepted or BookingStatus.InProgress;
        var showContact = activeJob && viewerId is not null && (viewerId == b.TaskerId || viewerId == b.CustomerId);
        var finished = b.Status is BookingStatus.Completed or BookingStatus.Paid;
        var isParty = viewerId is not null && b.TaskerId is not null && (viewerId == b.TaskerId || viewerId == b.CustomerId);
        var canReview = finished && isParty && b.Reviews.All(r => r.ReviewerId != viewerId);
        var isCustomer = viewerId is not null && viewerId == b.CustomerId;
        return Map(b, nowUtc, disputeHours, showContact, canReview, isCustomer);
    }

    private static BookingResponse Map(Booking b, DateTime nowUtc, int disputeHours, bool showContact, bool canReview,
        bool isCustomer) => new()
    {
        Id = b.Id,
        ServiceCategoryName = b.ServiceCategory?.Name ?? string.Empty,
        CustomerId = b.CustomerId,
        CustomerName = b.Customer?.FullName ?? string.Empty,
        TaskerId = b.TaskerId,
        TaskerName = b.Tasker?.FullName,
        ScheduledTime = b.ScheduledTime,
        Address = b.Address,
        TotalPrice = b.TotalPrice,
        Status = b.Status,
        CreatedAt = b.CreatedAt,
        CommissionRate = b.CommissionRate,
        PaymentMethod = b.PaymentMethod,
        CustomerPresent = b.CustomerPresent,
        PaymentStatus = b.Payment?.Status,
        PaymentDueAt = b.PaymentDueAt,
        CompletedAt = b.CompletedAt,
        IsTaskerNoShow = b.IsTaskerNoShow,
        AutoConfirmAt = b.AutoConfirmAt,
        CheckInPhotoUrl = b.CheckInPhotoUrl,
        CheckOutPhotoUrl = b.CheckOutPhotoUrl,
        ServiceCategoryId = b.ServiceCategoryId,
        Note = b.Note,
        Quantity = b.Quantity,
        Latitude = b.Latitude,
        Longitude = b.Longitude,
        UpdatedAt = b.UpdatedAt,
        MatchRound = b.MatchRound,
        CurrentWave = b.CurrentWave,
        CancellationFee = b.CancellationFee,
        DisputeId = b.Dispute?.Id,
        CanDispute = DisputeService.CanDispute(b, nowUtc, disputeHours),
        // Số liên hệ khách nhập khi đặt đơn được ưu tiên hơn SĐT trong hồ sơ
        CustomerPhone = showContact ? b.ContactPhone ?? b.Customer?.PhoneNumber : null,
        TaskerPhone = showContact ? b.Tasker?.PhoneNumber : null,
        CanReview = canReview,
        ContactPhone = isCustomer || showContact ? b.ContactPhone ?? b.Customer?.PhoneNumber : null,
    };
}
