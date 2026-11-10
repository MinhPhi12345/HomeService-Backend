using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Options;

namespace HomeService.Api.Services;

public static class BookingMapper
{
    public const int DefaultDisputeHours = 24;

    /// <summary>Cần Include: Customer, Tasker, ServiceCategory, Payment, Dispute, Reviews.</summary>
    public static BookingResponse ToResponse(Booking b, DateTime nowUtc, int? viewerId = null, int disputeHours = DefaultDisputeHours,
        int searchStopMinutes = MatchingOptions.DefaultStopSearchMinutes)
    {
        var activeJob = b.Status is BookingStatus.Accepted or BookingStatus.InProgress;
        var showContact = activeJob && viewerId is not null && (viewerId == b.TaskerId || viewerId == b.CustomerId);
        var finished = b.Status is BookingStatus.Completed or BookingStatus.Paid;
        var isParty = viewerId is not null && b.TaskerId is not null && (viewerId == b.TaskerId || viewerId == b.CustomerId);
        var canReview = finished && isParty && b.Reviews.All(r => r.ReviewerId != viewerId);
        var isCustomer = viewerId is not null && viewerId == b.CustomerId;
        var r = Map(b, nowUtc, disputeHours, showContact, canReview, isCustomer);
        // BR-M05/M07: đơn đã quét hết mà chưa ai nhận, hệ thống vẫn đang tìm tiếp
        var searching = b.TaskerId == null && b.SearchExhaustedAt != null &&
                        b.Status is BookingStatus.Pending or BookingStatus.Matched;
        if (searching)
        {
            r.SearchExhaustedAt = b.SearchExhaustedAt;
            r.NextSearchAt = b.Status == BookingStatus.Pending ? b.NextMatchAt : null;
            r.SearchStopsAt = b.ScheduledTime.AddMinutes(-searchStopMinutes);
            r.SearchNowAvailableAt = b.ManualSearchAvailableAt;
        }
        return r;
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
        DiscountAmount = b.DiscountAmount,
        PayableAmount = b.PayableAmount,
        VoucherCode = b.VoucherCode,
        EstimatedMinutes = b.EstimatedMinutes,
        ServiceUnit = b.ServiceCategory?.Unit ?? string.Empty,
        RequiresItemList = b.ServiceCategory?.RequiresItemList == true && b.Items.Count > 0,
        Items = b.Items.OrderBy(i => i.ItemNo).Select(i => new BookingItemDto(i.ItemNo, i.Description)).ToList(),
        Photos = PhotosOf(b),
    };

    public static List<BookingPhotoDto> PhotosOf(Booking b)
    {
        var photos = b.Photos.OrderBy(p => p.Stage).ThenBy(p => p.ItemNo ?? 0).ThenBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Select(p => new BookingPhotoDto(p.Stage, p.ItemNo, p.Url, p.Note, p.CreatedAt)).ToList();
        // Đơn cũ (trước SRS v1.8) chỉ có 1 ảnh mỗi giai đoạn trong CheckIn/CheckOutPhotoUrl
        if (photos.All(p => p.Stage != BookingPhotoStage.CheckIn) && b.CheckInPhotoUrl is not null)
            photos.Insert(0, new BookingPhotoDto(BookingPhotoStage.CheckIn, null, b.CheckInPhotoUrl, null, b.StartedAt ?? b.CreatedAt));
        if (photos.All(p => p.Stage != BookingPhotoStage.CheckOut) && b.CheckOutPhotoUrl is not null)
            photos.Add(new BookingPhotoDto(BookingPhotoStage.CheckOut, null, b.CheckOutPhotoUrl, null, b.CompletedAt ?? b.CreatedAt));
        return photos;
    }
}
