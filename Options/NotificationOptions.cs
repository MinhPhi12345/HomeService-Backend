namespace HomeService.Api.Options;

// appsettings.json → "Notifications" (FR-21)
public class NotificationOptions
{
    public const string Section = "Notifications";

    /// <summary>US29 – nhắc lịch trước giờ hẹn (phút) cho cả Khách hàng và Tasker.</summary>
    public int AppointmentReminderMinutes { get; set; } = 60;
}
