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
    public int AdvanceRetryMinutes { get; set; } = 30;
    public double StopRetryHoursBefore { get; set; } = 2;
    public int TickSeconds { get; set; } = 5;
    public int MaxRounds { get; set; } = 5;
    /// <summary>BR-B02: một Tasker không nhận 2 đơn có giờ hẹn cách nhau dưới số giờ này.</summary>
    public double ScheduleGapHours { get; set; } = 2;
}
