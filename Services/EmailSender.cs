using System.Net;
using System.Net.Mail;
using System.Text;
using HomeService.Api.Options;
using Microsoft.Extensions.Options;

namespace HomeService.Api.Services;

public interface IEmailSender
{
    /// <summary>Gửi 1 email (HTML + bản chữ thuần). Lỗi gửi thì ném exception.</summary>
    Task SendAsync(string toEmail, string toName, string subject, string html, string text, CancellationToken ct = default);
}

/// <summary>
/// Gửi email qua SMTP (Gmail: smtp.gmail.com:587, STARTTLS, đăng nhập bằng mật khẩu ứng dụng).
/// Email:Enabled = false thì chỉ ghi nội dung ra log (môi trường phát triển chưa cấu hình SMTP).
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _opt;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _opt = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string toName, string subject, string html, string text, CancellationToken ct = default)
    {
        if (!_opt.Enabled)
        {
            _logger.LogWarning("[Email chưa bật – không gửi thật] Tới {To} | {Subject}\n{Text}", toEmail, subject, text);
            return;
        }
        if (string.IsNullOrWhiteSpace(_opt.Username) || string.IsNullOrWhiteSpace(_opt.Password))
            throw new InvalidOperationException("Chưa cấu hình Email:Username / Email:Password.");

        var from = string.IsNullOrWhiteSpace(_opt.FromAddress) ? _opt.Username! : _opt.FromAddress!;
        using var message = new MailMessage
        {
            From = new MailAddress(from, _opt.FromName, Encoding.UTF8),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            HeadersEncoding = Encoding.UTF8,
        };
        message.To.Add(new MailAddress(toEmail, toName, Encoding.UTF8));
        // Bản chữ thuần cho trình đọc mail không hỗ trợ HTML, bản HTML hiển thị đẹp
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(text, Encoding.UTF8, "text/plain"));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, "text/html"));

        using var client = new SmtpClient(_opt.Host, _opt.Port)
        {
            EnableSsl = _opt.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_opt.Username, _opt.Password),
            Timeout = Math.Max(5, _opt.TimeoutSeconds) * 1000,
        };
        await client.SendMailAsync(message, ct);
        _logger.LogInformation("Đã gửi email \"{Subject}\" tới {To}", subject, toEmail);
    }
}
