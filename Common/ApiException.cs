namespace HomeService.Api.Common;

/// <summary>
/// Lỗi nghiệp vụ có mã HTTP rõ ràng (400/403/404/409...).
/// Service ném ApiException, ExceptionHandlingMiddleware chuyển thành JSON { "message": "..." } theo SRS mục 8.6.
/// </summary>
public class ApiException : Exception
{
    public int StatusCode { get; }

    public ApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static ApiException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);
    public static ApiException Conflict(string message) => new(StatusCodes.Status409Conflict, message);
    public static ApiException Forbidden() => new(StatusCodes.Status403Forbidden, string.Empty);
    public static ApiException NotFound() => new(StatusCodes.Status404NotFound, string.Empty);
}
