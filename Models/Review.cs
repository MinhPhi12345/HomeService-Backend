namespace HomeService.Api.Models;

// EP06 - Đánh giá & Nhận xét (US25-US27)
public class Review
{
    public int Id { get; set; }

    public int BookingId { get; set; }
    public Booking? Booking { get; set; }

    public int ReviewerId { get; set; } // người đánh giá
    public User? Reviewer { get; set; }

    public int RevieweeId { get; set; } // người được đánh giá
    public User? Reviewee { get; set; }

    public int Rating { get; set; } // 1-5
    public string? Comment { get; set; }
    public bool IsFlagged { get; set; } = false; // US27: đánh dấu để Admin xử lý khiếu nại

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
