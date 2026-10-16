namespace HomeService.Api.Common;

/// <summary>
/// US06 – tên vai trò dùng trong [Authorize(Roles = ...)] (khớp claim role trong JWT = tên enum UserRole).
/// Bảng quyền từng API: SRS mục 9.1 (Public / Auth / C / T / A).
/// </summary>
public static class Roles
{
    public const string Customer = "Customer";
    public const string Tasker = "Tasker";
    public const string Admin = "Admin";
    public const string CustomerOrTasker = Customer + "," + Tasker;
    public const string CustomerOrAdmin = Customer + "," + Admin;
}
