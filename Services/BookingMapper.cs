using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;

namespace HomeService.Api.Services;

public static class BookingMapper
{
    public const int DefaultDisputeHours = 24;

    /// <summary>Cần Include: Customer, Tasker, ServiceCategory, Payment, Dispute.</summary>
    public static BookingResponse ToResponse(Booking b, DateTime nowUtc, int? viewerId = null, int disputeHours = DefaultDisputeHours)
    {
        var activeJob = b.Status is BookingStatus.Accepted or BookingStatus.InProgress;
        var showContact = activeJob && viewerId is not null && (viewerId == b.TaskerId || viewerId == b.CustomerId);
        return Map(b, nowUtc, disputeHours, showContact);
    }

    private static BookingResponse Map(Booking b, DateTime nowUtc, int disputeHours, bool showContact) => new()
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
        CustomerPhone = showContact ? b.Customer?.PhoneNumber : null,
        TaskerPhone = showContact ? b.Tasker?.PhoneNumber : null,
    };
}
