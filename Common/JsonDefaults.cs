using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace HomeService.Api.Common;

public static class JsonDefaults
{
    // Giữ nguyên tiếng Việt có dấu trong JSON trả về (dễ đọc khi test bằng Postman/Swagger)
    public static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = Encoder,
    };
}
