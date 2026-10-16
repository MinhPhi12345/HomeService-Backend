namespace HomeService.Api.Common;

public static class SkillMatcher
{
    /// <summary>
    /// Kiểm tra Tasker có kỹ năng phù hợp dịch vụ (lọc cứng – BR-M01).
    /// Skills lưu dạng "Dọn dẹp, Sửa điện nước". Một kỹ năng khớp khi tên dịch vụ chứa kỹ năng đó
    /// hoặc ngược lại, không phân biệt hoa thường (vd: "Dọn dẹp" khớp dịch vụ "Dọn dẹp nhà").
    /// </summary>
    public static bool Matches(string? skills, string serviceName)
    {
        if (string.IsNullOrWhiteSpace(skills)) return false;
        var service = serviceName.Trim();
        return skills.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(skill => service.Contains(skill, StringComparison.OrdinalIgnoreCase)
                          || skill.Contains(service, StringComparison.OrdinalIgnoreCase));
    }
}
