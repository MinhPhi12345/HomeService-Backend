using HomeService.Api.DTOs;
using HomeService.Api.Models;

namespace HomeService.Api.Services;

public static class BookingMapper
{
    /// <summary>Cần Include: Customer, Tasker, ServiceCategory, Payment.</summary>
    public static BookingResponse ToResponse(Booking b, DateTime nowUtc) => new()
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
        ServiceCategoryId = b.ServiceCategoryId,
        Note = b.Note,
        Quantity = b.Quantity,
        Latitude = b.Latitude,
        Longitude = b.Longitude,
        UpdatedAt = b.UpdatedAt,
        MatchRound = b.MatchRound,
        CurrentWave = b.CurrentWave,
    };
}
