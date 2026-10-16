namespace HomeService.Api.Common;

public static class Money
{
    /// <summary>Làm tròn đến đồng (BR-P01, NFR-14).</summary>
    public static decimal Round(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    /// <summary>Hoa hồng = làm tròn(amount × commissionRate).</summary>
    public static decimal Commission(decimal amount, decimal rate) => Round(amount * rate);
}
