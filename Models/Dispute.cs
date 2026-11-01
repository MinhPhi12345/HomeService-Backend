using HomeService.Api.Enums;

namespace HomeService.Api.Models;

// US35 – khiếu nại & tranh chấp (SRS 7.11). Mỗi đơn tối đa 1 khiếu nại.
public class Dispute
{
    public int Id { get; set; }

    public int BookingId { get; set; }
    public Booking? Booking { get; set; }

    public int CustomerId { get; set; }
    public User? Customer { get; set; }

    public int TaskerId { get; set; }
    public User? Tasker { get; set; }

    public DisputeReason Reason { get; set; }
    public string Description { get; set; } = string.Empty;
    public string EvidenceUrls { get; set; } = string.Empty;      // URL cách nhau dấu phẩy
    public string? TaskerResponse { get; set; }
    public string? TaskerEvidenceUrls { get; set; }
    public DateTime? TaskerRespondedAt { get; set; }

    public DisputeStatus Status { get; set; } = DisputeStatus.Open;
    public DisputeResolution? Resolution { get; set; }
    public decimal? RefundAmount { get; set; }
    public DateTime? RedoScheduledTime { get; set; }
    public string? AdminNote { get; set; }

    public int? ResolvedById { get; set; }
    public User? ResolvedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }

    public static string JoinUrls(IEnumerable<string>? urls) =>
        string.Join(',', (urls ?? Array.Empty<string>()).Select(u => Common.UploadUrl.Normalize(u) ?? string.Empty).Where(u => u.Length > 0));

    public static List<string> SplitUrls(string? urls) =>
        string.IsNullOrWhiteSpace(urls) ? new List<string>() : urls.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
}
