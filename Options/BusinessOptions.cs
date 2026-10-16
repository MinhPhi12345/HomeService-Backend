namespace HomeService.Api.Options;

// appsettings.json → "Business"
public class BusinessOptions
{
    public const string Section = "Business";

    /// <summary>Tỷ lệ chiết khấu nền tảng (BR-P01), mặc định 20%. Được chốt vào đơn lúc tạo đơn.</summary>
    public decimal CommissionRate { get; set; } = 0.2m;
}
