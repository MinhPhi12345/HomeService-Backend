using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// US12/US13
public record CreateBookingRequest(int ServiceCategoryId, DateTime ScheduledTime, string Address, double? Latitude, double? Longitude, string? Note);

// US18
public record UpdateBookingStatusRequest(BookingStatus Status);

public record BookingResponse(
    int Id,
    string ServiceCategoryName,
    int CustomerId,
    string CustomerName,
    int? TaskerId,
    string? TaskerName,
    DateTime ScheduledTime,
    string Address,
    decimal TotalPrice,
    BookingStatus Status,
    DateTime CreatedAt
);
