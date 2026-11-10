namespace HomeService.Api.Options;

// appsettings.json → "Matching" (FR-12). Mọi tham số ghép nối lấy từ cấu hình, không hardcode.
public class MatchingOptions
{
    public const string Section = "Matching";

    // Không gán giá trị mặc định cho mảng: ConfigurationBinder sẽ NỐI THÊM vào mảng có sẵn.
    public double[] WaveRadiusKm { get; set; } = Array.Empty<double>();
    public int[] WaveTimeoutSeconds { get; set; } = Array.Empty<int>();

    public double[] Radii => WaveRadiusKm.Length > 0 ? WaveRadiusKm : new double[] { 3, 5, 8 };
    public int[] Timeouts => WaveTimeoutSeconds.Length > 0 ? WaveTimeoutSeconds : new[] { 45, 45, 60 };
    public int OffersPerWave { get; set; } = 5;
    public int LocationFreshMinutes { get; set; } = 60;
    /// <summary>BR-M05: đã quét hết các đợt mà chưa ai nhận → tự quét lại sau số phút này (giờ hẹn còn xa).</summary>
    public int AdvanceRetryMinutes { get; set; } = 30;
    /// <summary>BR-M05: giờ hẹn còn trong NearWindowHours giờ → quét lại dày hơn, sau số phút này.</summary>
    public int NearRetryMinutes { get; set; } = 5;
    public double NearWindowHours { get; set; } = 2;
    public const int DefaultStopSearchMinutes = 45;
    /// <summary>BR-M05: còn dưới số phút này trước giờ hẹn mà chưa có Tasker → dừng tìm, đơn chuyển "Chưa có Tasker nhận".</summary>
    public int StopSearchMinutesBefore { get; set; } = DefaultStopSearchMinutes;
    /// <summary>BR-M07: khoảng cách tối thiểu giữa 2 lần khách bấm "Tìm lại ngay".</summary>
    public int ManualSearchCooldownMinutes { get; set; } = 5;
    public int TickSeconds { get; set; } = 5;
    public int MaxRounds { get; set; } = 5;
    /// <summary>
    /// BR-B02 (SRS v1.8): mỗi đơn chiếm lịch Tasker từ giờ hẹn đến giờ hẹn + thời lượng ước tính + số phút di chuyển này.
    /// Hai đơn của cùng Tasker không được chồng nhau.
    /// </summary>
    public int TravelBufferMinutes { get; set; } = 30;
    /// <summary>
    /// BR-P04 (bổ sung): ký quỹ – Tasker phải có số dư KHẢ DỤNG tối thiểu bằng mức này mới được bật nhận đơn,
    /// nhận lời mời và nhận đơn (mọi phương thức). Đơn tiền mặt còn phải đủ tiền chiết khấu.
    /// </summary>
    public decimal MinWalletDeposit { get; set; } = 100_000m;
}
