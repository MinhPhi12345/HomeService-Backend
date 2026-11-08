namespace HomeService.Api.Options;

/// <summary>Cấu hình Tasker xuất sắc tháng (FR-31, BR-T01 → BR-T04). Mục "Awards" trong appsettings.json.</summary>
public class AwardOptions
{
    public const string Section = "Awards";

    public int TopCount { get; set; } = 10;
    public int MinCompletedBookings { get; set; } = 10;
    public int MinReviews { get; set; } = 5;

    // Tỷ trọng chấm điểm (tổng = 1)
    public decimal RatingWeight { get; set; } = 0.40m;
    public decimal CompletedWeight { get; set; } = 0.25m;
    public decimal AcceptanceWeight { get; set; } = 0.15m;
    public decimal OnTimeWeight { get; set; } = 0.10m;
    public decimal NoDisputeWeight { get; set; } = 0.10m;

    /// <summary>Trung bình có trọng số: coi như mỗi Tasker có sẵn N đánh giá ở mức RatingPriorMean.</summary>
    public int RatingPriorCount { get; set; } = 5;
    public decimal RatingPriorMean { get; set; } = 4.5m;

    /// <summary>Bắt đầu làm (check-in) trễ hơn giờ hẹn quá số phút này thì tính là trễ.</summary>
    public int OnTimeGraceMinutes { get; set; } = 15;

    /// <summary>
    /// Phần thưởng mặc định theo hạng (Admin chỉnh được từng người trước khi duyệt). Để trống trong appsettings
    /// → dùng DefaultTiers (danh sách khởi tạo rỗng vì trình đọc cấu hình cộng dồn vào danh sách có sẵn).
    /// </summary>
    public List<AwardTier> Tiers { get; set; } = new();

    public static readonly IReadOnlyList<AwardTier> DefaultTiers = new List<AwardTier>
    {
        new() { FromRank = 1, ToRank = 1, Bonus = 1_000_000m, CommissionRate = 0.10m, Gift = true },
        new() { FromRank = 2, ToRank = 3, Bonus = 500_000m, CommissionRate = 0.15m, Gift = false },
        new() { FromRank = 4, ToRank = 10, Bonus = 0m, CommissionRate = null, Gift = false },
    };

    public IReadOnlyList<AwardTier> EffectiveTiers => Tiers.Count > 0 ? Tiers : DefaultTiers;

    public AwardTier? TierFor(int rank) => EffectiveTiers.FirstOrDefault(t => rank >= t.FromRank && rank <= t.ToRank);
}

public class AwardTier
{
    public int FromRank { get; set; }
    public int ToRank { get; set; }
    public decimal Bonus { get; set; }
    public decimal? CommissionRate { get; set; }
    public bool Gift { get; set; }
}

/// <summary>Khuyến mãi khách hàng (FR-32, FR-33). Mục "Promotions" trong appsettings.json.</summary>
public class PromotionOptions
{
    public const string Section = "Promotions";

    /// <summary>Sau khi giảm, khách vẫn phải trả tối thiểu số tiền này (BR-V02).</summary>
    public decimal MinPayable { get; set; } = 10_000m;
}
