namespace HomeService.Api.DTOs;

// US07/US08
public record UpsertServiceCategoryRequest(string Name, string? Description, decimal BasePrice, string Unit, string? IconUrl);

public record ServiceCategoryResponse(int Id, string Name, string? Description, decimal BasePrice, string Unit, string? IconUrl, bool IsActive);
