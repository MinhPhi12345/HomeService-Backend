using System.Net;
using System.Text.RegularExpressions;
using HomeService.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace HomeService.Api.Tests.Infrastructure;

/// <summary>Giả lập máy chủ OpenStreetMap / Nominatim: trả nội dung cố định và đếm số lần gọi.</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public List<string> Requests { get; } = new();

    public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.ToString());
        return Task.FromResult(_respond(request));
    }

    public static HttpResponseMessage Bytes(byte[] content) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
    public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    public static HttpResponseMessage Status(HttpStatusCode code) => new(code);
}

public sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

public sealed class FakeWebHostEnvironment : IWebHostEnvironment
{
    public FakeWebHostEnvironment(string root)
    {
        ContentRootPath = root;
        WebRootPath = Path.Combine(root, "wwwroot");
    }

    public string WebRootPath { get; set; }
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ApplicationName { get; set; } = "HomeService.Api";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; }
    public string EnvironmentName { get; set; } = "Testing";
}

/// <summary>Thư mục tạm, tự xóa khi test xong.</summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hs-test-" + Guid.NewGuid().ToString("N"));
    public TempFolder() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Giả lập gửi email: lưu lại các email đã "gửi" để test đọc mã OTP; có thể bật lỗi gửi.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    public sealed record Sent(string To, string Subject, string Html, string Text);

    public List<Sent> Messages { get; } = new();
    public bool Fail { get; set; }

    public Task SendAsync(string toEmail, string toName, string subject, string html, string text, CancellationToken ct = default)
    {
        if (Fail) throw new InvalidOperationException("SMTP lỗi (giả lập)");
        Messages.Add(new Sent(toEmail, subject, html, text));
        return Task.CompletedTask;
    }

    /// <summary>Mã OTP 6 số trong email gần nhất gửi tới địa chỉ này.</summary>
    public string LastCode(string? to = null)
    {
        var mail = to is null ? Messages.Last() : Messages.Last(m => m.To == to);
        return Regex.Match(mail.Text, @"\b\d{6}\b").Value;
    }
}
