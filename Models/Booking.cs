using HomeService.Api.Enums;

namespace HomeService.Api.Models;

// EP03 + EP04 - Đặt dịch vụ, ghép nối & theo dõi (US12-US20)
public class Booking
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public User? Customer { get; set; }

    public int ServiceCategoryId { get; set; }
    public ServiceCategory? ServiceCategory { get; set; }

    public int? TaskerId { get; set; } // null cho đến khi được ghép nối (US16)
    public User? Tasker { get; set; }

    public DateTime ScheduledTime { get; set; } // US13
    public string Address { get; set; } = string.Empty; // US13
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public string? Note { get; set; }
    public decimal TotalPrice { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Payment? Payment { get; set; }
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}
