using HomeService.Api.Enums;

namespace HomeService.Api.Models;

// US16/US17 – lời mời nhận đơn gửi cho từng Tasker theo đợt (SRS 7.11)
public class BookingOffer
{
    public int Id { get; set; }

    public int BookingId { get; set; }
    public Booking? Booking { get; set; }

    public int TaskerId { get; set; }
    public User? Tasker { get; set; }

    public int Round { get; set; }   // = Booking.MatchRound lúc gửi; UNIQUE (BookingId, TaskerId, Round)
    public int Wave { get; set; }    // 1, 2 hoặc 3
    public decimal DistanceKm { get; set; }
    public decimal Score { get; set; }

    public OfferStatus Status { get; set; } = OfferStatus.Sent;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}
