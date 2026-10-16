using System.Globalization;

namespace HomeService.Api.Common;

public static class MoneyFormat
{
    /// <summary>450000 → "450.000 đ" (dùng trong nội dung thông báo; không phụ thuộc culture của server).</summary>
    public static string Vnd(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", ".") + " đ";
}
